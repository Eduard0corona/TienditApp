// Mirrors the API (OrderTaxCalculator): lines grouped by IVA rate, each group rounded once to cents with
// banker's rounding like .NET's Math.Round. With prices that include IVA the group keeps the amount
// charged and the tax is taken out of it; otherwise the tax is added on top. It is an estimate for the
// cashier: the order total is the one the API returns, and the sale is rejected if they differ.
import { lineAmountCents } from './units';

export interface VatTreatment {
  ratePercent: number;
  isExempt: boolean;
}

export interface TotalsLine extends VatTreatment {
  unitPrice: number;
  quantity: number;
}

export interface TaxLineEstimate extends VatTreatment {
  taxableBase: number;
  taxAmount: number;
  total: number;
}

export interface OrderTotalsEstimate {
  /** Taxable base: amounts without IVA. */
  subtotal: number;
  tax: number;
  total: number;
  breakdown: TaxLineEstimate[];
}

/** IVA of a catalog item: exempt, its own rate, or the business default when it has none. */
export function resolveProductVat(
  product: { taxRate?: number | null; isTaxExempt?: boolean },
  defaultTaxRate: number,
): VatTreatment {
  if (product.isTaxExempt) return { ratePercent: 0, isExempt: true };
  return { ratePercent: product.taxRate ?? defaultTaxRate, isExempt: false };
}

/** numerator / denominator rounded half to even, both positive integers. */
function divideRoundingHalfEven(numerator: number, denominator: number): number {
  const quotient = Math.floor(numerator / denominator);
  const twiceRemainder = 2 * (numerator - quotient * denominator);
  if (twiceRemainder > denominator) return quotient + 1;
  if (twiceRemainder < denominator) return quotient;
  return quotient % 2 === 0 ? quotient : quotient + 1;
}

export function estimateOrderTotals(lines: TotalsLine[], pricesIncludeTax: boolean): OrderTotalsEstimate {
  // Integer cents and rates in hundredths of a percent avoid floating point drift.
  const groups = new Map<string, VatTreatment & { amountCents: number }>();
  for (const line of lines) {
    const key = `${line.ratePercent}|${line.isExempt}`;
    const group = groups.get(key) ?? { ratePercent: line.ratePercent, isExempt: line.isExempt, amountCents: 0 };
    // Weighed lines can fall between cents: each line is rounded once, like OrderItem.TotalPrice.
    group.amountCents += lineAmountCents(line.unitPrice, line.quantity);
    groups.set(key, group);
  }

  const breakdown = [...groups.values()]
    .sort((a, b) => Number(a.isExempt) - Number(b.isExempt) || b.ratePercent - a.ratePercent)
    .map((group) => {
      const rate = Math.round(group.ratePercent * 100);
      let baseCents = group.amountCents;
      let taxCents = 0;
      if (!group.isExempt && rate > 0 && group.amountCents > 0) {
        if (pricesIncludeTax) {
          taxCents = divideRoundingHalfEven(group.amountCents * rate, 10_000 + rate);
          baseCents = group.amountCents - taxCents;
        } else {
          taxCents = divideRoundingHalfEven(group.amountCents * rate, 10_000);
        }
      }
      return {
        ratePercent: group.ratePercent,
        isExempt: group.isExempt,
        taxableBase: baseCents / 100,
        taxAmount: taxCents / 100,
        total: (baseCents + taxCents) / 100,
      };
    });

  const subtotalCents = breakdown.reduce((sum, line) => sum + Math.round(line.taxableBase * 100), 0);
  const taxCents = breakdown.reduce((sum, line) => sum + Math.round(line.taxAmount * 100), 0);
  return {
    subtotal: subtotalCents / 100,
    tax: taxCents / 100,
    total: (subtotalCents + taxCents) / 100,
    breakdown,
  };
}

/** "IVA 16 %", "IVA 0 %" or "Exento", as printed on tickets. */
export function getVatLabel(line: VatTreatment): string {
  return line.isExempt ? 'Exento de IVA' : `IVA ${line.ratePercent} %`;
}
