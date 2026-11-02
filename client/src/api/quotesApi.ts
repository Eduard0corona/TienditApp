import { apiRequest, type Schemas } from './typedClient';
import type { QuoteListFilter } from '../types';

// The quotes of the business (Fase 29C, ADR 0053) and the link a customer opens without an account (/cotizacion#link). The
// server works out the taxes and the totals: the browser sends lines, never a total.

export type Quote = Schemas['QuoteDto'];
export type QuoteSummary = Schemas['QuoteSummaryDto'];
export type QuoteLine = Schemas['QuoteLineDto'];
export type QuoteLineRequest = Schemas['QuoteLineRequest'];
export type QuoteSettings = Schemas['QuoteSettingsDto'];
export type UpdateQuoteDraft = Schemas['UpdateQuoteDraftRequest'];
export type CreateQuote = Schemas['CreateQuoteCommand'];
export type QuoteReply = Schemas['RecordQuoteReplyRequest'];
export type PublicQuote = Schemas['PublicQuoteDto'];
export type QuoteEmail = Schemas['QuoteEmailDto'];
export type QuoteWhatsApp = Schemas['QuoteWhatsAppDto'];
export type QuoteWhatsAppTemplate = Schemas['QuoteWhatsAppTemplateDto'];
export type QuoteOrder = Schemas['QuoteOrderDto'];

export const quotesApi = {
  list: (filter: QuoteListFilter, search: string, pageNumber: number, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/quotes', { query: { filter, search: search || undefined, pageNumber, pageSize: 20 }, signal }),

  get: (id: string, signal?: AbortSignal) => apiRequest('get', '/api/v1/quotes/{id}', { path: { id }, signal }),

  settings: (signal?: AbortSignal) => apiRequest('get', '/api/v1/quotes/settings', { signal }),

  /** How many days a quote is valid once sent (owners and admins). */
  updateSettings: (validityDays: number) => apiRequest('put', '/api/v1/quotes/settings', { body: { validityDays } }),

  /** A draft for a customer, or for the visit of an appointment (which opens its quote when it has one). */
  create: (body: CreateQuote) => apiRequest('post', '/api/v1/quotes', { body }),

  updateDraft: (id: string, body: UpdateQuoteDraft) => apiRequest('put', '/api/v1/quotes/{id}', { path: { id }, body }),

  send: (id: string) => apiRequest('post', '/api/v1/quotes/{id}/send', { path: { id } }),

  revise: (id: string) => apiRequest('post', '/api/v1/quotes/{id}/revise', { path: { id } }),

  discard: (id: string) => apiRequest('post', '/api/v1/quotes/{id}/discard', { path: { id } }),

  reply: (id: string, body: QuoteReply) => apiRequest('post', '/api/v1/quotes/{id}/reply', { path: { id }, body }),

  /** Sends a valid sent version by email from TienditApp, in the name of the business (Fase 29D). */
  sendByEmail: (id: string, email: string) => apiRequest('post', '/api/v1/quotes/{id}/email', { path: { id }, body: { email } }),

  /** Sends a valid sent version by WhatsApp from the business's number (Fase 29D). */
  sendByWhatsApp: (id: string, phone: string) => apiRequest('post', '/api/v1/quotes/{id}/whatsapp', { path: { id }, body: { phone } }),

  /**
   * The accepted version becomes its service order (Fase 29E), once, at the accepted prices; what was paid for the visit
   * becomes a payment of the order. <paramref name="promisedAt"/> is when the job is promised (optional).
   */
  convertToOrder: (id: string, promisedAt: string | null) =>
    apiRequest('post', '/api/v1/quotes/{id}/order', { path: { id }, body: { promisedAt } }),

  /** The WhatsApp template the quotes go out with, or TienditApp's text (null): owners and admins. */
  setWhatsAppTemplate: (templateId: string | null) =>
    apiRequest('put', '/api/v1/quotes/settings/whatsapp-template', { body: { templateId } }),
};

export const publicQuoteApi = {
  /** The quote of the link; unknown, expired or of a business no longer active, it is not found (404). */
  view: (token: string, signal?: AbortSignal) => apiRequest('post', '/api/v1/public/quotes/view', { body: { token }, signal }),
};
