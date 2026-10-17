import type { ProductOptionGroupDto } from '../types';
import { getLineKey, getOptionsLabel, getPriceWithOptions, getSelectionError } from './productOptions';

/** A line of a cart as the device keeps it: what was chosen, never a price (the menu says it when opened). */
export interface SavedCartLine {
  productId: string;
  optionIds: string[];
  quantity: number;
}

/** The cart of one store on this device (Fase 24B), until it is sent, emptied or forgotten. */
export interface SavedCart {
  businessName: string;
  /** When it last changed (ISO). */
  savedAt: string;
  lines: SavedCartLine[];
  /** Filled by «Pedir otra vez»: how many products the order had. The store then words what did not come back. */
  reorderOf?: number;
  /** Filled by «Pedir otra vez»: the products of the order sold by weight or in fractions, which the cart does not take. */
  skipped?: number;
}

/** A product of a store's menu, as much as a cart needs of it. */
export interface CartMenuProduct {
  id: string;
  price: number;
  availableNow: boolean;
  optionGroups?: unknown[] | null;
}

/** A line of the cart, priced with the menu of now. */
export interface CartLine<P extends CartMenuProduct> {
  key: string;
  product: P;
  optionIds: string[];
  optionsLabel: string;
  unitPrice: number;
  quantity: number;
}

const STORAGE_KEY = 'tienditapp.carts';
/** A cart left longer than this is not offered again. */
export const CART_DAYS = 7;
/** The carts one device keeps, the most recent ones. */
const MAX_CARTS = 10;
// The same limits as the store and the API (PlacePublicOrderCommandValidator).
export const MAX_CART_LINES = 50;
export const MAX_LINE_QUANTITY = 99;

type Carts = Record<string, SavedCart>;

function fresh(cart: SavedCart, now: Date): boolean {
  const savedAt = Date.parse(cart.savedAt);
  return Number.isFinite(savedAt) && now.getTime() - savedAt < CART_DAYS * 24 * 60 * 60 * 1000 && cart.lines.length > 0;
}

function readAll(): Carts {
  try {
    const parsed: unknown = JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? '{}');
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Carts) : {};
  } catch {
    // Storage blocked or written by someone else: no carts.
    return {};
  }
}

function writeAll(carts: Carts): void {
  try {
    const kept = Object.entries(carts)
      .sort(([, a], [, b]) => b.savedAt.localeCompare(a.savedAt))
      .slice(0, MAX_CARTS);
    if (kept.length === 0) window.localStorage.removeItem(STORAGE_KEY);
    else window.localStorage.setItem(STORAGE_KEY, JSON.stringify(Object.fromEntries(kept)));
  } catch {
    // Storage blocked or full: the cart lives only on this page.
  }
}

function isLine(line: unknown): line is SavedCartLine {
  if (!line || typeof line !== 'object') return false;
  const candidate = line as Partial<SavedCartLine>;
  return typeof candidate.productId === 'string'
    && Array.isArray(candidate.optionIds) && candidate.optionIds.every((id) => typeof id === 'string')
    && typeof candidate.quantity === 'number' && Number.isInteger(candidate.quantity) && candidate.quantity > 0;
}

/** The cart this device keeps for the store of `slug`, or null (none, emptied or too old). */
export function readCart(slug: string, now = new Date()): SavedCart | null {
  const cart = readAll()[slug];
  if (!cart || typeof cart.businessName !== 'string' || !Array.isArray(cart.lines)) return null;
  const lines = cart.lines.filter(isLine);
  const skipped = typeof cart.skipped === 'number' && Number.isInteger(cart.skipped) && cart.skipped > 0 ? cart.skipped : undefined;
  return fresh({ ...cart, lines }, now) ? { ...cart, lines, skipped } : null;
}

/** Keeps the cart of a store; an empty one is forgotten. */
export function saveCart(slug: string, cart: Omit<SavedCart, 'savedAt'>, now = new Date()): void {
  const carts = readAll();
  if (cart.lines.length === 0) delete carts[slug];
  else carts[slug] = { ...cart, savedAt: now.toISOString() };
  writeAll(carts);
}

export function clearCart(slug: string): void {
  const carts = readAll();
  if (!(slug in carts)) return;
  delete carts[slug];
  writeAll(carts);
}

/** The cart left the most recently in any store, for «Tienes un pedido sin enviar en…»; null without one. */
export function latestCart(now = new Date()): { slug: string; businessName: string; itemCount: number } | null {
  const [latest] = Object.keys(readAll())
    .map((slug) => ({ slug, cart: readCart(slug, now) }))
    .filter((entry): entry is { slug: string; cart: SavedCart } => entry.cart !== null)
    .sort((a, b) => b.cart.savedAt.localeCompare(a.cart.savedAt));
  return latest
    ? {
      slug: latest.slug,
      businessName: latest.cart.businessName,
      itemCount: latest.cart.lines.reduce((sum, line) => sum + line.quantity, 0),
    }
    : null;
}

/**
 * The lines of a past order, as «Pedir otra vez» puts them in the cart: the menu takes whole units, so a product sold
 * by weight or in fractions (0.5 kg) does not come back, and `skipped` counts them.
 */
export function linesOfOrder(
  items: Array<{ productId: string; quantity: number; optionIds?: string[] | null }>,
): { lines: SavedCartLine[]; skipped: number } {
  const lines = new Map<string, SavedCartLine>();
  let skipped = 0;
  for (const item of items) {
    if (!Number.isInteger(item.quantity) || item.quantity <= 0) {
      skipped += 1;
      continue;
    }
    const optionIds = [...(item.optionIds ?? [])];
    const key = getLineKey(item.productId, optionIds);
    const current = lines.get(key);
    lines.set(key, {
      productId: item.productId,
      optionIds,
      quantity: Math.min((current?.quantity ?? 0) + item.quantity, MAX_LINE_QUANTITY),
    });
  }
  return { lines: [...lines.values()].slice(0, MAX_CART_LINES), skipped };
}

/**
 * The saved lines that the menu of now can take, priced with it: the product is on the menu and available now, and its
 * options still exist and make a valid choice. How many products did not come back goes in `dropped`.
 */
export function restoreLines<P extends CartMenuProduct>(
  saved: SavedCartLine[],
  products: P[],
): { lines: CartLine<P>[]; dropped: number } {
  const byId = new Map(products.map((product) => [product.id, product]));
  const lines: CartLine<P>[] = [];
  let dropped = 0;
  for (const line of saved.slice(0, MAX_CART_LINES)) {
    const product = byId.get(line.productId);
    const groups = (product?.optionGroups ?? []) as ProductOptionGroupDto[];
    const known = new Set(groups.flatMap((group) => group.options.map((option) => option.id)));
    if (!product || !product.availableNow || line.optionIds.some((id) => !known.has(id)) || getSelectionError(groups, line.optionIds)) {
      dropped += 1;
      continue;
    }
    const key = getLineKey(product.id, line.optionIds);
    if (lines.some((existing) => existing.key === key)) continue;
    lines.push({
      key,
      product,
      optionIds: line.optionIds,
      optionsLabel: getOptionsLabel(groups, line.optionIds),
      unitPrice: getPriceWithOptions(product.price, groups, line.optionIds),
      quantity: Math.min(line.quantity, MAX_LINE_QUANTITY),
    });
  }
  return { lines, dropped };
}
