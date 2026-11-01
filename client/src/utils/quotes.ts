import type { QuoteEmail, QuoteWhatsApp } from '../api/quotesApi';
import { OutgoingMessageStatus, QuoteEmailFailure, QuoteListFilter, QuoteReplyChannel, QuoteStatus } from '../types';
import { formatCalendarDate, formatDateTime } from './dates';
import { formatMoney } from './money';
import { businessWhatsAppUrl } from './whatsApp';

// How the quotes read (Fase 29C, ADR 0053). The status comes from the server as it stands today; nothing here decides it.

// The palette of the order states: new (amber) for a draft, confirmed (blue) once sent, ready (green) accepted, cancelled
// (red) rejected, neutral for what no longer counts. Always with text.
const STATUS: Record<QuoteStatus, { label: string; chip: string }> = {
  [QuoteStatus.Draft]: { label: 'Borrador', chip: 'ta-chip-new' },
  [QuoteStatus.Sent]: { label: 'Enviada', chip: 'ta-chip-confirmed' },
  [QuoteStatus.Accepted]: { label: 'Aceptada', chip: 'ta-chip-ready' },
  [QuoteStatus.Rejected]: { label: 'Rechazada', chip: 'ta-chip-cancelled' },
  [QuoteStatus.Expired]: { label: 'Vencida', chip: 'ta-chip-done' },
  [QuoteStatus.Superseded]: { label: 'Reemplazada', chip: 'ta-chip-done' },
  [QuoteStatus.Discarded]: { label: 'Descartada', chip: 'ta-chip-done' },
};

export function quoteStatusLabel(status: QuoteStatus): string {
  return STATUS[status]?.label ?? String(status);
}

/** The classes of its chip: `ta-chip` and its color. */
export function quoteStatusChip(status: QuoteStatus): string {
  return `ta-chip ${STATUS[status]?.chip ?? 'ta-chip-done'}`;
}

export const QUOTE_FILTERS: { value: QuoteListFilter; label: string }[] = [
  { value: QuoteListFilter.Open, label: 'Abiertas' },
  { value: QuoteListFilter.Accepted, label: 'Aceptadas' },
  { value: QuoteListFilter.Rejected, label: 'Rechazadas' },
  { value: QuoteListFilter.Expired, label: 'Vencidas' },
  { value: QuoteListFilter.Discarded, label: 'Descartadas' },
  { value: QuoteListFilter.All, label: 'Todas' },
];

export const REPLY_CHANNELS: { value: QuoteReplyChannel; label: string }[] = [
  { value: QuoteReplyChannel.WhatsApp, label: 'WhatsApp' },
  { value: QuoteReplyChannel.Email, label: 'Correo' },
  { value: QuoteReplyChannel.Phone, label: 'Teléfono' },
  { value: QuoteReplyChannel.InPerson, label: 'En persona' },
];

export function replyChannelLabel(channel: QuoteReplyChannel): string {
  return REPLY_CHANNELS.find((option) => option.value === channel)?.label ?? String(channel);
}

const REPLY_PHRASES: Record<QuoteReplyChannel, string> = {
  [QuoteReplyChannel.WhatsApp]: 'por WhatsApp',
  [QuoteReplyChannel.Email]: 'por correo',
  [QuoteReplyChannel.Phone]: 'por teléfono',
  [QuoteReplyChannel.InPerson]: 'en persona',
};

/** How the customer answered, after the verb: «Aceptó por WhatsApp», «Rechazó en persona». */
export function replyChannelPhrase(channel: QuoteReplyChannel): string {
  return REPLY_PHRASES[channel] ?? '';
}

/** «COT-000123» for the first version, «COT-000123 · versión 2» after it. */
export function quoteFolio(number: string, revision: number): string {
  return revision > 1 ? `${number} · versión ${revision}` : number;
}

/** The link the customer opens without an account: the token travels in the fragment, which never reaches a server. */
export function quoteLink(token: string, origin: string = window.location.origin): string {
  return `${origin}/cotizacion#${token}`;
}

/** What the business sends by WhatsApp or by email with the link: the folio, the total, until when and the link. */
export function quoteMessage(
  quote: { number: string; revision: number; toPay: number; validUntil?: string | null },
  businessName: string,
  link: string,
): string {
  const until = quote.validUntil ? ` Vale hasta el ${formatCalendarDate(quote.validUntil)}.` : '';
  const from = businessName.trim() ? ` de ${businessName.trim()}` : '';
  return `Hola, te comparto la cotización ${quoteFolio(quote.number, quote.revision)}${from} por ${formatMoney(quote.toPay)}.${until} Puedes verla aquí: ${link}`;
}

/** A wa.me link to the customer with the message ready, or null when their phone has fewer than ten digits. */
export function quoteWhatsAppUrl(phone: string, text: string): string | null {
  return businessWhatsAppUrl(phone, text);
}

/** A mailto link with the message, or null without an email. */
export function quoteMailUrl(email: string | null | undefined, subject: string, text: string): string | null {
  if (!email) return null;
  return `mailto:${encodeURIComponent(email)}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(text)}`;
}

// The sends of a version from TienditApp (Fase 29D): by email from the platform in the name of the business, and by
// WhatsApp from the business's number. Each one says where it is: on its way, sent (delivered, read), or not sent and why.

const EMAIL_FAILURES: Record<QuoteEmailFailure, string> = {
  [QuoteEmailFailure.Rejected]: 'el servidor de correo rechazó la dirección.',
  [QuoteEmailFailure.NotConfigured]: 'el correo de TienditApp no está configurado.',
  [QuoteEmailFailure.NotDelivered]: 'el servidor de correo no lo tomó después de varios intentos.',
};

export type QuoteSendState = 'pending' | 'sent' | 'failed';

export interface QuoteSendLine {
  key: string;
  channel: 'email' | 'whatsapp';
  state: QuoteSendState;
  requestedAt: string;
  text: string;
}

function emailLine(email: QuoteEmail): QuoteSendLine {
  const base = { key: `email-${email.id}`, channel: 'email' as const, requestedAt: email.requestedAt };
  if (email.sentAt) return { ...base, state: 'sent', text: `Se envió por correo a ${email.to} el ${formatDateTime(email.sentAt)}.` };
  if (email.failedAt) {
    const why = email.failure != null ? EMAIL_FAILURES[email.failure] : 'no se pudo enviar.';
    return { ...base, state: 'failed', text: `No se envió por correo a ${email.to}: ${why}` };
  }
  const retrying = email.failedAttempts > 0 ? ' El servidor de correo no lo ha tomado; TienditApp lo vuelve a intentar.' : '';
  return { ...base, state: 'pending', text: `Enviándose por correo a ${email.to}…${retrying}` };
}

function whatsAppLine(message: QuoteWhatsApp): QuoteSendLine {
  const base = { key: `whatsapp-${message.id}`, channel: 'whatsapp' as const, requestedAt: message.requestedAt };
  switch (message.status) {
    case OutgoingMessageStatus.Sent:
      if (message.readAt) return { ...base, state: 'sent', text: `Lo leyó en WhatsApp (${message.phone}) el ${formatDateTime(message.readAt)}.` };
      if (message.deliveredAt) return { ...base, state: 'sent', text: `Le llegó por WhatsApp a ${message.phone} el ${formatDateTime(message.deliveredAt)}.` };
      return { ...base, state: 'sent', text: `Se envió por WhatsApp a ${message.phone}${message.sentAt ? ` el ${formatDateTime(message.sentAt)}` : ''}.` };
    case OutgoingMessageStatus.Pending: {
      const retrying = message.attempts > 0 ? ' WhatsApp no lo ha tomado; TienditApp lo vuelve a intentar.' : '';
      return { ...base, state: 'pending', text: `Enviándose por WhatsApp a ${message.phone}…${retrying}` };
    }
    default:
      return { ...base, state: 'failed', text: `No se envió por WhatsApp a ${message.phone}${message.error ? `: ${message.error}` : '.'}` };
  }
}

/** The emails and WhatsApp messages of a version, the newest first. */
export function quoteSends(quote: { emails?: QuoteEmail[] | null; whatsApps?: QuoteWhatsApp[] | null }): QuoteSendLine[] {
  return [...(quote.emails ?? []).map(emailLine), ...(quote.whatsApps ?? []).map(whatsAppLine)]
    .sort((a, b) => b.requestedAt.localeCompare(a.requestedAt));
}

/** Something is still on its way: the detail asks again until it ends. */
export function hasPendingSends(quote: { emails?: QuoteEmail[] | null; whatsApps?: QuoteWhatsApp[] | null }): boolean {
  return quoteSends(quote).some((line) => line.state === 'pending');
}
