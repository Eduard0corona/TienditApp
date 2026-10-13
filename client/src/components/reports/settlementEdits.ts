import type {
  CreatePlatformSettlementBody,
  PlatformSettlement,
  PlatformSettlementDetail,
  PlatformSettlementOrder,
} from '../../api/platformSettlements';
import { PlatformSettlementAdjustmentKind } from '../../types';
import { calendarDaysBetween } from '../../utils/dates';
import { parseMoneyInput, toCents } from '../../utils/money';
import { previousWeek } from './reportPeriods';

// The limits of the API (PlatformSettlement and PlatformSettlementAdjustment): the screen says them before saving, and the
// server still decides.
export const MAX_PERIOD_DAYS = 31;
/** The earliest day a period starts on (yyyy-mm-dd): a guard against a year typed wrong. */
export const EARLIEST_PERIOD_FROM = '2000-01-01';
export const MAX_DEPOSITED_AMOUNT = 10_000_000;
export const MAX_ADJUSTMENT_AMOUNT = 1_000_000;
export const MAX_REFERENCE_LENGTH = 60;
export const MAX_NOTES_LENGTH = 500;
export const MAX_DESCRIPTION_LENGTH = 200;
export const MAX_ORDERS = 2_000;
export const MAX_ADJUSTMENTS = 100;
export const MAX_VOID_REASON_LENGTH = 500;

// The server's own words (PlatformSettlement), so the screen and the API say the same.
export const CHOOSE_THE_PERIOD_MESSAGE = 'Elige el periodo que paga el depósito.';
export const PERIOD_ORDER_MESSAGE = 'El periodo termina antes de empezar.';
export const PERIOD_TOO_LONG_MESSAGE = 'El periodo puede tener hasta 31 días.';
export const PERIOD_IN_THE_FUTURE_MESSAGE = 'El periodo no puede terminar después de hoy.';
export const PERIOD_TOO_OLD_MESSAGE = 'Revisa el año del periodo: no puede empezar antes de 2000.';
export const CHOOSE_THE_DEPOSIT_DATE_MESSAGE = 'Elige la fecha del depósito.';
export const DEPOSIT_BEFORE_PERIOD_MESSAGE = 'La fecha del depósito no puede ser anterior al periodo.';
export const DEPOSIT_IN_THE_FUTURE_MESSAGE = 'La fecha del depósito no puede ser futura.';
export const DEPOSITED_AMOUNT_MESSAGE = 'Lo depositado va de -$10,000,000 a $10,000,000, con dos decimales.';
export const REFERENCE_TOO_LONG_MESSAGE = 'La referencia admite hasta 60 caracteres.';
export const NOTES_TOO_LONG_MESSAGE = 'Las notas admiten hasta 500 caracteres.';
export const TOO_MANY_ORDERS_MESSAGE = 'Una liquidación lleva hasta 2,000 pedidos.';
export const TOO_MANY_ADJUSTMENTS_MESSAGE = 'Una liquidación lleva hasta 100 ajustes.';
export const ADJUSTMENT_KIND_MESSAGE = 'Elige el motivo de cada ajuste.';
export const ADJUSTMENT_AMOUNT_MESSAGE = 'Cada ajuste va de -$1,000,000 a $1,000,000, sin ser $0, con dos decimales.';
export const ADJUSTMENT_DESCRIPTION_REQUIRED_MESSAGE = 'Un ajuste «Otro» necesita una descripción.';
export const ADJUSTMENT_DESCRIPTION_TOO_LONG_MESSAGE = 'La descripción de un ajuste admite hasta 200 caracteres.';

/** What «Registrar la diferencia como ajuste» writes: the person may say more before saving. */
export const DIFFERENCE_DESCRIPTION = 'Diferencia sin identificar';

/**
 * A comma only groups thousands («12,500.50»): parseMoneyInput drops every comma, and «32,50» typed as a decimal would be
 * read as 3,250, so it is refused (as in Precios en apps).
 */
function commasGroupThousands(text: string): boolean {
  return !text.includes(',') || /^\$?\s*\d{1,3}(,\d{3})+(\.\d*)?$/.test(text.trim());
}

/**
 * An amount with its sign as typed («-1,250.50», «−$80», «$300»): undefined when empty, when it has more than two decimals
 * or when it goes further from zero than <max>.
 */
export function parseSignedAmount(text: string, max: number): number | undefined {
  const trimmed = text.trim();
  if (trimmed === '') return undefined;
  const negative = /^[-−]/.test(trimmed);
  const amount = negative ? trimmed.slice(1).trim() : trimmed;
  if (!commasGroupThousands(amount)) return undefined;
  const value = parseMoneyInput(amount);
  if (value === null || value > max) return undefined;
  return negative && value !== 0 ? -value : value;
}

/** An amount of the server in a box: two decimals, with «-» when negative. */
export function toAmountText(amount: number): string {
  return amount.toFixed(2);
}

/** Cents back to the text of a box (-5000 is «-50.00»). */
export function centsToText(cents: number): string {
  return toAmountText(cents / 100);
}

/** What is wrong with the period of the deposit, or '' (dates as yyyy-mm-dd, the business's days). */
export function periodProblem(from: string, to: string, today: string): string {
  if (!from || !to) return CHOOSE_THE_PERIOD_MESSAGE;
  if (to < from) return PERIOD_ORDER_MESSAGE;
  if (calendarDaysBetween(from, to) + 1 > MAX_PERIOD_DAYS) return PERIOD_TOO_LONG_MESSAGE;
  if (from < EARLIEST_PERIOD_FROM) return PERIOD_TOO_OLD_MESSAGE;
  if (to > today) return PERIOD_IN_THE_FUTURE_MESSAGE;
  return '';
}

/** What is wrong with the date of the deposit, or ''. */
export function depositDateProblem(depositedOn: string, from: string, today: string): string {
  if (!depositedOn) return CHOOSE_THE_DEPOSIT_DATE_MESSAGE;
  if (from && depositedOn < from) return DEPOSIT_BEFORE_PERIOD_MESSAGE;
  if (depositedOn > today) return DEPOSIT_IN_THE_FUTURE_MESSAGE;
  return '';
}

/** Where an order falls against the period: before it (a statement may bring it late), in it, or after it (it cannot go). */
export type Placement = 'before' | 'in' | 'after';

export function placementOf(registeredOn: string, from: string, to: string): Placement {
  if (to && registeredOn > to) return 'after';
  if (from && registeredOn < from) return 'before';
  return 'in';
}

/** An adjustment as typed: its reason, an optional description («Otro» needs one) and its signed amount. */
export interface AdjustmentRow {
  /** Only the screen's: keeps each row's boxes as rows come and go. */
  key: string;
  kind: PlatformSettlementAdjustmentKind | '';
  description: string;
  amountText: string;
}

export interface AdjustmentCheck {
  /** The amount, once the API would take it. */
  amount?: number;
  kindProblem: string;
  descriptionProblem: string;
  amountProblem: string;
}

export function checkAdjustment(row: AdjustmentRow): AdjustmentCheck {
  const amount = parseSignedAmount(row.amountText, MAX_ADJUSTMENT_AMOUNT);
  const description = row.description.trim();
  return {
    amount: amount === undefined || amount === 0 ? undefined : amount,
    kindProblem: row.kind === '' ? ADJUSTMENT_KIND_MESSAGE : '',
    descriptionProblem: description.length > MAX_DESCRIPTION_LENGTH
      ? ADJUSTMENT_DESCRIPTION_TOO_LONG_MESSAGE
      : row.kind === PlatformSettlementAdjustmentKind.Other && description === '' ? ADJUSTMENT_DESCRIPTION_REQUIRED_MESSAGE : '',
    amountProblem: amount === undefined || amount === 0 ? ADJUSTMENT_AMOUNT_MESSAGE : '',
  };
}

/** The settlement as the person is writing it. */
export interface SettlementForm {
  from: string;
  to: string;
  depositedOn: string;
  depositedText: string;
  reference: string;
  notes: string;
  adjustments: AdjustmentRow[];
  /** The orders the person checked or unchecked, by id; the rest follow the proposal. */
  picks: Record<string, boolean>;
}

let nextAdjustmentKey = 0;

/** A key for a new row of adjustments, unique on the screen. */
export function newAdjustmentKey(): string {
  nextAdjustmentKey += 1;
  return `adjustment-${nextAdjustmentKey}`;
}

/** A new settlement: last week, deposited today, nothing typed yet. */
export function newSettlementForm(today: string): SettlementForm {
  const week = previousWeek(today);
  return { ...week, depositedOn: today, depositedText: '', reference: '', notes: '', adjustments: [], picks: {} };
}

/** A saved settlement in the boxes. */
export function formOf(detail: PlatformSettlementDetail): SettlementForm {
  const { settlement } = detail;
  return {
    from: settlement.periodFrom,
    to: settlement.periodTo,
    depositedOn: settlement.depositedOn,
    depositedText: toAmountText(settlement.depositedAmount),
    reference: settlement.reference ?? '',
    notes: settlement.notes ?? '',
    adjustments: detail.adjustments.map((adjustment) => ({
      key: newAdjustmentKey(),
      kind: adjustment.kind,
      description: adjustment.description ?? '',
      amountText: toAmountText(adjustment.amount),
    })),
    picks: {},
  };
}

/** The orders of the settlement and the pending ones it could add, once each, the oldest first. */
export function mergeOrders(saved: PlatformSettlementOrder[], candidates: PlatformSettlementOrder[]): PlatformSettlementOrder[] {
  const seen = new Set(saved.map((order) => order.orderId));
  return [...saved, ...candidates.filter((order) => !seen.has(order.orderId))]
    .sort((a, b) => a.registeredAt.localeCompare(b.registeredAt) || a.orderId.localeCompare(b.orderId));
}

/**
 * Whether an order goes in the settlement: never one registered after the period; otherwise what the person chose or, until
 * then, the proposal: the orders already saved and, in a new settlement, those of the period.
 */
export function isPicked(
  order: PlatformSettlementOrder,
  form: SettlementForm,
  saved: ReadonlySet<string>,
  proposesThePeriod: boolean,
): boolean {
  const placement = placementOf(order.registeredOn, form.from, form.to);
  if (placement === 'after') return false;
  return form.picks[order.orderId] ?? (saved.has(order.orderId) || (proposesThePeriod && placement === 'in'));
}

/** The sums the screen shows while the person writes, in cents; the server counts them again when it saves. */
export interface SettlementTotals {
  pickedCount: number;
  ordersCents: number;
  adjustmentsCents: number;
  expectedCents: number;
  /** Undefined while the box of the deposit holds something the API would refuse. */
  depositedCents?: number;
  differenceCents?: number;
}

export function totalsOf(pickedOrders: PlatformSettlementOrder[], adjustments: AdjustmentRow[], depositedText: string): SettlementTotals {
  // What each order brings now: the server counts it again the same way when it saves.
  const ordersCents = pickedOrders.reduce((sum, order) => sum + toCents(order.currentExpectedNet), 0);
  const adjustmentsCents = adjustments.reduce((sum, row) => sum + toCents(checkAdjustment(row).amount ?? 0), 0);
  const expectedCents = ordersCents + adjustmentsCents;
  const deposited = parseSignedAmount(depositedText, MAX_DEPOSITED_AMOUNT);
  const depositedCents = deposited === undefined ? undefined : toCents(deposited);
  return {
    pickedCount: pickedOrders.length,
    ordersCents,
    adjustmentsCents,
    expectedCents,
    depositedCents,
    differenceCents: depositedCents === undefined ? undefined : depositedCents - expectedCents,
  };
}

/** The sums of a saved settlement, as the server counted them. */
export function savedTotals(settlement: PlatformSettlement): SettlementTotals {
  return {
    pickedCount: settlement.orderCount,
    ordersCents: toCents(settlement.ordersTotal),
    adjustmentsCents: toCents(settlement.adjustmentsTotal),
    expectedCents: toCents(settlement.expectedDeposit),
    depositedCents: toCents(settlement.depositedAmount),
    differenceCents: toCents(settlement.difference),
  };
}

/** What is wrong with the deposit's boxes, by box ('' where nothing is). */
export interface DepositProblems {
  period: string;
  depositedOn: string;
  deposited: string;
  reference: string;
  notes: string;
}

export function depositProblems(form: SettlementForm, today: string): DepositProblems {
  return {
    period: periodProblem(form.from, form.to, today),
    depositedOn: depositDateProblem(form.depositedOn, form.from, today),
    deposited: parseSignedAmount(form.depositedText, MAX_DEPOSITED_AMOUNT) === undefined ? DEPOSITED_AMOUNT_MESSAGE : '',
    reference: form.reference.trim().length > MAX_REFERENCE_LENGTH ? REFERENCE_TOO_LONG_MESSAGE : '',
    notes: form.notes.trim().length > MAX_NOTES_LENGTH ? NOTES_TOO_LONG_MESSAGE : '',
  };
}

/** The first thing that keeps the settlement from being saved, or '' when the API would take it. */
export function firstProblem(form: SettlementForm, pickedCount: number, today: string): string {
  const deposit = Object.values(depositProblems(form, today)).find(Boolean);
  if (deposit) return deposit;
  if (pickedCount > MAX_ORDERS) return TOO_MANY_ORDERS_MESSAGE;
  if (form.adjustments.length > MAX_ADJUSTMENTS) return TOO_MANY_ADJUSTMENTS_MESSAGE;
  for (const row of form.adjustments) {
    const check = checkAdjustment(row);
    const problem = check.kindProblem || check.descriptionProblem || check.amountProblem;
    if (problem) return problem;
  }
  return '';
}

/** The settlement to save, as the API takes it, once nothing keeps it from being saved. */
export type SettlementBody = Omit<CreatePlatformSettlementBody, 'channel'>;

export function buildBody(form: SettlementForm, pickedIds: string[]): SettlementBody {
  return {
    periodFrom: form.from,
    periodTo: form.to,
    depositedOn: form.depositedOn,
    depositedAmount: parseSignedAmount(form.depositedText, MAX_DEPOSITED_AMOUNT) ?? 0,
    reference: form.reference.trim() || null,
    notes: form.notes.trim() || null,
    orderIds: pickedIds,
    adjustments: form.adjustments.map((row) => ({
      kind: row.kind as PlatformSettlementAdjustmentKind,
      description: row.description.trim() || null,
      amount: checkAdjustment(row).amount ?? 0,
    })),
  };
}

/** A body in one string to compare: the orders in any order, the amounts in cents. */
function fingerprint(body: SettlementBody): string {
  return JSON.stringify({
    ...body,
    depositedAmount: toCents(body.depositedAmount),
    orderIds: [...body.orderIds].sort(),
    adjustments: body.adjustments.map((adjustment) => ({ ...adjustment, amount: toCents(adjustment.amount) })),
  });
}

/** The saved settlement as a body, to know whether the screen changed it. */
export function savedBody(detail: PlatformSettlementDetail): SettlementBody {
  const { settlement } = detail;
  return {
    periodFrom: settlement.periodFrom,
    periodTo: settlement.periodTo,
    depositedOn: settlement.depositedOn,
    depositedAmount: settlement.depositedAmount,
    reference: settlement.reference ?? null,
    notes: settlement.notes ?? null,
    orderIds: detail.orders.map((order) => order.orderId),
    adjustments: detail.adjustments.map((adjustment) => ({
      kind: adjustment.kind,
      description: adjustment.description ?? null,
      amount: adjustment.amount,
    })),
  };
}

export function sameBody(a: SettlementBody, b: SettlementBody): boolean {
  return fingerprint(a) === fingerprint(b);
}

/**
 * Whether the screen says something other than the saved settlement. A deposit box the API would refuse (emptied, for
 * instance) is always a change, even over a saved $0: the body counts it as 0 only to be complete.
 */
export function editedSince(detail: PlatformSettlementDetail, form: SettlementForm, body: SettlementBody): boolean {
  return parseSignedAmount(form.depositedText, MAX_DEPOSITED_AMOUNT) === undefined || !sameBody(body, savedBody(detail));
}

/**
 * The saved orders whose expected net changed since the save (a refund in between): saving counts them again, and closing
 * without saving would answer with the new figures to review.
 */
export function changedSinceSaved(detail: PlatformSettlementDetail): PlatformSettlementOrder[] {
  return detail.orders.filter((order) => toCents(order.currentExpectedNet) !== toCents(order.expectedNet));
}

/** «Registrar la diferencia como ajuste»: an «Otro» that explains it, which the person may describe better. */
export function differenceAdjustment(differenceCents: number): AdjustmentRow {
  return {
    key: newAdjustmentKey(),
    kind: PlatformSettlementAdjustmentKind.Other,
    description: DIFFERENCE_DESCRIPTION,
    amountText: centsToText(differenceCents),
  };
}
