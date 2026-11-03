import { ResponseTime } from '../api/generated/enums';

const LABELS: Partial<Record<ResponseTime, string>> = {
  [ResponseTime.WithinAnHour]: 'Responde en menos de 1 hora',
  [ResponseTime.WithinThreeHours]: 'Responde en menos de 3 horas',
  [ResponseTime.WithinTwelveHours]: 'Responde en menos de 12 horas',
  [ResponseTime.WithinADay]: 'Responde en menos de un día',
};

/**
 * What the card of a business says of how soon it answers the requests its customers make online (Fase 29J, ADR 0064):
 * within what it answered at least half of them, as the server sums it up. Nothing without enough requests or past a day.
 */
export function responseTimeLabel(responseTime: ResponseTime | null | undefined): string | null {
  return responseTime == null ? null : (LABELS[responseTime] ?? null);
}
