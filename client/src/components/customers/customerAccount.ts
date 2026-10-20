import type { CustomerAccount } from '../../api/customerCredit';
import { formatDate } from '../../utils/dates';
import { formatMoney, parseMoneyInput } from '../../utils/money';

// Orders listed in the WhatsApp summary: more would make a message nobody reads to the end.
const SUMMARY_ORDERS = 10;

/**
 * The account as the customer reads it on WhatsApp: what they owe, order by order, and what they may still owe. The
 * business reviews it and taps send; nothing is sent by the server.
 */
export function accountSummaryText(account: CustomerAccount, businessName: string): string {
  const lines = [`*Estado de cuenta en ${businessName}*`];
  if (account.name) lines.push(account.name);
  lines.push('');
  if (account.owed <= 0) {
    lines.push('No debes nada. ¡Gracias!');
    return lines.join('\n');
  }

  lines.push(`Debes *${formatMoney(account.owed)}*:`);
  account.orders.slice(0, SUMMARY_ORDERS).forEach((order) => {
    lines.push(`• ${order.orderNumber} del ${formatDate(order.createdAt)}: ${formatMoney(order.balance)}`);
  });
  const more = account.ordersCount - Math.min(account.orders.length, SUMMARY_ORDERS);
  if (more > 0) lines.push(more === 1 ? '• y 1 pedido más' : `• y ${more} pedidos más`);
  if (account.creditLimit != null && account.available != null) {
    lines.push('', `Tu límite de fiado es ${formatMoney(account.creditLimit)}: te quedan ${formatMoney(account.available)}.`);
  }
  lines.push('', '¡Gracias!');
  return lines.join('\n');
}

/** Whole days since the order was made, as the list reads them. */
export function daysSince(createdAt: string, now: Date = new Date()): number {
  return Math.max(0, Math.floor((now.getTime() - new Date(createdAt).getTime()) / 86_400_000));
}

export function describeDays(days: number): string {
  if (days === 0) return 'hoy';
  return days === 1 ? 'hace 1 día' : `hace ${days} días`;
}

/** The highest limit the server takes (Customer.MaxCreditLimit). */
export const MAX_CREDIT_LIMIT = 1_000_000;

/** What the limit typed means, in the owner's words; null when it can be saved. */
export function creditLimitProblem(typed: string): string | null {
  const limit = parseMoneyInput(typed);
  if (typed.trim() === '' || limit === null) return 'Escribe cuánto le fías, por ejemplo 500.';
  if (limit < 1 || limit > MAX_CREDIT_LIMIT) return 'El límite de fiado va de $1 a $1,000,000.';
  return null;
}
