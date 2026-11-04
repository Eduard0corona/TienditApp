import { ResponseTime } from '../api/generated/enums';

/** The times the card says: every one but the slowest, which it never says. */
type ShownTime = Exclude<ResponseTime, typeof ResponseTime.Slower>;

const LABELS: Record<ShownTime, string> = {
  [ResponseTime.WithinAnHour]: 'Suele responder en menos de una hora',
  [ResponseTime.WithinThreeHours]: 'Suele responder en menos de 3 horas',
  [ResponseTime.WithinTwelveHours]: 'Suele responder en menos de 12 horas',
  [ResponseTime.WithinADay]: 'Suele responder en menos de un día',
};

/**
 * What the card of a business says of how soon it answers the requests its customers make online (Fase 29J, ADR 0064):
 * within what it answered at least half of them, as the server sums it up. Nothing without enough requests or past a day.
 */
export function responseTimeLabel(responseTime: ResponseTime | null | undefined): string | null {
  return responseTime == null || responseTime === ResponseTime.Slower ? null : LABELS[responseTime];
}

/**
 * What its owner reads under the preview of its card while Explorar counts its requests online (Fase 29K, ADR 0065):
 * where its customers read how soon it answers, or why nobody reads it yet.
 */
export function ownResponseTimeNote(responseTime: ResponseTime | null | undefined): string {
  if (responseTime == null)
    return 'Cuando tengas 3 solicitudes en línea de los últimos 90 días, tu tarjeta dirá en cuánto sueles responder.';
  if (responseTime === ResponseTime.Slower)
    return 'Sueles tardar un día o más en responder tus solicitudes en línea, así que tu tarjeta no lo dice.';
  return 'Tus clientes lo leen en tu tarjeta, en tu tienda y donde te piden citas o visitas, por tus solicitudes en línea de los últimos 90 días.';
}
