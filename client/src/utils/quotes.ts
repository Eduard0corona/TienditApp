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
  // Fase 29G: accepted in the last 30 days and not yet their order, as «Requiere atención» counts them.
  { value: QuoteListFilter.ToConvert, label: 'Por convertir' },
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
  if (channel === QuoteReplyChannel.Link) return 'Desde su enlace';
  return REPLY_CHANNELS.find((option) => option.value === channel)?.label ?? String(channel);
}

const REPLY_PHRASES: Record<QuoteReplyChannel, string> = {
  [QuoteReplyChannel.WhatsApp]: 'por WhatsApp',
  [QuoteReplyChannel.Email]: 'por correo',
  [QuoteReplyChannel.Phone]: 'por teléfono',
  [QuoteReplyChannel.InPerson]: 'en persona',
  // Fase 29G: the customer answered from the quote's link; the business never picks it.
  [QuoteReplyChannel.Link]: 'desde su enlace',
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

// The sends of a version from TienditApp (Fase 29D): by email from the platform in the name of the business, and by
// WhatsApp from the business's number. Each one says where it is: on its way, sent (delivered, read), or not sent and why.

const EMAIL_FAILURES: Record<QuoteEmailFailure, string> = {
  [QuoteEmailFailure.Rejected]: 'el servidor de correo rechazó la dirección.',
  [QuoteEmailFailure.NotConfigured]: 'el correo de TienditApp no está configurado.',
  [QuoteEmailFailure.NotDelivered]: 'el servidor de correo no lo tomó después de varios intentos.',
  [QuoteEmailFailure.NoLongerValid]: 'cuando iba a salir, la cotización ya no estaba vigente.',
};

/** How many of the latest sends of each channel the server gives (QuoteDeliveries.Shown). */
const SHOWN_PER_CHANNEL = 5;

/** How long after a WhatsApp went out the detail keeps asking: WhatsApp tells later whether it arrived or failed. */
const DELIVERY_WINDOW_MS = 5 * 60 * 1000;

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

type WithSends = { emails?: QuoteEmail[] | null; whatsApps?: QuoteWhatsApp[] | null };

/**
 * The emails and WhatsApp messages of a version, the newest first. The server gives the latest of each channel: when one
 * comes full, the list stops at its oldest, so an older send of the other channel never shows below newer ones it hid.
 */
export function quoteSends(quote: WithSends): QuoteSendLine[] {
  const emails = quote.emails ?? [];
  const whatsApps = quote.whatsApps ?? [];
  const cutOffs = [emails, whatsApps]
    .filter((sends) => sends.length >= SHOWN_PER_CHANNEL)
    .map((sends) => sends.reduce((oldest, send) => (send.requestedAt < oldest ? send.requestedAt : oldest), sends[0].requestedAt));
  const since = cutOffs.reduce<string | null>((latest, cutOff) => (latest === null || cutOff > latest ? cutOff : latest), null);
  return [...emails.map(emailLine), ...whatsApps.map(whatsAppLine)]
    .filter((line) => since === null || line.requestedAt >= since)
    .sort((a, b) => b.requestedAt.localeCompare(a.requestedAt));
}

/**
 * Something is still on its way, or a WhatsApp went out a moment ago and WhatsApp has not said whether it arrived: the
 * detail asks again until it knows.
 */
export function hasPendingSends(quote: WithSends, now: number = Date.now()): boolean {
  if (quoteSends(quote).some((line) => line.state === 'pending')) return true;
  return (quote.whatsApps ?? []).some((message) => message.status === OutgoingMessageStatus.Sent && !message.deliveredAt
    && !message.readAt && message.sentAt != null && now - Date.parse(message.sentAt) < DELIVERY_WINDOW_MS);
}
