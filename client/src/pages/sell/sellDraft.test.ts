import { beforeEach, describe, expect, it } from 'vitest';
import { OrderChannel, PlatformCollectedBy } from '../../types';
import { describeSale, readSellDraft, writeSellDraft, type SellDraft } from './sellDraft';

const draft: SellDraft = {
  lines: [{ productId: 'taco', quantity: 2, notes: 'sin cebolla', optionIds: ['salsa-verde'] }],
  customerName: 'Lupita',
  customerPhone: '',
  tableNumber: '',
  orderNotes: '',
  channel: OrderChannel.Counter,
  externalOrderNumber: '',
  collectedBy: PlatformCollectedBy.Platform,
};

const rappi: SellDraft = { ...draft, channel: OrderChannel.Rappi, externalOrderNumber: 'BC953' };

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

  it('describes a counter sale as it always did, so a key kept before the platforms arrived stays valid', () => {
    const before = JSON.stringify([[['taco', 2, 'sin cebolla', ['salsa-verde']]], ['Lupita', '', '', '']]);

    expect(describeSale(draft)).toBe(before);
  });

  it('makes another sale of another platform, another folio or another who charged', () => {
    expect(describeSale(rappi)).not.toBe(describeSale(draft));
    expect(describeSale({ ...rappi, channel: OrderChannel.UberEats })).not.toBe(describeSale(rappi));
    expect(describeSale({ ...rappi, externalOrderNumber: 'BC954' })).not.toBe(describeSale(rappi));
    expect(describeSale({ ...rappi, collectedBy: PlatformCollectedBy.Business })).not.toBe(describeSale(rappi));
    // The folio as the server keeps it: typed with a # or spaces it is the same order.
    expect(describeSale({ ...rappi, externalOrderNumber: ' #bc 953 ' })).toBe(describeSale(rappi));
  });

  it('brings back an order of a platform with its folio and who charged', () => {
    const business = { ...rappi, channel: OrderChannel.DidiFood, collectedBy: PlatformCollectedBy.Business };

    writeSellDraft('t1', business);

    expect(readSellDraft('t1')).toEqual({ ...business, pendingCharge: undefined });
  });

  it('brings a draft from before the platforms, or one with values that are not the enum\'s, back as a counter sale', () => {
    const old = { lines: draft.lines, customerName: 'Lupita', customerPhone: '', tableNumber: '', orderNotes: '' };
    window.localStorage.setItem('tienditapp.sell.draft.v1.t1', JSON.stringify(old));
    expect(readSellDraft('t1')).toEqual({ ...draft, pendingCharge: undefined });

    window.localStorage.setItem('tienditapp.sell.draft.v1.t1', JSON.stringify({ ...draft, channel: 9, collectedBy: 7, externalOrderNumber: 12 }));
    expect(readSellDraft('t1')).toEqual({ ...draft, pendingCharge: undefined });

    // A chat is never kept in the draft: it comes back with its link, or the sale is a counter one.
    window.localStorage.setItem('tienditapp.sell.draft.v1.t1', JSON.stringify({ ...draft, channel: OrderChannel.WhatsApp }));
    expect(readSellDraft('t1')?.channel).toBe(OrderChannel.Counter);
  });

  it('keeps an order of a platform not started yet, and forgets an empty counter sale', () => {
    const empty: SellDraft = { ...draft, lines: [], customerName: '' };

    writeSellDraft('t1', { ...empty, channel: OrderChannel.Rappi });
    expect(readSellDraft('t1')?.channel).toBe(OrderChannel.Rappi);

    writeSellDraft('t1', empty);
    expect(readSellDraft('t1')).toBeNull();
  });

  it('keeps where an order sent to an address goes, and tells another address apart', () => {
    const delivery = { postalCode: '72000', addressLine: 'Av. Reforma 123', references: '' };
    const toAddress: SellDraft = { ...draft, delivery };

    writeSellDraft('t1', toAddress);
    // A draft from before «paga con» reads it empty.
    expect(readSellDraft('t1')?.delivery).toEqual({ ...delivery, paysWith: '' });
    // A counter sale saved before, or with a delivery that is not one, reads without it.
    expect(describeSale({ ...draft, delivery: null })).toBe(describeSale(draft));
    expect(describeSale(toAddress)).not.toBe(describeSale(draft));
    expect(describeSale({ ...toAddress, delivery: { ...delivery, addressLine: ' Av. Reforma 123 ' } })).toBe(describeSale(toAddress));
    expect(describeSale({ ...toAddress, delivery: { ...delivery, postalCode: '72005' } })).not.toBe(describeSale(toAddress));

    writeSellDraft('t1', { ...draft, lines: [], customerName: '', delivery: { postalCode: '', addressLine: '', references: '' } });
    expect(readSellDraft('t1')?.delivery).toEqual({ postalCode: '', addressLine: '', references: '', paysWith: '' });
    window.localStorage.setItem('tienditapp.sell.draft.v1.t1', JSON.stringify({ ...draft, delivery: 'Reforma' }));
    expect(readSellDraft('t1')?.delivery).toBeUndefined();
  });

  it('keeps «paga con», tells another bill apart, and describes a sale without one as before', () => {
    const delivery = { postalCode: '72000', addressLine: 'Av. Reforma 123', references: '' };
    const toAddress: SellDraft = { ...draft, delivery };

    writeSellDraft('t1', { ...toAddress, delivery: { ...delivery, paysWith: '500' } });

    expect(readSellDraft('t1')?.delivery?.paysWith).toBe('500');
    expect(describeSale({ ...toAddress, delivery: { ...delivery, paysWith: ' ' } })).toBe(describeSale(toAddress));
    expect(describeSale({ ...toAddress, delivery: { ...delivery, paysWith: '500' } })).not.toBe(describeSale(toAddress));
    expect(describeSale({ ...toAddress, delivery: { ...delivery, paysWith: '500' } }))
      .not.toBe(describeSale({ ...toAddress, delivery: { ...delivery, paysWith: '1000' } }));
  });
});
