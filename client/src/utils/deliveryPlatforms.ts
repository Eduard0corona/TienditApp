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

/** The longest long id of an order the server keeps (PlatformOrderId.MaxLength): a UUID of Uber Eats fits. */
export const PLATFORM_ORDER_ID_MAX_LENGTH = 64;
/** What the server reads of a long id before normalizing (PlatformOrderId.MaxInputLength): spaces may split its groups. */
export const PLATFORM_ORDER_ID_MAX_INPUT_LENGTH = 100;
const ORDER_ID_PATTERN = /^[A-Za-z0-9-]{6,64}$/;
export const PLATFORM_ORDER_ID_FORMAT_MESSAGE = 'El ID largo del pedido lleva de 6 a 64 letras, números o guiones.';
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

/** Where each platform shows the long id of an order, and the keyboard it takes (Rappi's and DiDi's are digits). */
export interface PlatformOrderIdHint {
  help: string;
  inputMode?: 'numeric';
}

// Its statements name the orders by it (Fase 22I).
const ORDER_ID_HINTS: Record<DeliveryPlatform, PlatformOrderIdHint> = {
  3: { help: 'El número de orden completo de Rappi Aliados o del estado de cuenta.', inputMode: 'numeric' },
  4: { help: 'El ID del pedido (con guiones) del estado de cuenta de Uber Eats.' },
  5: { help: 'El ID del pedido de DiDi Tienda o del estado de cuenta.', inputMode: 'numeric' },
};

export function getPlatformOrderIdHint(platform: DeliveryPlatform): PlatformOrderIdHint {
  return ORDER_ID_HINTS[platform];
}

/** "Rappi", "Uber Eats", "DiDi Food". */
export function getPlatformName(platform: DeliveryPlatform): string {
  return ORDER_CHANNEL_LABELS[platform];
}

/**
 * The folio without the spaces around and inside it nor one leading #, still as typed. A space is what the server takes
 * for one (`char.IsWhiteSpace`): the invisible U+FEFF a pasted text may carry is not, so the server refuses it, and so
 * does this.
 */
function compactFolio(raw: string): string {
  const trimmed = raw.replace(/^[^\S\uFEFF]+|[^\S\uFEFF]+$/g, '');
  return (trimmed.startsWith('#') ? trimmed.slice(1) : trimmed).replace(/[^\S\uFEFF]/g, '');
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

/** The long id as the server keeps it (PlatformOrderId): normalized as the folio. */
export function normalizePlatformOrderId(raw: string): string {
  return compactFolio(raw).toUpperCase();
}

/** What is wrong with the long id typed, or '' when it is empty (it is optional) or the server would take it. */
export function getPlatformOrderIdProblem(raw: string): string {
  if (!raw.trim()) return '';
  return raw.length <= PLATFORM_ORDER_ID_MAX_INPUT_LENGTH && ORDER_ID_PATTERN.test(compactFolio(raw))
    ? ''
    : PLATFORM_ORDER_ID_FORMAT_MESSAGE;
}

/** What is wrong with the folio typed for an order of the platform, or '' when the server would take it. */
export function getPlatformFolioProblem(platform: DeliveryPlatform, raw: string): string {
  if (!raw.trim()) return `Escribe el folio de ${getPlatformName(platform)}.`;
  return isValidPlatformFolio(raw) ? '' : PLATFORM_FOLIO_FORMAT_MESSAGE;
}
