import { describe, expect, it } from 'vitest';
import { estimateOrderTotals, getVatLabel, resolveProductVat } from './orderTotals';

const vat16 = { ratePercent: 16, isExempt: false };
const exempt = { ratePercent: 0, isExempt: true };

describe('estimateOrderTotals', () => {
  it('adds IVA on top when prices do not include it', () => {
    const totals = estimateOrderTotals([{ unitPrice: 20, quantity: 1, ...vat16 }], false);

    expect(totals).toMatchObject({ subtotal: 20, tax: 3.2, total: 23.2 });
  });

  it('takes IVA out of the amount charged when prices include it', () => {
    const totals = estimateOrderTotals([{ unitPrice: 116, quantity: 1, ...vat16 }], true);

    expect(totals).toMatchObject({ subtotal: 100, tax: 16, total: 116 });
  });

  it('groups lines by rate and lists the taxed group before the exempt one', () => {
    const totals = estimateOrderTotals(
      [
        { unitPrice: 30, quantity: 1, ...exempt },
        { unitPrice: 10, quantity: 2, ...vat16 },
        { unitPrice: 5, quantity: 1, ...vat16 },
      ],
      false,
    );

    expect(totals.breakdown.map((line) => [getVatLabel(line), line.taxableBase, line.taxAmount])).toEqual([
      ['IVA 16 %', 25, 4],
      ['Exento de IVA', 30, 0],
    ]);
    expect(totals.total).toBe(59);
  });

  it('uses the business rate unless the product has its own or is exempt', () => {
    expect(resolveProductVat({}, 16)).toEqual(vat16);
    expect(resolveProductVat({ taxRate: 8 }, 16)).toEqual({ ratePercent: 8, isExempt: false });
    expect(resolveProductVat({ isTaxExempt: true, taxRate: 16 }, 16)).toEqual(exempt);
  });
});
