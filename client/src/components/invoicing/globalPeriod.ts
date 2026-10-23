import type { GlobalInvoicePeriodicity } from '../../types';
import { periodicityLabel } from '../../utils/cfdi';
import { describePeriod } from '../reports/reportPeriods';

/** "Semanal · 13 al 19 oct. 2026": the period of a global invoice, which never crosses a year. */
export function globalPeriodLabel(period: { periodicity: GlobalInvoicePeriodicity; periodFrom: string; periodTo: string }): string {
  return `${periodicityLabel(period.periodicity)} · ${describePeriod(period.periodFrom, period.periodTo)} ${period.periodTo.slice(0, 4)}`;
}
