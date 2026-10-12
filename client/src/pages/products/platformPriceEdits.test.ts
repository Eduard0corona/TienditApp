import { describe, expect, it } from 'vitest';
import {
  buildSaveBody,
  countChanges,
  optionBox,
  optionKey,
  parseOptionDelta,
  parseProductPrice,
  parseRaisePercent,
  productBox,
  productKey,
  raiseAmount,
  raisePrices,
  type PriceListItem,
} from './platformPriceEdits';

/** A burger of $99 in the menu, «Sencilla» (+$0) or «Doble» (+$30), at $125 in the app and «Doble» at +$38. */
function burger(patch: Partial<PriceListItem> = {}): PriceListItem {
  return {
    productId: 'burger',
    name: 'Hamburguesa',
    categoryId: 'c1',
    categoryName: 'Hamburguesas',
    isAvailable: true,
    menuPrice: 99,
    platformPrice: 125,
    optionGroups: [
      {
        id: 'size',
        name: 'Tamaño',
        options: [
          { id: 'sencilla', name: 'Sencilla', isAvailable: true, menuPriceDelta: 0, platformPriceDelta: null },
          { id: 'doble', name: 'Doble', isAvailable: true, menuPriceDelta: 30, platformPriceDelta: 38 },
        ],
      },
    ],
    ...patch,
  };
}

/** A soda of $25 without a price in the app. */
const SODA: PriceListItem = {
  productId: 'soda',
  name: 'Refresco',
  categoryId: 'c2',
  categoryName: 'Bebidas',
  isAvailable: true,
  menuPrice: 25,
  platformPrice: null,
  optionGroups: [],
};

describe('platformPriceEdits', () => {
  it('reads a price of a product as the API takes it: empty is the menu, and no zero, cents beyond two or more than a million', () => {
    expect(parseProductPrice('')).toBeNull();
    expect(parseProductPrice('  ')).toBeNull();
    expect(parseProductPrice('125')).toBe(125);
    expect(parseProductPrice('$1,250.50')).toBe(1250.5);
    expect(parseProductPrice('1000000')).toBe(1_000_000);
    for (const wrong of ['0', '0.00', '-5', '1.005', '1000000.01', 'abc', '+5']) expect(parseProductPrice(wrong)).toBeUndefined();
  });

  it('reads what an option adds, negative for a cheaper variant, up to 100,000', () => {
    expect(parseOptionDelta('')).toBeNull();
    expect(parseOptionDelta('38')).toBe(38);
    expect(parseOptionDelta('-5')).toBe(-5);
    expect(parseOptionDelta('−5.50')).toBe(-5.5);
    expect(parseOptionDelta('0')).toBe(0);
    expect(parseOptionDelta('-0')).toBe(0);
    expect(parseOptionDelta('-100000')).toBe(-100_000);
    for (const wrong of ['100000.01', '-100000.01', '1.005', '--5', 'x']) expect(parseOptionDelta(wrong)).toBeUndefined();
  });

  it('tells a box that changed from one that only shows the saved price again', () => {
    const item = burger();

    expect(productBox(item, {})).toEqual({ text: '125.00', changed: false, valid: true });
    expect(productBox(item, { [productKey('burger')]: '125' })).toEqual({ text: '125', changed: false, valid: true });
    expect(productBox(item, { [productKey('burger')]: '129' })).toEqual({ text: '129', changed: true, valid: true });
    // Empty goes back to the menu's price: a change.
    expect(productBox(item, { [productKey('burger')]: '' })).toEqual({ text: '', changed: true, valid: true });
    expect(productBox(item, { [productKey('burger')]: '0' })).toEqual({ text: '0', changed: true, valid: false });
    expect(optionBox(item.optionGroups[0].options[0], {})).toEqual({ text: '', changed: false, valid: true });
    expect(optionBox(item.optionGroups[0].options[1], { [optionKey('doble')]: '38.00' }).changed).toBe(false);
  });

  it('saves only what changed: each product with its price as it is now and only the options that changed', () => {
    const items = [burger(), SODA];
    const edits = { [optionKey('doble')]: '40', [productKey('soda')]: '32', [optionKey('sencilla')]: '' };

    expect(countChanges(items, edits)).toEqual({ changed: 2, invalid: 0 });
    expect(buildSaveBody(items, edits)).toEqual({
      products: [
        { productId: 'burger', price: 125, options: [{ optionId: 'doble', priceDelta: 40 }] },
        { productId: 'soda', price: 32, options: [] },
      ],
    });
    // Back to the menu's price: null.
    expect(buildSaveBody(items, { [productKey('burger')]: '', [optionKey('doble')]: '' })).toEqual({
      products: [{ productId: 'burger', price: null, options: [{ optionId: 'doble', priceDelta: null }] }],
    });
    expect(buildSaveBody(items, {})).toEqual({ products: [] });
  });

  it('saves nothing while a box holds what the API would refuse', () => {
    const items = [burger(), SODA];
    const edits = { [productKey('soda')]: '32', [optionKey('doble')]: '1.005' };

    expect(countChanges(items, edits)).toEqual({ changed: 2, invalid: 1 });
    expect(buildSaveBody(items, edits)).toBeNull();
  });

  it('raises a price by the percent and rounds it up to the step, in whole cents', () => {
    expect(raiseAmount(99, 25, 1)).toBe(124);
    expect(raiseAmount(99, 25, 5)).toBe(125);
    expect(raiseAmount(99, 25, 10)).toBe(130);
    // Exact multiples stay as they are.
    expect(raiseAmount(100, 20, 10)).toBe(120);
    expect(raiseAmount(100, 12.5, 1)).toBe(113);
    expect(raiseAmount(0.5, 10, 1)).toBe(1);
    expect(raiseAmount(19.99, 30, 1)).toBe(26);
    // A cheaper variant grows the same way, towards zero when it rounds.
    expect(raiseAmount(-10, 25, 1)).toBe(-12);
  });

  it('fills the empty boxes from the menu, or all of them, with the options to the peso and the free ones left free', () => {
    const items = [burger({ platformPrice: null }), SODA];
    const typed = { [productKey('soda')]: '30' };

    const empty = raisePrices(items, typed, { percent: 25, roundTo: 5, replaceAll: false });
    expect(empty[productKey('burger')]).toBe('125.00');
    // What was typed and what was saved stay.
    expect(empty[productKey('soda')]).toBe('30');
    expect(empty[optionKey('doble')]).toBeUndefined();
    expect(empty[optionKey('sencilla')]).toBe('');

    const all = raisePrices(items, typed, { percent: 25, roundTo: 5, replaceAll: true });
    expect(all[productKey('soda')]).toBe('35.00');
    // The options round to the peso: +$30 is +$37.50, so +$38.
    expect(all[optionKey('doble')]).toBe('38.00');
    expect(all[optionKey('sencilla')]).toBe('');
  });

  it('reads the percent of the calculation from more than 0 to 200, with two decimals', () => {
    expect(parseRaisePercent('25')).toBe(25);
    expect(parseRaisePercent('12.5')).toBe(12.5);
    expect(parseRaisePercent('25%')).toBe(25);
    for (const wrong of ['', '0', '200.01', '-5', '1.005']) expect(parseRaisePercent(wrong)).toBeUndefined();
  });
});
