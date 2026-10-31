// Dates as the business lives them: in its time zone (TenantSettings.TimeZone), not the device's. A tablet set to
// another zone, or an owner travelling, still sees the business's hours and days. Without a business (login, public
// menu) the device's zone applies.

/** The zones a business can pick, the same list the API accepts (BusinessTimeZones). */
export const DEFAULT_TIME_ZONE = 'America/Mexico_City';
export const BUSINESS_TIME_ZONES: ReadonlyArray<{ id: string; label: string }> = [
  { id: DEFAULT_TIME_ZONE, label: 'Centro (CDMX, Guadalajara, Monterrey, Mérida)' },
  { id: 'America/Cancun', label: 'Sureste (Quintana Roo)' },
  { id: 'America/Mazatlan', label: 'Pacífico (Sinaloa, Sonora, Nayarit, BCS)' },
  { id: 'America/Tijuana', label: 'Noroeste (Baja California, con horario de verano)' },
  { id: 'America/Matamoros', label: 'Frontera noreste (con horario de verano)' },
];

let businessTimeZone: string | undefined;

function isKnownTimeZone(timeZone: string): boolean {
  try {
    new Intl.DateTimeFormat('es-MX', { timeZone });
    return true;
  } catch {
    return false;
  }
}

/** The business's time zone, set when the business loads; unknown or empty falls back to the device's. */
export function setBusinessTimeZone(timeZone?: string | null): void {
  businessTimeZone = timeZone && isKnownTimeZone(timeZone) ? timeZone : undefined;
}

export function getBusinessTimeZone(): string | undefined {
  return businessTimeZone;
}

function toDate(value: string | number | Date): Date {
  return value instanceof Date ? value : new Date(value);
}

/** Any instant in the business's time zone with Intl options; "-" when there is no valid date. */
export function formatInBusinessZone(value: string | number | Date | null | undefined, options: Intl.DateTimeFormatOptions): string {
  if (value === null || value === undefined || value === '') return '-';
  const date = toDate(value);
  return Number.isNaN(date.getTime())
    ? '-'
    : new Intl.DateTimeFormat('es-MX', { ...options, timeZone: businessTimeZone }).format(date);
}

/** "4 oct 2026, 12:30 p.m." */
export function formatDateTime(value?: string | null): string {
  return formatInBusinessZone(value, { dateStyle: 'medium', timeStyle: 'short' });
}

/** "4 oct 2026" */
export function formatDate(value?: string | null): string {
  return formatInBusinessZone(value, { dateStyle: 'medium' });
}

/** "04/10/26, 12:30 p.m.", for tickets and dense lists. */
export function formatShortDateTime(value?: string | number | Date | null): string {
  return formatInBusinessZone(value, { dateStyle: 'short', timeStyle: 'short' });
}

/** "12:30 p.m." */
export function formatTime(value?: string | number | Date | null): string {
  return formatInBusinessZone(value, { timeStyle: 'short' });
}

/** A sentence that ends in an hour («p.m.») or an abbreviated month («oct.») takes no second period. */
export function endSentence(text: string): string {
  return text.endsWith('.') ? text : `${text}.`;
}

/** A calendar date typed in a date input ("2026-10-05") shown as words; it has no time, so no zone shifts it. */
export function formatCalendarDate(date: string, options: Intl.DateTimeFormatOptions = { dateStyle: 'long' }): string {
  const parsed = new Date(`${date}T12:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? date : new Intl.DateTimeFormat('es-MX', { ...options, timeZone: 'UTC' }).format(parsed);
}

/** A calendar date at noon UTC: the same day in words whatever the zone of the device or the business. */
function calendarNoon(date: string): Date | null {
  const parsed = new Date(`${date}T12:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/** The weekday, day and month of a calendar date in words, as Intl writes them in Spanish. */
function calendarParts(date: Date, style: 'short' | 'long'): Record<'weekday' | 'day' | 'month', string> {
  const parts = new Intl.DateTimeFormat('es-MX', { weekday: style, day: 'numeric', month: style, timeZone: 'UTC' }).formatToParts(date);
  // Some versions of the browser's data end an abbreviation with a period ("dic.").
  const part = (type: Intl.DateTimeFormatPartTypes) => (parts.find((item) => item.type === type)?.value ?? '').replace(/\.$/, '');
  return { weekday: part('weekday'), day: part('day'), month: part('month') };
}

/** "Vie 25 dic": a calendar date in a short list (special days), without the comma Intl puts after the weekday. */
export function formatShortCalendarDay(date: string): string {
  const parsed = calendarNoon(date);
  if (!parsed) return date;
  const { weekday, day, month } = calendarParts(parsed, 'short');
  return `${weekday.charAt(0).toUpperCase()}${weekday.slice(1)} ${day} ${month}`;
}

/** "viernes 25 de diciembre", or "viernes 25" without its month, to name a calendar date in a sentence. */
export function formatLongCalendarDay(date: string, withMonth = true): string {
  const parsed = calendarNoon(date);
  if (!parsed) return date;
  const { weekday, day, month } = calendarParts(parsed, 'long');
  return withMonth ? `${weekday} ${day} de ${month}` : `${weekday} ${day}`;
}

/** The calendar date some days later, or earlier when negative: "2026-12-31" plus 1 is "2027-01-01". */
export function addCalendarDays(date: string, days: number): string {
  const parsed = calendarNoon(date);
  if (!parsed) return date;
  parsed.setUTCDate(parsed.getUTCDate() + days);
  return parsed.toISOString().slice(0, 10);
}

/** Days from one calendar date to another: 1 from "2026-12-31" to "2027-01-01", negative when it is before. */
export function calendarDaysBetween(from: string, to: string): number {
  return Math.round((Date.parse(`${to}T12:00:00Z`) - Date.parse(`${from}T12:00:00Z`)) / 86_400_000);
}

/** Value for an <input type="datetime-local"> in local time. */
export function toDateTimeLocalInput(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** Today's date in the business, as a date input wants it ("2026-10-05"). */
export function getBusinessToday(now: Date = new Date()): string {
  // en-CA writes dates as yyyy-mm-dd.
  return new Intl.DateTimeFormat('en-CA', { timeZone: businessTimeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(now);
}

/** Minutes the zone is ahead of UTC at that instant (-360 for Mexico City). */
function zoneOffsetMinutes(timeZone: string, at: Date): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(at);
  const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((p) => p.type === type)?.value ?? 0);
  const asUtc = Date.UTC(part('year'), part('month') - 1, part('day'), part('hour'), part('minute'), part('second'));
  return Math.round((asUtc - at.getTime()) / 60_000);
}

/** The instant a wall clock in the business shows that date and time. */
function businessWallTimeToInstant(date: string, time: string): Date {
  if (!businessTimeZone) return new Date(`${date}T${time}`);
  const [year, month, day] = date.split('-').map(Number);
  const [hour, minute, second] = time.split(':').map(Number);
  const wall = Date.UTC(year, month - 1, day, hour, minute, second);
  // The offset can change that day (summer time on the border): correct once with the offset at the result.
  const guess = wall - zoneOffsetMinutes(businessTimeZone, new Date(wall)) * 60_000;
  return new Date(wall - zoneOffsetMinutes(businessTimeZone, new Date(guess)) * 60_000);
}

/** When a business day starts, for "from" filters of the API (ISO). */
export function businessDayStart(date: string): string {
  return businessWallTimeToInstant(date, '00:00:00').toISOString();
}

/** When a business day ends, for "to" filters of the API (ISO). */
export function businessDayEnd(date: string): string {
  return businessWallTimeToInstant(date, '23:59:59').toISOString();
}
