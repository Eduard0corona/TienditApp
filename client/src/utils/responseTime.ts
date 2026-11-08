import { OrderConfirmTime, QuoteTime, ResponseTime } from '../api/generated/enums';
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

/** The confirm times the card says: every one but the slowest, which it never says. */
type ShownConfirmTime = Exclude<OrderConfirmTime, typeof OrderConfirmTime.Slower>;

const CONFIRM_LABELS: Record<ShownConfirmTime, string> = {
  [OrderConfirmTime.WithinFiveMinutes]: 'Suele confirmar los pedidos en menos de 5 minutos',
  [OrderConfirmTime.WithinFifteenMinutes]: 'Suele confirmar los pedidos en menos de 15 minutos',
  [OrderConfirmTime.WithinHalfAnHour]: 'Suele confirmar los pedidos en menos de media hora',
};

/**
 * What the card and the menu of a business say of how soon it confirms the orders placed on its online menu (Fase 29T, ADR
 * 0074): within what it accepted or declined at least half of them, as the server sums it up. Nothing without enough orders
 * or past half an hour.
 */
export function orderConfirmTimeLabel(confirmTime: OrderConfirmTime | null | undefined): string | null {
  return confirmTime == null || confirmTime === OrderConfirmTime.Slower ? null : CONFIRM_LABELS[confirmTime];
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

/** What the agenda says of a request its business declined as fake (Fase 29V, ADR 0076). */
export const MARKED_FAKE_LABEL = 'Marcada como falsa: no cuenta en Explorar';
