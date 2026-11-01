import { QuoteListFilter, QuoteReplyChannel, QuoteStatus } from '../types';
import { formatCalendarDate } from './dates';
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
