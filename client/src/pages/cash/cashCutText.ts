import type { CashSessionDto, DailyCashCutDto } from '../../types';
import { CashSessionStatus } from '../../types';
import { formatMoney, toCents } from '../../utils/money';
import { describeDifference } from './cashFormat';

const dateTime = (value: string) => new Date(value).toLocaleString('es-MX', { dateStyle: 'short', timeStyle: 'short' });

/** Wallet and other methods, shown together as "Otros" (added in cents). */
export function getOtherMethodsTotal(cut: DailyCashCutDto): number {
  return (toCents(cut.walletTotal) + toCents(cut.otherTotal)) / 100;
}

/** The figures of the day's cut, in the order the printed cut and the message show them. */
export function getDailyCutRows(cut: DailyCashCutDto): Array<[label: string, amount: number]> {
  const rows: Array<[string, number]> = [
    ['Cobrado', cut.salesPaymentsTotal],
    ['Efectivo', cut.cashTotal],
    ['Tarjeta', cut.cardTotal],
    ['Transferencia', cut.transferTotal],
    ['Otros', getOtherMethodsTotal(cut)],
    ['Neto en efectivo', cut.netCashOnlyFlow],
  ];
  // Tips are for the team: apart from what was sold.
  if (cut.tipsTotal > 0) rows.push(['Propinas', cut.tipsTotal], ['Propinas en efectivo', cut.cashTipsTotal]);
  return rows;
}

export interface DailyCutScope {
  businessName?: string;
  /** "5 de octubre de 2026". */
  dateLabel: string;
  /** "Todas las sucursales", a branch, and the user when the cut is filtered by one. */
  scopeLabel: string;
}

/** The day's cut as text, for WhatsApp: the same figures as the printed one. */
export function buildDailyCutText(cut: DailyCashCutDto, scope: DailyCutScope): string {
  const text: string[] = [];
  if (scope.businessName) text.push(`*${scope.businessName}*`);
  text.push(`*Corte del día* · ${scope.dateLabel}`, scope.scopeLabel, '');
  getDailyCutRows(cut).forEach(([label, amount], index) =>
    text.push(index === 0 ? `*${label}: ${formatMoney(amount)}* (${cut.salesPaymentsCount} pagos)` : `${label}: ${formatMoney(amount)}`));
  if (cut.byUser.length > 0) {
    text.push('', 'Por usuario');
    cut.byUser.forEach((item) => text.push(`• ${item.userName}: ${item.paymentsCount} pagos / ${formatMoney(item.totalAmount)}`));
  }
  return text.join('\n');
}

/** The cut of one shift as text, for WhatsApp: the same figures as the printed ticket, without the movements. */
export function buildShiftCutText(session: CashSessionDto, businessName?: string): string {
  const closed = session.status === CashSessionStatus.Closed;
  const expected = session.expectedClosingAmount ?? session.expectedBalance;
  const text: string[] = [];
  if (businessName) text.push(`*${businessName}*`);
  text.push(
    `*${closed ? 'Corte de caja' : 'Corte parcial (caja abierta)'}*`,
    `${session.openedBy}${session.branchName ? ` · ${session.branchName}` : ''}`,
    `Abrió: ${dateTime(session.openedAt)}`,
  );
  if (closed && session.closedAt) text.push(`Cerró${session.closedBy ? ` (${session.closedBy})` : ''}: ${dateTime(session.closedAt)}`);
  text.push(
    '',
    `Fondo inicial: ${formatMoney(session.openingAmount)}`,
    `Entró en efectivo: ${formatMoney(session.inflows)}`,
    `Salió en efectivo: ${formatMoney(session.outflows)}`,
    `*Debía haber: ${formatMoney(expected)}*`,
  );
  if (closed) {
    text.push(`*Contado: ${formatMoney(session.closingAmount ?? 0)}*`);
    if (session.closingDifference !== null && session.closingDifference !== undefined)
      text.push(`*Diferencia: ${describeDifference(session.closingDifference, formatMoney)}*`);
  }
  text.push(`Tarjeta y otros (fuera de caja): ${formatMoney(session.nonCashInflows)}`);
  if (session.closingNotes) text.push('', `Nota: ${session.closingNotes}`);
  return text.join('\n');
}
