import { cartLineKey, linesOfOrder, MAX_CART_LINES, MAX_LINE_QUANTITY, type SavedCartLine } from './savedCart';

/**
 * The parameter of a link that fills a store's cart (Fase 32A, ADR 0096): `?agregar={producto}:{cantidad}[:{opción}…]`,
 * one per product. An AI assistant builds it from the store's menu in text (/m/{slug}/menu.md, AgentPages.cs); the
 * customer opens it, reviews the order and sends it from the store.
 */
export const ORDER_LINK_PARAMETER = 'agregar';

/** An id of the menu as it travels in the link: letters, digits and dashes (the API's are uuids). */
const ID = /^[A-Za-z0-9-]{1,64}$/;

/** What a link asks to put in the cart. */
export interface OrderLink {
  /** The products, the same one with the same options added up, at most what a cart takes. */
  lines: SavedCartLine[];
  /** Parts of the link that are not a product: a bad id or quantity, or past what a cart takes. */
  invalid: number;
}

/**
 * The cart a link asks for, from the values of its `agregar` parameters; null when it has none. A quantity is a whole
 * number from 1 (without one, 1) and up to what a line takes; the menu says later which products and options exist.
 */
export function orderLinkOf(values: string[]): OrderLink | null {
  if (values.length === 0) return null;
  const items: Array<{ productId: string; quantity: number; optionIds: string[] }> = [];
  let invalid = 0;
  for (const value of values) {
    const [productId = '', quantityText, ...optionIds] = value.trim().split(':');
    const quantity = quantityText === undefined || quantityText === '' ? 1 : Number(quantityText);
    if (!ID.test(productId) || !Number.isInteger(quantity) || quantity <= 0 || optionIds.some((id) => !ID.test(id))) {
      invalid += 1;
      continue;
    }
    items.push({ productId, quantity: Math.min(quantity, MAX_LINE_QUANTITY), optionIds });
  }
  const distinct = new Set(items.map((item) => cartLineKey(item.productId, item.optionIds))).size;
  const { lines } = linesOfOrder(items);
  return { lines, invalid: invalid + Math.max(0, distinct - MAX_CART_LINES) };
}

/** What the store says of a link once its menu is read: nothing when everything went in the cart. */
export function orderLinkMessage(kept: number, missing: number): string {
  if (missing === 0) return '';
  if (kept === 0) return 'Los productos del enlace no están en el menú de ahora o les falta elegir una opción. Mira el menú.';
  return missing === 1
    ? 'Agregamos lo que sigue disponible; 1 producto del enlace no se pudo agregar: ya no está en el menú o le falta elegir una opción.'
    : `Agregamos lo que sigue disponible; ${missing} productos del enlace no se pudieron agregar: ya no están en el menú o les falta elegir una opción.`;
}
