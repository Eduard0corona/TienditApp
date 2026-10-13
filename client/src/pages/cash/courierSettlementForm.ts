// What a courier hands over for the cash it collected (Fase 22D, Caja › Repartidores). Mirrors the API (CourierSettlement):
// the orders paid with what they owe, the adjustments (the courier's trip, its fee for collecting and a shortage are taken
// off; «Otro» takes off or adds and says what it was), and what was handed over, which must be the orders plus the
// adjustments to the cent. The screen says it before saving; the server still decides.
import type { CourierOrder, CreateCourierSettlementBody } from '../../api/courierSettlements';
import { parseSignedAmount } from '../../components/reports/settlementEdits';
import { CourierSettlementAdjustmentKind, DeliveryCourier, OrderPaymentMethod } from '../../types';
import { formatMoney, toCents } from '../../utils/money';

export const MAX_ADJUSTMENTS = 50;
export const MAX_AMOUNT = 1_000_000;
export const MAX_DESCRIPTION_LENGTH = 200;
export const MAX_REFERENCE_LENGTH = 60;
export const MAX_NOTES_LENGTH = 500;

// The server's words (CourierSettlement), so the screen and the API say the same.
export const ADJUSTMENT_KIND_MESSAGE = 'Elige el motivo de cada ajuste.';
export const ADJUSTMENT_AMOUNT_MESSAGE = 'Cada ajuste va de $0.01 a $1,000,000, con dos decimales.';
export const ADJUSTMENT_DESCRIPTION_REQUIRED_MESSAGE = 'Un ajuste «Otro» necesita una descripción.';
export const HANDED_AMOUNT_MESSAGE = 'Lo que entregó va de $0 a $1,000,000, con dos decimales.';
export const NO_ORDERS_MESSAGE = 'Elige al menos un pedido que cobró el repartidor.';

/** The server's words (CourierSettlement.DoesNotBalanceMessage). */
export function doesNotBalanceMessage(handed: number, expected: number): string {
  return `Entregó ${formatMoney(handed)} y los pedidos con sus ajustes dan ${formatMoney(expected)}: la diferencia debe ser $0.00. `
    + 'Registra lo que falta como un ajuste.';
}

export const ADJUSTMENT_KIND_LABELS: Record<CourierSettlementAdjustmentKind, string> = {
  [CourierSettlementAdjustmentKind.TripFee]: 'Viaje',
  [CourierSettlementAdjustmentKind.CollectionFee]: 'Comisión por cobrar',
  [CourierSettlementAdjustmentKind.Shortage]: 'Faltante',
  [CourierSettlementAdjustmentKind.Other]: 'Otro',
};

/** The trip, the fee and a shortage are always taken off what the courier hands over; «Otro» says which way. */
export function isDeduction(kind: CourierSettlementAdjustmentKind): boolean {
  return kind !== CourierSettlementAdjustmentKind.Other;
}

/** An adjustment as typed: the amount without sign, and for «Otro» whether it adds (it takes off otherwise). */
export interface AdjustmentRow {
  key: string;
  kind: CourierSettlementAdjustmentKind | '';
  description: string;
  amountText: string;
  adds: boolean;
}

let nextKey = 0;
export function newAdjustment(patch: Partial<AdjustmentRow> = {}): AdjustmentRow {
  nextKey += 1;
  return { key: `adjustment-${nextKey}`, kind: '', description: '', amountText: '', adds: false, ...patch };
}

export interface AdjustmentCheck {
  kindProblem: string;
  amountProblem: string;
  descriptionProblem: string;
  /** Signed as the server keeps it (negative when taken off), or null while it is not valid. */
  amount: number | null;
}

export function checkAdjustment(row: AdjustmentRow): AdjustmentCheck {
  const typed = parseSignedAmount(row.amountText, MAX_AMOUNT);
  const valid = typed !== undefined && typed > 0;
  const kindProblem = row.kind === '' ? ADJUSTMENT_KIND_MESSAGE : '';
  const descriptionProblem = row.kind === CourierSettlementAdjustmentKind.Other && !row.description.trim()
    ? ADJUSTMENT_DESCRIPTION_REQUIRED_MESSAGE : '';
  const takesOff = row.kind === '' ? true : isDeduction(row.kind) || !row.adds;
  return {
    kindProblem,
    amountProblem: valid ? '' : ADJUSTMENT_AMOUNT_MESSAGE,
    descriptionProblem,
    amount: valid && !kindProblem ? (takesOff ? -typed : typed) : null,
  };
}

/** What was handed over, as typed: undefined while it is not an amount from $0 to $1,000,000 with two decimals. */
export function parseHandedAmount(text: string): number | undefined {
  const amount = parseSignedAmount(text, MAX_AMOUNT);
  return amount !== undefined && amount >= 0 ? amount : undefined;
}

export interface SettlementSummary {
  orders: number;
  adjustments: number;
  /** What the courier should hand over: the orders plus the adjustments. */
  expected: number;
  handed: number | undefined;
  /** Handed minus expected: negative when it handed less. Undefined while «entregó» is not an amount. */
  difference: number | undefined;
  /** Every adjustment is valid. */
  adjustmentsValid: boolean;
}

export function summarize(orders: readonly CourierOrder[], rows: readonly AdjustmentRow[], handedText: string): SettlementSummary {
  const ordersCents = orders.reduce((sum, order) => sum + toCents(order.balance), 0);
  const checks = rows.map(checkAdjustment);
  const adjustmentsValid = checks.every((check) => check.amount !== null && !check.descriptionProblem);
  const adjustmentsCents = checks.reduce((sum, check) => sum + toCents(check.amount ?? 0), 0);
  const handed = parseHandedAmount(handedText);
  const expectedCents = ordersCents + adjustmentsCents;
  return {
    orders: ordersCents / 100,
    adjustments: adjustmentsCents / 100,
    expected: expectedCents / 100,
    handed,
    difference: handed === undefined ? undefined : (toCents(handed) - expectedCents) / 100,
    adjustmentsValid,
  };
}

/** The adjustment that explains a difference: a shortage when it handed less, «Otro» that adds when it handed more. */
export function adjustmentForDifference(difference: number): AdjustmentRow {
  return difference < 0
    ? newAdjustment({ kind: CourierSettlementAdjustmentKind.Shortage, amountText: (-difference).toFixed(2) })
    : newAdjustment({ kind: CourierSettlementAdjustmentKind.Other, amountText: difference.toFixed(2), adds: true, description: 'Sobrante' });
}

export function toBody(
  courier: DeliveryCourier,
  method: OrderPaymentMethod,
  handed: number,
  orders: readonly CourierOrder[],
  rows: readonly AdjustmentRow[],
  reference: string,
  notes: string,
): CreateCourierSettlementBody {
  return {
    courier,
    method,
    handedAmount: handed,
    orderIds: orders.map((order) => order.orderId),
    // What these orders owed on screen: if a balance changed meanwhile, the server refuses instead of charging another amount.
    ordersTotal: orders.reduce((sum, order) => sum + toCents(order.balance), 0) / 100,
    adjustments: rows.map((row) => ({
      kind: row.kind as CourierSettlementAdjustmentKind,
      description: row.description.trim() || null,
      amount: checkAdjustment(row).amount!,
    })),
    reference: reference.trim() || null,
    notes: notes.trim() || null,
  };
}
