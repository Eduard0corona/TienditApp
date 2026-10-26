import { apiRequest, type Schemas } from './typedClient';
import type { CfdiReceiver } from './invoicing';

// Self-invoicing (Fase 28F, ADR 0050): the customer invoices the ticket on the business's page, without an account, with the
// folio and the code printed on it. They travel in the body, never in the URL: request telemetry keeps URLs.

export type SelfInvoicePage = Schemas['SelfInvoicePageDto'];
export type SelfInvoiceTicket = Schemas['SelfInvoiceTicketDto'];
export type SelfInvoiceResult = Schemas['SelfInvoiceResultDto'];
export type SelfInvoiceResend = Schemas['SelfInvoiceResendDto'];

/** The folio and the code printed on the ticket. */
export interface TicketKey {
  folio: string;
  code: string;
}

export const selfInvoiceApi = {
  /** The business and whether it invoices tickets online now; not found (404) when it does not exist. */
  getPage: (slug: string, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/public/self-invoice/{slug}', { path: { slug }, signal }),

  /** The ticket and whether it can be invoiced online; a folio that is not there and a code typed wrong are both 404. */
  lookUp: (slug: string, ticket: TicketKey) =>
    apiRequest('post', '/api/v1/public/self-invoice/{slug}/ticket', { path: { slug }, body: ticket }),

  /** Stamps the ticket's invoice: stamped, still stamping (it follows by email) or refused by the PAC with its reason. */
  invoice: (slug: string, ticket: TicketKey, receiver: CfdiReceiver, email: string) =>
    apiRequest('post', '/api/v1/public/self-invoice/{slug}/invoice', { path: { slug }, body: { ...ticket, receiver, email } }),

  /** Sends the ticket's invoice again, to whoever knows the RFC it was made for. */
  resend: (slug: string, ticket: TicketKey, rfc: string, email: string) =>
    apiRequest('post', '/api/v1/public/self-invoice/{slug}/resend', { path: { slug }, body: { ...ticket, rfc, email } }),
};

/**
 * The folio and code the ticket's QR carries in the fragment of the page's address (#ORD-000123.ABCDE-12345): the browser
 * never sends the fragment to a server.
 */
export function ticketKeyFromHash(hash: string): TicketKey {
  const raw = hash.replace(/^#/, '');
  let value = raw;
  try {
    value = decodeURIComponent(raw);
  } catch {
    // A fragment mangled on the way (a lone %) is read as typed: the lookup says whether it is a ticket.
  }
  const dot = value.lastIndexOf('.');
  return dot < 0 ? { folio: value.trim(), code: '' } : { folio: value.slice(0, dot).trim(), code: value.slice(dot + 1).trim() };
}

/** The page's address with the ticket in its fragment, for the QR printed on it. */
export function selfInvoiceUrl(path: string, ticket?: TicketKey): string {
  const url = new URL(path, window.location.origin);
  if (ticket) url.hash = `${encodeURIComponent(ticket.folio)}.${encodeURIComponent(ticket.code)}`;
  return url.toString();
}
