import { beforeEach, describe, expect, it } from 'vitest';
import { cartLineKey, clearCart, latestCart, lineNotes, linesOfOrder, readCart, restoreLines, saveCart } from './savedCart';

const now = new Date('2026-10-17T13:25:00Z');
const later = (days: number) => new Date(now.getTime() + days * 24 * 60 * 60 * 1000);

const size = {
  id: 'g1',
  name: 'Tamaño',
  minSelections: 1,
  maxSelections: 1,
  options: [
    { id: 'chico', name: 'Chico', priceDelta: 0, isAvailable: true },
    { id: 'grande', name: 'Grande', priceDelta: 10, isAvailable: true },
    { id: 'jumbo', name: 'Jumbo', priceDelta: 20, isAvailable: false },
  ],
};
const latte = { id: 'latte', price: 55, availableNow: true, optionGroups: [size] };
const concha = { id: 'concha', price: 18, availableNow: true, optionGroups: [] };
const tamal = { id: 'tamal', price: 25, availableNow: false, optionGroups: [] };

describe('savedCart', () => {
  beforeEach(() => window.localStorage.clear());

  it('keeps the cart of each store on the device until it is emptied or cleared', () => {
    saveCart('cafe-luna', { businessName: 'Café Luna', lines: [{ productId: 'latte', optionIds: ['grande'], quantity: 2 }] }, now);
    saveCart('el-guero', { businessName: 'El Güero', lines: [{ productId: 'taco', optionIds: [], quantity: 3 }] }, later(1));

    expect(readCart('cafe-luna', now)).toEqual({
      businessName: 'Café Luna',
      savedAt: now.toISOString(),
      lines: [{ productId: 'latte', optionIds: ['grande'], quantity: 2 }],
    });
    expect(latestCart(later(1))).toEqual({ slug: 'el-guero', businessName: 'El Güero', itemCount: 3 });

    saveCart('el-guero', { businessName: 'El Güero', lines: [] }, later(1));
    expect(readCart('el-guero', later(1))).toBeNull();
    clearCart('cafe-luna');
    expect(latestCart(later(1))).toBeNull();
    expect(window.localStorage.getItem('tienditapp.carts')).toBeNull();
  });

  it('forgets a cart left for a week, and anything it cannot read', () => {
    saveCart('cafe-luna', { businessName: 'Café Luna', lines: [{ productId: 'latte', optionIds: [], quantity: 1 }] }, now);

    expect(readCart('cafe-luna', later(6))).not.toBeNull();
    expect(readCart('cafe-luna', later(7))).toBeNull();
    expect(latestCart(later(8))).toBeNull();

    window.localStorage.setItem('tienditapp.carts', '{not json');
    expect(readCart('cafe-luna', now)).toBeNull();
    window.localStorage.setItem('tienditapp.carts', JSON.stringify({
      'cafe-luna': { businessName: 'Café Luna', savedAt: now.toISOString(), lines: [{ productId: 'latte', optionIds: [], quantity: 0.5 }, { productId: 7 }] },
    }));
    expect(readCart('cafe-luna', now)).toBeNull();
  });

  it('turns a past order into whole units, joining the same product with the same options', () => {
    const { lines, skipped } = linesOfOrder([
      { productId: 'latte', quantity: 1, optionIds: ['grande'] },
      { productId: 'latte', quantity: 2, optionIds: ['grande'] },
      { productId: 'queso', quantity: 0.25, optionIds: [] },
      { productId: 'concha', quantity: 150 },
    ]);

    expect(lines).toEqual([
      { productId: 'latte', optionIds: ['grande'], quantity: 3 },
      { productId: 'concha', optionIds: [], quantity: 99 },
    ]);
    expect(skipped).toBe(1);
  });

  it('orders again each product with its note, and never takes a note for options', () => {
    const { lines } = linesOfOrder([
      { productId: 'latte', quantity: 1, optionIds: ['grande'], notes: 'sin azúcar' },
      { productId: 'latte', quantity: 1, optionIds: ['grande'], notes: '  sin azúcar ' },
      { productId: 'latte', quantity: 1, optionIds: ['grande'], notes: null },
    ]);

    expect(lines).toEqual([
      { productId: 'latte', optionIds: ['grande'], quantity: 2, notes: 'sin azúcar' },
      { productId: 'latte', optionIds: ['grande'], quantity: 1 },
    ]);
    expect(cartLineKey('latte', [], 'grande')).not.toBe(cartLineKey('latte', ['grande']));
  });

  it('puts back only what the menu of now can take, at its prices of now', () => {
    const { lines, dropped } = restoreLines(
      [
        { productId: 'latte', optionIds: ['grande'], quantity: 2 },
        { productId: 'latte', optionIds: ['jumbo'], quantity: 1 },
        { productId: 'latte', optionIds: [], quantity: 1 },
        { productId: 'latte', optionIds: ['otro'], quantity: 1 },
        { productId: 'concha', optionIds: [], quantity: 4 },
        { productId: 'tamal', optionIds: [], quantity: 1 },
        { productId: 'ya-no-existe', optionIds: [], quantity: 1 },
      ],
      [latte, concha, tamal],
    );

    expect(lines.map((line) => [line.product.id, line.optionsLabel, line.unitPrice, line.quantity])).toEqual([
      ['latte', 'Grande', 65, 2],
      ['concha', '', 18, 4],
    ]);
    // An option sold out, a required choice missing, an option that no longer exists, a product not available now and
    // one no longer on the menu.
    expect(dropped).toBe(5);
  });

  it('keeps the note of each product: the same note adds up, another one is another line', () => {
    expect(cartLineKey('latte', ['grande'])).toBe(cartLineKey('latte', ['grande'], undefined));
    expect(cartLineKey('latte', ['grande'], 'sin azúcar')).not.toBe(cartLineKey('latte', ['grande']));
    expect(lineNotes('  sin azúcar  ')).toBe('sin azúcar');
    expect(lineNotes('   ')).toBeUndefined();
    expect(lineNotes('x'.repeat(400))).toHaveLength(300);

    saveCart('cafe-luna', {
      businessName: 'Café Luna',
      lines: [
        { productId: 'latte', optionIds: ['grande'], quantity: 1, notes: 'sin azúcar' },
        { productId: 'latte', optionIds: ['grande'], quantity: 2 },
      ],
    }, now);
    const saved = readCart('cafe-luna', now)!;
    const { lines } = restoreLines(saved.lines, [latte, concha]);

    expect(lines.map((line) => [line.key, line.quantity, line.notes])).toEqual([
      ['latte|grande#sin azúcar', 1, 'sin azúcar'],
      ['latte|grande', 2, undefined],
    ]);
  });

  it('reads no line whose note is not text', () => {
    window.localStorage.setItem('tienditapp.carts', JSON.stringify({
      'cafe-luna': {
        businessName: 'Café Luna',
        savedAt: now.toISOString(),
        lines: [{ productId: 'concha', optionIds: [], quantity: 1, notes: 7 }, { productId: 'concha', optionIds: [], quantity: 2 }],
      },
    }));

    expect(readCart('cafe-luna', now)?.lines).toEqual([{ productId: 'concha', optionIds: [], quantity: 2 }]);
  });
});
