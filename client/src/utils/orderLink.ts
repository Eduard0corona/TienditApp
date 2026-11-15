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

/**
 * The fragment of the link of an order an AI assistant prepared through the MCP server (Fase 32B, ADR 0096):
 * `/m/{slug}#pedido={token}`. In the fragment, the secret never reaches a server's log; the store reads its cart with it.
 */
export const ASSISTANT_ORDER_FRAGMENT = 'pedido';

/** A token of that link: 43 URL-safe Base64 characters, as the server issues them. */
const ASSISTANT_TOKEN = /^[A-Za-z0-9_-]{43}$/;

/** The token in a page's fragment (`#pedido=…`), or null when it has none or it is not one. */
export function assistantOrderTokenOf(hash: string): string | null {
  const value = new URLSearchParams(hash.replace(/^#/, '')).get(ASSISTANT_ORDER_FRAGMENT)?.trim() ?? '';
  return ASSISTANT_TOKEN.test(value) ? value : null;
}

/**
 * What the store says when the link of an assistant's order fills nothing: sent already, expired, not found, or not read
 * now (offline, the server busy: the link stays in the address for a reload to try again).
 */
export function assistantOrderMessage(state: 'sent' | 'expired' | 'missing' | 'failed'): string {
  switch (state) {
    case 'sent':
      return 'Este pedido ya se envió. Síguelo con el enlace que te dio la página al enviarlo.';
    case 'expired':
      return 'Este pedido venció. Pídele al asistente que lo prepare otra vez, o arma tu pedido aquí.';
    case 'failed':
      return 'No pudimos abrir el pedido que preparó tu asistente. Recarga la página para intentarlo de nuevo.';
    default:
      return 'No encontramos ese pedido. Revisa el enlace, o arma tu pedido aquí.';
  }
}

const assistantOrderKey = (slug: string) => `tienditapp.assistantOrder.${slug}`;

/**
 * The link of the assistant's order whose cart a store holds, for this tab: a reload or a sign-in comes back without the
 * fragment, and the order sent still confirms it. Not a credential: it opens that cart and says whether it was sent.
 */
export function rememberAssistantOrder(slug: string, token: string): void {
  try {
    sessionStorage.setItem(assistantOrderKey(slug), token);
  } catch {
    // Storage blocked: the order is still sent, without confirming the assistant's.
  }
}

/** The one this tab remembers for a store, if it is one of its tokens. */
export function rememberedAssistantOrder(slug: string): string | null {
  try {
    const token = sessionStorage.getItem(assistantOrderKey(slug));
    return token && ASSISTANT_TOKEN.test(token) ? token : null;
  } catch {
    return null;
  }
}

export function forgetAssistantOrder(slug: string): void {
  try {
    sessionStorage.removeItem(assistantOrderKey(slug));
  } catch {
    // Nothing kept.
  }
}
