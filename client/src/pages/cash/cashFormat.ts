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

export function getTodayInputDate(): string {
  const date = new Date();
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
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
    default: return String(type);
  }
}

/** Short label of a closing difference: "Cuadró", "Faltan $16.00" or "Sobran $5.00". */
export function describeDifference(difference: number, formatMoney: (value: number) => string): string {
  if (difference === 0) return 'Cuadró';
  return difference < 0 ? `Faltan ${formatMoney(-difference)}` : `Sobran ${formatMoney(difference)}`;
}
