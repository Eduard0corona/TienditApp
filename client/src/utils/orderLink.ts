import { cartLineKey, linesOfOrder, MAX_CART_LINES, MAX_LINE_QUANTITY, type SavedCartLine } from './savedCart';

/**
 * The parameter of a link that fills a store's cart (Fase 32A, ADR 0096): `?agregar={producto}:{cantidad}[:{opción}…]`,
 * one per product. An AI assistant builds it from the store's menu in text (/m/{slug}/menu.md, AgentPages.cs); the
 * customer opens it, reviews the order and sends it from the store.
 */
export const ORDER_LINK_PARAMETER = 'agregar';

/** An id of the menu as it travels in the link: letters, digits and dashes (the API's are uuids, in lowercase). */
const ID = /^[a-z0-9-]{1,64}$/;

/** What a link asks to put in the cart. */
export interface OrderLink {
  /** The products, the same one with the same options added up, at most what a cart takes. */
  lines: SavedCartLine[];
  /** Parts of the link that are not a product: a bad id or quantity, or past what a cart takes. */
  invalid: number;
}

/**
 * The cart a link asks for, from the values of its `agregar` parameters; null when it has none. A quantity is a whole
 * number from 1 (without one, 1) and up to what a line takes; an option goes once (the API refuses one twice); the menu
 * says later which products and options exist.
 */
export function orderLinkOf(values: string[]): OrderLink | null {
  if (values.length === 0) return null;
  const items: Array<{ productId: string; quantity: number; optionIds: string[] }> = [];
  let invalid = 0;
  for (const value of values) {
    const [productId = '', quantityText, ...optionIds] = value.trim().toLowerCase().split(':');
    const quantity = quantityText === undefined || quantityText === '' ? 1 : Number(quantityText);
    if (!ID.test(productId) || !Number.isInteger(quantity) || quantity <= 0 || optionIds.some((id) => !ID.test(id))) {
      invalid += 1;
      continue;
    }
    items.push({ productId, quantity: Math.min(quantity, MAX_LINE_QUANTITY), optionIds: [...new Set(optionIds)] });
  }
  const distinct = new Set(items.map((item) => cartLineKey(item.productId, item.optionIds))).size;
  const { lines } = linesOfOrder(items);
  return { lines, invalid: invalid + Math.max(0, distinct - MAX_CART_LINES) };
}

/** What happened to a link once the store read its menu. */
export interface OrderLinkOutcome {
  /** The products that went in the cart. */
  kept: number;
  /** Products the menu does not have now, or without an option they need. */
  notOnMenu: number;
  /** Parts of the link that are not a product. */
  invalid: number;
  /** The cart had other products, and the link's took their place. */
  replaced: boolean;
  /** The store takes orders, but not now: the order waits on this device until it opens. */
  paused: boolean;
}

const plural = (count: number, one: string, many: string) => (count === 1 ? one : many.replace('{n}', String(count)));

/** What the store says of a link: nothing when everything went in the cart and nothing was there before. */
export function orderLinkMessage({ kept, notOnMenu, invalid, replaced, paused }: OrderLinkOutcome): string {
  const missing = [
    notOnMenu > 0 ? plural(notOnMenu, '1 producto ya no está en el menú o le falta elegir una opción', '{n} productos ya no están en el menú o les falta elegir una opción') : '',
    invalid > 0 ? plural(invalid, '1 parte del enlace no se entiende (un id o una cantidad que no es válida)', '{n} partes del enlace no se entienden (un id o una cantidad que no es válida)') : '',
  ].filter(Boolean).join('; ');
  if (kept === 0) return `No pudimos agregar los productos del enlace: ${missing}. Mira el menú.`;
  const said = [
    replaced ? 'Cambiamos el pedido que tenías por el del enlace.' : '',
    paused ? 'Guardamos el pedido del enlace: envíalo cuando este negocio vuelva a recibir pedidos.' : '',
    missing ? `${paused ? 'No agregamos todo' : 'Agregamos lo que sigue disponible'}; ${missing}.` : '',
  ].filter(Boolean);
  return said.join(' ');
}
