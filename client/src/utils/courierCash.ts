// The cash the courier collects for an order sent to the customer's address (Fase 22D). Mirrors the API
// (HomeDeliveryRules, Order.Dispatch): «paga con» is the bill the customer pays with, in pesos and cents up to $100,000, and
// it covers what the courier collects; the change is the rest. The server decides what the courier collects (the order's
// balance when it goes out) and refuses a bill that does not cover it.
import { formatMoney, parseMoneyInput, toCents } from './money';

export const PAYS_WITH_MAX = 100_000;
/** The server's words (HomeDeliveryRules.PaysWithMessage). */
export const PAYS_WITH_MESSAGE = '«Paga con» va hasta $100,000.00, con dos decimales.';

/** The server's words (HomeDeliveryRules.PaysWithNotEnoughMessage). */
export function paysWithNotEnoughMessage(paysWith: number, toCollect: number): string {
  return `Con ${formatMoney(paysWith)} no alcanza: hay que cobrar ${formatMoney(toCollect)}.`;
}

/** What was typed in «paga con»: nothing (null), an amount, or something that is not one. */
export function parsePaysWith(text: string): number | null | 'invalid' {
  if (!text.trim()) return null;
  const amount = parseMoneyInput(text);
  return amount === null || amount <= 0 || amount > PAYS_WITH_MAX ? 'invalid' : amount;
}

/** Why «paga con» cannot go with what is collected, or '' when it can (or nothing was typed). */
export function paysWithProblem(text: string, toCollect: number): string {
  const paysWith = parsePaysWith(text);
  if (paysWith === null) return '';
  if (paysWith === 'invalid') return PAYS_WITH_MESSAGE;
  return toCents(paysWith) < toCents(toCollect) ? paysWithNotEnoughMessage(paysWith, toCollect) : '';
}

/** The change the courier takes: what the bill leaves over what it collects. */
export function changeFor(paysWith: number, toCollect: number): number {
  return Math.max(0, toCents(paysWith) - toCents(toCollect)) / 100;
}

/** «Cobra $275.00 · paga con $500.00 · cambio $225.00», or without the bill when there is none. */
export function describeCollection(toCollect: number, paysWith?: number | null): string {
  const parts = [`Cobra ${formatMoney(toCollect)}`];
  if (paysWith != null) parts.push(`paga con ${formatMoney(paysWith)}`, `cambio ${formatMoney(changeFor(paysWith, toCollect))}`);
  return parts.join(' · ');
}
