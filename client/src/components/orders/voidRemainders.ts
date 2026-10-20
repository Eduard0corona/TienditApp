import type { OrderDto, OrderPaymentMethod } from '../../types';
import { toCents } from '../../utils/money';

/**
 * What each payment method still has to give back, as the server voids (VoidOrderCommand): paid with it minus refunded with
 * it, and what was refunded another way (to the balance in the customer's favor, Fase 26D) taken from the largest first.
 */
export function remainingByMethod(order: OrderDto): Array<{ method: OrderPaymentMethod; amount: number }> {
  const cents = new Map<OrderPaymentMethod, number>();
  for (const payment of order.payments) cents.set(payment.method, (cents.get(payment.method) ?? 0) + toCents(payment.amount));
  let unassigned = 0;
  for (const refund of order.refunds ?? []) {
    if (cents.has(refund.method)) cents.set(refund.method, (cents.get(refund.method) ?? 0) - toCents(refund.amount));
    else unassigned += toCents(refund.amount);
  }
  const rows = [...cents.entries()].filter(([, value]) => value > 0).map(([method, value]) => ({ method, cents: value }));
  for (const row of [...rows].sort((a, b) => b.cents - a.cents)) {
    const taken = Math.min(unassigned, row.cents);
    row.cents -= taken;
    unassigned -= taken;
  }
  return rows.filter((row) => row.cents > 0).map((row) => ({ method: row.method, amount: row.cents / 100 }));
}
