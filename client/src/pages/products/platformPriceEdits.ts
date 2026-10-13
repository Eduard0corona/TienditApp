import type { Schemas } from '../../api/typedClient';
import { parseMoneyInput, toCents } from '../../utils/money';

export type PriceListItem = Schemas['PlatformPriceItemDto'];
export type PriceListOption = Schemas['PlatformPriceOptionDto'];
export type SavePricesBody = Schemas['SavePlatformPricesRequest'];

// The limits of the API (ProductPlatformPrice and ProductOption): a price as a menu would have it.
export const MAX_PLATFORM_PRICE = 1_000_000;
export const MAX_OPTION_PRICE_DELTA = 100_000;

/** What the person typed in each box, by price: `p:` and the product, or `o:` and the option. */
export type PriceEdits = Record<string, string>;

export const productKey = (productId: string) => `p:${productId}`;
export const optionKey = (optionId: string) => `o:${optionId}`;

/** A saved price in its box: empty when the app charges what the menu does. */
export function toInputText(amount: number | null | undefined): string {
  return amount == null ? '' : amount.toFixed(2);
}

/**
 * A comma only groups thousands («1,250.50»): parseMoneyInput drops every comma, and «32,50» typed as a decimal would be
 * read as 3,250. A price list is typed in bulk, so it is refused instead.
 */
function commasGroupThousands(text: string): boolean {
  return !text.includes(',') || /^\$?\s*\d{1,3}(,\d{3})+(\.\d*)?$/.test(text.trim());
}

/** The price of a product as typed: null when empty (the menu's), undefined when the API would refuse it. */
export function parseProductPrice(text: string): number | null | undefined {
  if (text.trim() === '') return null;
  if (!commasGroupThousands(text)) return undefined;
  const value = parseMoneyInput(text);
  return value !== null && value > 0 && value <= MAX_PLATFORM_PRICE ? value : undefined;
}

/**
 * What an option adds as typed, negative for a variant cheaper than the product: null when empty (what it adds in the
 * menu), undefined when the API would refuse it.
 */
export function parseOptionDelta(text: string): number | null | undefined {
  const trimmed = text.trim();
  if (trimmed === '') return null;
  const negative = /^[-−]/.test(trimmed);
  const amount = negative ? trimmed.slice(1) : trimmed;
  if (!commasGroupThousands(amount)) return undefined;
  const value = parseMoneyInput(amount);
  if (value === null || value > MAX_OPTION_PRICE_DELTA) return undefined;
  return negative && value !== 0 ? -value : value;
}

/** A box of the list: what it holds now, whether it changed from what is saved and whether the API would take it. */
export interface PriceBox {
  text: string;
  changed: boolean;
  valid: boolean;
}

export function productBox(item: PriceListItem, edits: PriceEdits): PriceBox {
  return box(edits[productKey(item.productId)], item.platformPrice, parseProductPrice);
}

export function optionBox(option: PriceListOption, edits: PriceEdits): PriceBox {
  return box(edits[optionKey(option.id)], option.platformPriceDelta, parseOptionDelta);
}

function box(typed: string | undefined, saved: number | null | undefined, parse: (text: string) => number | null | undefined): PriceBox {
  const text = typed ?? toInputText(saved);
  const value = parse(text);
  if (value === undefined) return { text, changed: true, valid: false };
  const changed = typed !== undefined && !sameAmount(value, saved ?? null);
  return { text, changed, valid: true };
}

function sameAmount(a: number | null, b: number | null): boolean {
  return a === null || b === null ? a === b : toCents(a) === toCents(b);
}

/** How many prices changed, and how many of them the API would refuse. */
export function countChanges(items: PriceListItem[], edits: PriceEdits): { changed: number; invalid: number } {
  let changed = 0;
  let invalid = 0;
  for (const item of items) {
    for (const current of [productBox(item, edits), ...optionsOf(item).map((option) => optionBox(option, edits))]) {
      if (current.changed) changed += 1;
      if (!current.valid) invalid += 1;
    }
  }
  return { changed, invalid };
}

/**
 * The save of what changed: each product with a change, with its price as it is now (null: the menu's) and only its options
 * that changed. Null while a box holds something the API would refuse.
 */
export function buildSaveBody(items: PriceListItem[], edits: PriceEdits): SavePricesBody | null {
  const products: SavePricesBody['products'] = [];
  for (const item of items) {
    const productPrice = productBox(item, edits);
    const options = optionsOf(item).map((option) => ({ option, current: optionBox(option, edits) }));
    if (!productPrice.valid || options.some(({ current }) => !current.valid)) return null;
    const changedOptions = options.filter(({ current }) => current.changed);
    if (!productPrice.changed && changedOptions.length === 0) continue;
    products.push({
      productId: item.productId,
      price: parseProductPrice(productPrice.text) ?? null,
      options: changedOptions.map(({ option, current }) => ({ optionId: option.id, priceDelta: parseOptionDelta(current.text) ?? null })),
    });
  }
  return { products };
}

export function optionsOf(item: PriceListItem): PriceListOption[] {
  return item.optionGroups.flatMap((group) => group.options);
}

/** How the prices of the app are worked out from the menu's: a percent more, rounded up. */
export interface RaiseSettings {
  /** 0.01 to 200, with two decimals at most. */
  percent: number;
  /** The products round up to whole pesos, or to $5 or $10; the options, always to the peso. */
  roundTo: 1 | 5 | 10;
  /** Also the boxes that already have a price; otherwise only the empty ones. */
  replaceAll: boolean;
}

export const MAX_RAISE_PERCENT = 200;

/** The percent as typed: undefined unless it is more than 0, up to 200 and with two decimals at most. */
export function parseRaisePercent(text: string): number | undefined {
  const value = parseMoneyInput(text.replace('%', ''));
  return value !== null && value > 0 && value <= MAX_RAISE_PERCENT ? value : undefined;
}

/**
 * The amount raised by the percent and rounded up to the step, in whole cents (no floating point drift): $99 + 25 % to
 * the peso is $124; to $5, $125. A negative amount (a cheaper variant) grows the same way, towards zero when it rounds.
 */
export function raiseAmount(amount: number, percent: number, step: number): number {
  const scale = 10_000;
  const raised = toCents(amount) * (scale + Math.round(percent * 100));
  const unit = Math.round(step * 100) * scale;
  const steps = raised >= 0 ? Math.floor((raised + unit - 1) / unit) : -Math.floor(-raised / unit);
  return (steps * Math.round(step * 100)) / 100;
}

/**
 * Fills the boxes of the products given, and of their options, with the menu's price raised by the percent: the empty ones,
 * or all of them. An option that adds nothing in the menu stays like that. Nothing is saved: the person checks them first.
 */
export function raisePrices(items: PriceListItem[], edits: PriceEdits, settings: RaiseSettings): PriceEdits {
  const next = { ...edits };
  for (const item of items) {
    if (settings.replaceAll || productBox(item, edits).text.trim() === '') {
      next[productKey(item.productId)] = toInputText(raiseAmount(item.menuPrice, settings.percent, settings.roundTo));
    }
    for (const option of optionsOf(item)) {
      if (!settings.replaceAll && optionBox(option, edits).text.trim() !== '') continue;
      next[optionKey(option.id)] = option.menuPriceDelta === 0 ? '' : toInputText(raiseAmount(option.menuPriceDelta, settings.percent, 1));
    }
  }
  return next;
}
