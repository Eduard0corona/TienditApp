import { addCalendarDays, formatCalendarDate } from '../../utils/dates';

// Periods of the reports, as calendar dates of the business (yyyy-mm-dd).

/** Monday to Sunday of the week before that day's: the weeks the platforms settle. */
export function previousWeek(today: string): { from: string; to: string } {
  const daysSinceMonday = (new Date(`${today}T12:00:00Z`).getUTCDay() + 6) % 7;
  const monday = addCalendarDays(today, -daysSinceMonday - 7);
  return { from: monday, to: addCalendarDays(monday, 6) };
}

/** "oct.", whatever the period the browser's data puts or leaves after the abbreviation. */
function shortMonth(date: string): string {
  return `${formatCalendarDate(date, { month: 'short' }).replace(/\.$/, '')}.`;
}

/** "6 al 12 oct.", "28 sep. al 4 oct.", or with the years when the period crosses one. */
export function describePeriod(from: string, to: string): string {
  const day = (date: string) => formatCalendarDate(date, { day: 'numeric' });
  if (from === to) return `${day(from)} ${shortMonth(from)}`;
  if (from.slice(0, 4) !== to.slice(0, 4)) return `${day(from)} ${shortMonth(from)} ${from.slice(0, 4)} al ${day(to)} ${shortMonth(to)} ${to.slice(0, 4)}`;
  if (from.slice(5, 7) !== to.slice(5, 7)) return `${day(from)} ${shortMonth(from)} al ${day(to)} ${shortMonth(to)}`;
  return `${day(from)} al ${day(to)} ${shortMonth(to)}`;
}
