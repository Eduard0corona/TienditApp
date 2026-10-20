import type { CashTransactionType } from '../../types';
import { CashTransactionType as CashTransactionTypeConst } from '../../types';

/** Bills and coins in circulation in Mexico, largest first. */
export const MXN_DENOMINATIONS = [1000, 500, 200, 100, 50, 20, 10, 5, 2, 1, 0.5];

// Money typed by the cashier: empty, negative or more than 2 decimals is rejected (the API stores cents).
export function parseCashAmount(value: string): number | null {
  const trimmed = value.trim().replace(/[$,\s]/g, '');
  if (!/^\d+(\.\d{1,2})?$/.test(trimmed)) return null;
  return Number(trimmed);
}

export function getTransactionTypeLabel(type: CashTransactionType): string {
  switch (type) {
    case CashTransactionTypeConst.SalePayment: return 'Pago de venta';
    case CashTransactionTypeConst.Refund: return 'Reembolso';
    case CashTransactionTypeConst.Expense: return 'Gasto';
    case CashTransactionTypeConst.Withdrawal: return 'Retiro';
    case CashTransactionTypeConst.Deposit: return 'Depósito';
    case CashTransactionTypeConst.OpeningAdjustment: return 'Ajuste apertura';
    case CashTransactionTypeConst.ClosingAdjustment: return 'Ajuste cierre';
    case CashTransactionTypeConst.Tip: return 'Propina';
    // What a customer left in their favor and what was given back to them (Fase 26D).
    case CashTransactionTypeConst.CustomerDeposit: return 'Dejó a favor';
    case CashTransactionTypeConst.CustomerDepositReturn: return 'Saldo a favor devuelto';
    default: return String(type);
  }
}

/**
 * Who opened, closed or charged: their name when the API gives it (a member of the business), else their user, as the
 * API stores it (an email, or "Sistema"). Never both, so the cut shows no more about the person than before.
 */
export function personLabel(name: string | null | undefined, user: string): string {
  return name?.trim() || user;
}

/** Short label of a closing difference: "Cuadró", "Faltan $16.00" or "Sobran $5.00". */
export function describeDifference(difference: number, formatMoney: (value: number) => string): string {
  if (difference === 0) return 'Cuadró';
  return difference < 0 ? `Faltan ${formatMoney(-difference)}` : `Sobran ${formatMoney(difference)}`;
}
