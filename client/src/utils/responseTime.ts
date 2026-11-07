import { QuoteTime, ResponseTime } from '../api/generated/enums';
import { addCalendarDays, formatInBusinessZone, getBusinessToday, toBusinessDateTimeInput } from './dates';

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
 * where its customers read how soon it answers, or why nobody reads it yet. Since Fase 29M (ADR 0067) a customer counts once,
 * by their last request, and those of its own team do not count.
 */
export function ownResponseTimeNote(responseTime: ResponseTime | null | undefined): string {
  if (responseTime == null)
    return 'Todavía no contamos solicitudes en línea de 3 clientes para saber en cuánto sueles responder: una cuenta cuando la respondes, o cuando llega su hora o pasa un día sin respuesta.';
  if (responseTime === ResponseTime.Slower)
    return 'Sueles tardar un día o más en responder tus solicitudes en línea, así que tus clientes no lo leen.';
  return 'Lo calculamos con tus solicitudes en línea de los últimos 90 días, la última de cada cliente y sin las de tu equipo. Tus clientes lo leen en tu tarjeta, en tu tienda y donde te piden citas o visitas.';
}

/** The quote times the card says: every one but the slowest, which it never says. */
type ShownQuoteTime = Exclude<QuoteTime, typeof QuoteTime.Slower>;

const QUOTE_LABELS: Record<ShownQuoteTime, string> = {
  [QuoteTime.WithinADay]: 'Suele mandar la cotización en menos de un día',
  [QuoteTime.WithinThreeDays]: 'Suele mandar la cotización en menos de 3 días',
  [QuoteTime.WithinAWeek]: 'Suele mandar la cotización en menos de una semana',
};

/**
 * What the card of a trade says of how soon it sends the quote after the visit (Fase 29S, ADR 0073): within what it sent at
 * least half of them, as the server sums it up. Nothing without enough visits quoted or past a week.
 */
export function quoteTimeLabel(quoteTime: QuoteTime | null | undefined): string | null {
  return quoteTime == null || quoteTime === QuoteTime.Slower ? null : QUOTE_LABELS[quoteTime];
}

/** What its owner reads of how soon it quotes, while Explorar counts its requests and it receives visits (Fase 29S). */
export function ownQuoteTimeNote(quoteTime: QuoteTime | null | undefined): string {
  if (quoteTime == null)
    return 'Todavía no contamos visitas en línea de 3 clientes con su cotización mandada para saber en cuánto sueles cotizar.';
  if (quoteTime === QuoteTime.Slower)
    return 'Sueles tardar una semana o más en mandar la cotización después de la visita, así que tus clientes no lo leen.';
  return 'Lo contamos desde que termina cada visita en línea hasta que mandas su cotización, en los últimos 90 días, la última de cada cliente. Tus clientes lo leen en tu tarjeta y donde te piden la visita.';
}

/**
 * By when the business answers a request online still waiting to be in time (Fase 29L, ADR 0066), in its own time zone:
 * the server works out the limit (`answerBy`: the time asked for, or a day after it arrived, whichever comes first), so it
 * falls today or tomorrow while it has not passed; another day is named only when the device clock is off.
 */
export function answerInTimeLabel(answerBy: string, now: Date = new Date()): string {
  const limit = new Date(answerBy);
  if (limit.getTime() <= now.getTime()) return 'Ya pasó el límite para responder a tiempo';
  const time = formatInBusinessZone(limit, { hour: 'numeric', minute: '2-digit' });
  // "de las 3:30", but "de la 1:30" in the one o'clock hour.
  const at = `${time.startsWith('1:') ? 'la' : 'las'} ${time}`;
  const day = toBusinessDateTimeInput(limit).slice(0, 10);
  const today = getBusinessToday(now);
  const when = day === today
    ? `antes de ${at}`
    : day === addCalendarDays(today, 1)
      ? `mañana antes de ${at}`
      : `antes del ${formatInBusinessZone(limit, { weekday: 'long', day: 'numeric' })} a ${at}`;
  return `Responde ${when} para que cuente a tiempo`;
}

/** What the agenda says of a request online from the business's own team (Fase 29M, ADR 0067). */
export const FROM_TEAM_LABEL = 'De tu equipo: no cuenta en Explorar';
