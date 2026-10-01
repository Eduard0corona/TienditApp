// Mirrors the API's current rule (Order.RecalculateTotals): 16% over the subtotal, rounded to cents with
// banker's rounding like .NET's Math.Round. It is an estimate for the cashier; the order's total is the
// one the API returns (a configurable tax engine is pending, see ADR 0007).
export const ORDER_TAX_RATE_PERCENT = 16;

export interface OrderTotalsEstimate {
  subtotal: number;
  tax: number;
  total: number;
}

export function estimateOrderTotals(lines: Array<{ unitPrice: number; quantity: number }>): OrderTotalsEstimate {
  // Integer cents avoid floating point drift (prices come with 2 decimals from the API).
  const subtotalCents = lines.reduce((sum, line) => sum + Math.round(line.unitPrice * 100) * line.quantity, 0);
  const taxInHundredthsOfCent = subtotalCents * ORDER_TAX_RATE_PERCENT;
  const wholeCents = Math.floor(taxInHundredthsOfCent / 100);
  const remainder = taxInHundredthsOfCent % 100;
  const taxCents = remainder > 50 || (remainder === 50 && wholeCents % 2 === 1) ? wholeCents + 1 : wholeCents;

  return {
    subtotal: subtotalCents / 100,
    tax: taxCents / 100,
    total: (subtotalCents + taxCents) / 100,
  };
}
