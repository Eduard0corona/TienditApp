import { describe, expect, it } from 'vitest';
import {
  belowMinimumMessage,
  deliveryFeeFor,
  describeDeliveryTerms,
  estimateDeliveryTotals,
  findDeliveryZone,
  meetsMinimum,
  notCoveredMessage,
} from './homeDelivery';

const centro = { name: 'Centro', isActive: true, postalCodes: ['72000', '72005'], fee: 35, minimumOrder: 150, freeDeliveryFrom: 400 };
const norte = { name: 'Norte', isActive: false, postalCodes: ['72100'], fee: 45, minimumOrder: 0, freeDeliveryFrom: null };

describe('homeDelivery', () => {
  it('finds the active zone that reaches the postal code, as the server keeps it', () => {
    expect(findDeliveryZone([norte, centro], ' 72005 ')?.name).toBe('Centro');
    expect(findDeliveryZone([norte, centro], '72100')).toBeNull();
    expect(findDeliveryZone([norte, centro], '7200')).toBeNull();
    expect(findDeliveryZone([norte, centro], '72999')).toBeNull();
  });

  it('charges the fee until the order reaches «gratis desde», and compares the minimum with the products', () => {
    expect(deliveryFeeFor(centro, 399.99)).toBe(35);
    expect(deliveryFeeFor(centro, 400)).toBe(0);
    expect(deliveryFeeFor(norte, 10_000)).toBe(45);
    expect(meetsMinimum(centro, 149.99)).toBe(false);
    expect(meetsMinimum(centro, 150)).toBe(true);
  });

  it('adds the fee as one more concept with the IVA of the business', () => {
    const tacos = [{ unitPrice: 120, quantity: 2, ratePercent: 16, isExempt: false }];

    const included = estimateDeliveryTotals(tacos, true, centro, 16);
    expect(included).toMatchObject({ productsTotal: 240, fee: 35, total: 275, tax: 37.93 });

    const onTop = estimateDeliveryTotals(tacos, false, centro, 16);
    // $240 + $35 before IVA, and 16 % on both.
    expect(onTop).toMatchObject({ productsTotal: 278.4, fee: 35, subtotal: 275, total: 319 });

    const free = estimateDeliveryTotals([{ ...tacos[0], quantity: 4 }], true, centro, 16);
    expect(free).toMatchObject({ productsTotal: 480, fee: 0, total: 480 });
  });

  it('matches the server with products of other rates: each rate rounded once, the fee in the business\'s', () => {
    const tacos = { unitPrice: 120, quantity: 2, ratePercent: 16, isExempt: false };
    const tortillas = { unitPrice: 25, quantity: 1, ratePercent: 0, isExempt: true };
    const noFreeDelivery = { ...centro, freeDeliveryFrom: null };

    // IVA included: $275 at 16 % (tacos and the fee) and $25 exempt.
    expect(estimateDeliveryTotals([tacos, tortillas], true, noFreeDelivery, 16))
      .toMatchObject({ productsTotal: 265, fee: 35, total: 300, tax: 37.93 });
    // Before IVA: the tacos and the fee get 16 % on top; the tortillas none.
    expect(estimateDeliveryTotals([tacos, tortillas], false, noFreeDelivery, 16))
      .toMatchObject({ productsTotal: 303.4, fee: 35, subtotal: 300, tax: 44, total: 344 });
    // Products at the border's 8 % and the fee at 16 %: two groups.
    const border = estimateDeliveryTotals([{ ...tacos, ratePercent: 8 }], true, noFreeDelivery, 16);
    expect(border.breakdown.map((line) => [line.ratePercent, line.taxAmount])).toEqual([[16, 4.83], [8, 17.78]]);
    expect(border.total).toBe(275);
  });

  it('says it in the words of the server', () => {
    expect(notCoveredMessage('72999')).toBe('Por ahora no llegamos al 72999.');
    expect(belowMinimumMessage('72000', 150)).toBe('El pedido mínimo para el 72000 es de $150.00.');
    expect(describeDeliveryTerms(centro)).toBe('Envío $35.00 · gratis desde $400.00 · pedido mínimo $150.00');
    expect(describeDeliveryTerms({ fee: 0, minimumOrder: 0 })).toBe('Envío gratis');
    // With prices before IVA the order adds IVA to the fee; the minimum is what the customer pays in products.
    expect(describeDeliveryTerms(centro, false)).toBe('Envío $35.00 más IVA · gratis desde $400.00 · pedido mínimo $150.00');
    expect(describeDeliveryTerms({ fee: 0, minimumOrder: 0 }, false)).toBe('Envío gratis');
  });
});
