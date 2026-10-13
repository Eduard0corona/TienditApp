import type { OrderChannel } from '../types';
import { DELIVERY_PLATFORM_CHANNELS, ORDER_CHANNEL_LABELS } from './labels';

// Orders of Rappi, Uber Eats and DiDi Food: the business registers in Vender, with its folio, an order the platform's
// app already took. The server decides everything about them (the platforms active in the business, the folio, who
// charged, the commission); these helpers only make typing them easier and mirror its rules for quick feedback.

/** Rappi, Uber Eats or DiDi Food. */
export type DeliveryPlatform = (typeof DELIVERY_PLATFORM_CHANNELS)[number];

/** An order that came in through a delivery platform's app (OrderChannelRules.IsDeliveryPlatform). */
export function isDeliveryPlatform(channel: OrderChannel | null | undefined): channel is DeliveryPlatform {
  return DELIVERY_PLATFORM_CHANNELS.some((platform) => platform === channel);
}

/** The longest folio the server keeps (PlatformOrderNumber.MaxLength): DiDi's long id fits. */
export const PLATFORM_FOLIO_MAX_LENGTH = 30;
// What the server reads before normalizing (PlatformOrderNumber.MaxInputLength).
const FOLIO_MAX_INPUT_LENGTH = 60;
// Letters are checked before upper-casing: "ß" would become "SS", which the server never accepts.
const FOLIO_PATTERN = /^[A-Za-z0-9-]{1,30}$/;

export const PLATFORM_FOLIO_FORMAT_MESSAGE = 'El folio lleva de 1 a 30 letras, números o guiones.';
export const PLATFORM_ONLY_PRODUCTS_MESSAGE = 'Los pedidos de plataforma son de productos: quita los servicios del ticket.';

/** How each platform shows its folio, so the cashier finds it in the app and the phone opens the right keyboard. */
export interface PlatformFolioHint {
  label: string;
  placeholder: string;
  help: string;
  inputMode?: 'numeric';
  autoCapitalize?: 'characters';
}

const FOLIO_HINTS: Record<DeliveryPlatform, PlatformFolioHint> = {
  3: { label: 'Folio de Rappi', placeholder: 'Ej. 152390', help: 'El número de pedido que ves en Rappi Aliados.', inputMode: 'numeric' },
  4: {
    label: 'Folio de Uber Eats',
    placeholder: 'Ej. BC953',
    help: 'Los 5 caracteres del pedido en Uber Eats Orders.',
    autoCapitalize: 'characters',
  },
  5: { label: 'Folio de DiDi Food', placeholder: 'Ej. 12', help: 'El número de recolección o de pedido de DiDi Tienda.', inputMode: 'numeric' },
};

export function getPlatformFolioHint(platform: DeliveryPlatform): PlatformFolioHint {
  return FOLIO_HINTS[platform];
}

/** "Rappi", "Uber Eats", "DiDi Food". */
export function getPlatformName(platform: DeliveryPlatform): string {
  return ORDER_CHANNEL_LABELS[platform];
}

/** The folio without the spaces around and inside it nor one leading #, still as typed. */
function compactFolio(raw: string): string {
  const trimmed = raw.trim();
  return (trimmed.startsWith('#') ? trimmed.slice(1) : trimmed).replace(/\s/g, '');
}

/**
 * The folio as the server keeps it (PlatformOrderNumber): without the spaces around it, one leading # and the spaces
 * inside, in upper case. "  #bc 953 " is BC953.
 */
export function normalizePlatformFolio(raw: string): string {
  return compactFolio(raw).toUpperCase();
}

/** Whether the server would take the folio: 1 to 30 letters, digits or hyphens once normalized. */
export function isValidPlatformFolio(raw: string): boolean {
  return raw.length <= FOLIO_MAX_INPUT_LENGTH && FOLIO_PATTERN.test(compactFolio(raw));
}

/** What is wrong with the folio typed for an order of the platform, or '' when the server would take it. */
export function getPlatformFolioProblem(platform: DeliveryPlatform, raw: string): string {
  if (!raw.trim()) return `Escribe el folio de ${getPlatformName(platform)}.`;
  return isValidPlatformFolio(raw) ? '' : PLATFORM_FOLIO_FORMAT_MESSAGE;
}
