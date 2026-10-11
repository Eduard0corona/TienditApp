import { beforeEach, describe, expect, it } from 'vitest';
import { describeSale, readSellDraft, writeSellDraft, type SellDraft } from './sellDraft';

const draft: SellDraft = {
  lines: [{ productId: 'taco', quantity: 2, notes: 'sin cebolla', optionIds: ['salsa-verde'] }],
  customerName: 'Lupita',
  customerPhone: '',
  tableNumber: '',
  orderNotes: '',
};

describe('sellDraft', () => {
  beforeEach(() => window.localStorage.clear());

  it('keeps the key of the charge on its way with the sale of its business', () => {
    const pendingCharge = { key: '0b7c9a52-5d2e-4f0a-9a3e-7f1d2c3b4a5e', sale: describeSale(draft) };

    writeSellDraft('t1', { ...draft, pendingCharge });

    expect(readSellDraft('t1')).toEqual({ ...draft, pendingCharge });
    expect(readSellDraft('t2')).toBeNull();
  });

  it('brings the sale back without a key that is not one', () => {
    window.localStorage.setItem('tienditapp.sell.draft.v1.t1', JSON.stringify({ ...draft, pendingCharge: { key: 7 } }));

    expect(readSellDraft('t1')).toEqual({ ...draft, pendingCharge: undefined });
  });

  it('tells another sale apart from the same one typed with spaces around', () => {
    const same = describeSale({ ...draft, customerName: ' Lupita ', lines: [{ ...draft.lines[0], notes: 'sin cebolla ' }] });

    expect(same).toBe(describeSale(draft));
    expect(describeSale({ ...draft, lines: [{ ...draft.lines[0], quantity: 3 }] })).not.toBe(describeSale(draft));
    expect(describeSale({ ...draft, lines: [{ ...draft.lines[0], optionIds: [] }] })).not.toBe(describeSale(draft));
    expect(describeSale({ ...draft, customerPhone: '5512345678' })).not.toBe(describeSale(draft));
  });
});
