// The hours of a business (TenantSettings.OpeningHours): up to two openings a day, in its own time zone, and its special
// days (closed, or open with other hours). The API keeps them, decides whether it is open and, since the Fase 21, writes
// what the public menu says about it (PublicOpeningHoursDto); here they only become words and a form.
import type { Schemas } from '../api/typedClient';
import { DayOfWeek, SpecialDayKind, type OpeningIntervalDto } from '../types';
import { formatLongCalendarDay, formatShortCalendarDay } from './dates';

export interface TimeRange {
  opens: string;
  closes: string;
}

/** One day of the form: closing a day keeps its times, so opening it again brings them back. */
export interface DayDraft {
  day: DayOfWeek;
  open: boolean;
  ranges: TimeRange[];
}

/**
 * Whether the business is open now, as the public menu receives it. With `todayDate` the API also sends its words
 * (statusLabel, statusDetail, closedNotice…); an older API sends only the times, which describeOpeningStatus words.
 */
export type PublicOpeningHours = Schemas['PublicOpeningHoursDto'];

/** A special day as the API sends it, to the public menu or to its owner: one date, or consecutive dates with one rule. */
export interface SpecialDayText {
  from: string;
  to: string;
  kind: SpecialDayKind;
  openings: readonly TimeRange[];
  reason?: string | null;
}

export const MAX_RANGES_PER_DAY = 2;
export const DEFAULT_RANGE: TimeRange = { opens: '09:00', closes: '20:00' };

const DAY_NAMES: Record<DayOfWeek, string> = {
  [DayOfWeek.Sunday]: 'domingo',
  [DayOfWeek.Monday]: 'lunes',
  [DayOfWeek.Tuesday]: 'martes',
  [DayOfWeek.Wednesday]: 'miércoles',
  [DayOfWeek.Thursday]: 'jueves',
  [DayOfWeek.Friday]: 'viernes',
  [DayOfWeek.Saturday]: 'sábado',
};

/** The week as a business reads it, Monday first. */
export const WEEK: readonly DayOfWeek[] = [
  DayOfWeek.Monday,
  DayOfWeek.Tuesday,
  DayOfWeek.Wednesday,
  DayOfWeek.Thursday,
  DayOfWeek.Friday,
  DayOfWeek.Saturday,
  DayOfWeek.Sunday,
];

/** "lunes", "miércoles". */
export function dayName(day: DayOfWeek): string {
  return DAY_NAMES[day];
}

/** "Lunes", for a label that starts a line. */
export function dayLabel(day: DayOfWeek): string {
  const name = dayName(day);
  return name.charAt(0).toUpperCase() + name.slice(1);
}

/** "09:00" as it is read: "9:00". */
export function clock(time: string): string {
  return time.replace(/^0(\d)/, '$1');
}

/** "a las 9:00", or "a la 1:30" in the one o'clock hour, as the API words it. */
function atClock(time: string): string {
  return `${time.startsWith('01:') ? 'a la' : 'a las'} ${clock(time)}`;
}

/** "9:00 a 20:00", "13:00 a medianoche", or "Abierto las 24 horas" when it closes at the time it opens. */
export function describeInterval(range: TimeRange): string {
  if (range.opens === range.closes) return 'Abierto las 24 horas';
  return `${clock(range.opens)} a ${range.closes === '00:00' ? 'medianoche' : clock(range.closes)}`;
}

/** Openings of one day: "9:00 a 14:00 y 16:00 a 20:00", or "Cerrado" without any. */
export function describeOpenings(ranges: readonly TimeRange[]): string {
  return ranges.length === 0 ? 'Cerrado' : [...ranges].sort(byOpening).map(describeInterval).join(' y ');
}

/** The hours of one day: "9:00 a 14:00 y 16:00 a 20:00", or "Cerrado". */
export function describeDay(intervals: readonly OpeningIntervalDto[], day: DayOfWeek): string {
  return describeOpenings(intervals.filter((interval) => interval.day === day));
}

/** What an opening means when it is not plain: the API reads a closing at or before the opening as the next day. */
export function rangeHint(range: TimeRange, hints: RangeHints = OPENING_HINTS): string | null {
  if (!range.opens || !range.closes) return null;
  if (range.opens === range.closes) return hints.allDay;
  if (range.closes === '00:00') return hints.untilMidnight;
  return range.closes < range.opens ? hints.nextDay : null;
}

/** How a range that is not an ordinary day reads: a business's opening, unless a person's hours say otherwise. */
export interface RangeHints {
  allDay: string;
  untilMidnight: string;
  nextDay: string;
}

const OPENING_HINTS: RangeHints = { allDay: 'Abierto las 24 horas', untilMidnight: 'Cierra a medianoche', nextDay: 'Cierra al día siguiente' };

/** An opening that still runs after midnight (18:00 to 2:00); one that closes at 00:00 ends with its day. */
export function endsAfterMidnight(range: TimeRange): boolean {
  return range.closes !== '00:00' && range.closes <= range.opens;
}

/** The weekday of a calendar date ("2026-12-25" is a Friday). */
export function dayOfWeekOf(date: string): DayOfWeek {
  return new Date(`${date}T12:00:00Z`).getUTCDay() as DayOfWeek;
}

/** The weekly openings of a weekday, by opening time. */
export function openingsOn(intervals: readonly OpeningIntervalDto[] | null | undefined, day: DayOfWeek): TimeRange[] {
  return (intervals ?? []).filter((interval) => interval.day === day).map(({ opens, closes }) => ({ opens, closes })).sort(byOpening);
}

function byOpening(a: TimeRange, b: TimeRange): number {
  return a.opens.localeCompare(b.opens);
}

/** As the API keeps them: by day (Sunday first) and opening time. */
function sorted(intervals: readonly OpeningIntervalDto[]): OpeningIntervalDto[] {
  return [...intervals].sort((a, b) => a.day - b.day || byOpening(a, b));
}

export function toDraft(intervals?: readonly OpeningIntervalDto[] | null): DayDraft[] {
  return WEEK.map((day) => {
    const ranges = openingsOn(intervals, day);
    return { day, open: ranges.length > 0, ranges };
  });
}

/** What the form sends: the openings of the open days. */
export function fromDraft(draft: readonly DayDraft[]): OpeningIntervalDto[] {
  return sorted(draft.flatMap((day) => (day.open ? day.ranges.map((range) => ({ day: day.day, ...range })) : [])));
}

export function sameHours(a?: readonly OpeningIntervalDto[] | null, b?: readonly OpeningIntervalDto[] | null): boolean {
  return JSON.stringify(sorted(a ?? [])) === JSON.stringify(sorted(b ?? []));
}

/**
 * Opens or closes a day. A day opened for the first time takes the hours of the closest open day before it (most
 * days repeat them), or 9:00 to 20:00.
 */
export function setDayOpen(draft: readonly DayDraft[], day: DayOfWeek, open: boolean): DayDraft[] {
  const index = draft.findIndex((item) => item.day === day);
  return draft.map((item, position) => {
    if (position !== index) return item;
    if (!open || item.ranges.length > 0) return { ...item, open };
    const before = [...draft.slice(0, index)].reverse().find((other) => other.open && other.ranges.length > 0);
    return { ...item, open, ranges: before ? before.ranges.map((range) => ({ ...range })) : [{ ...DEFAULT_RANGE }] };
  });
}

/** The first open day with a time left blank, to ask for it before saving. */
export function firstDayMissingTime(draft: readonly DayDraft[]): DayOfWeek | null {
  return draft.find((day) => day.open && day.ranges.some((range) => !range.opens || !range.closes))?.day ?? null;
}

function onDay(today: DayOfWeek, inDays: number): string {
  if (inDays <= 0) return 'hoy';
  if (inDays === 1) return 'mañana';
  return `el ${dayName(((today + inDays) % 7) as DayOfWeek)}`;
}

/**
 * "Abierto" and "cierra a las 20:00", or "Cerrado" and "abre mañana a las 9:00" (when: "mañana a las 9:00"; "mañana a la
 * 1:00" in the one o'clock hour).
 */
export interface OpeningStatusText {
  isOpen: boolean;
  label: string;
  detail: string | null;
  when: string | null;
}

/** Whether the business is open now, in words; the API worked it out in the business's time. */
export function describeOpeningStatus(hours: PublicOpeningHours): OpeningStatusText {
  if (hours.isOpen) {
    if (!hours.closesAt) return { isOpen: true, label: 'Abierto las 24 horas', detail: null, when: null };
    const inDays = hours.closesInDays ?? 0;
    // Closing at 00:00 ends the day before: "cierra a medianoche", not "mañana a las 0:00".
    const when = hours.closesAt === '00:00'
      ? `${inDays <= 1 ? '' : `${onDay(hours.today, inDays - 1)} `}a medianoche`
      : `${inDays <= 0 ? '' : `${onDay(hours.today, inDays)} `}${atClock(hours.closesAt)}`;
    return { isOpen: true, label: 'Abierto', detail: `cierra ${when}`, when };
  }
  if (!hours.opensAt) return { isOpen: false, label: 'Cerrado', detail: null, when: null };
  const when = `${onDay(hours.today, hours.opensInDays ?? 0)} ${atClock(hours.opensAt)}`;
  return { isOpen: false, label: 'Cerrado', detail: `abre ${when}`, when };
}

/**
 * Today on the public menu, after "Hoy:": "9:00 a 20:00", "Cerrado" and, on a special day, why: "Cerrado · Navidad",
 * "9:00 a 18:00 · Nochebuena", or "9:00 a 18:00 · horario especial" without a reason.
 */
export function describeTodayHours(today: Schemas['PublicDayHoursDto']): string {
  const hours = today.closed ? 'Cerrado' : describeOpenings(today.openings);
  if (!today.special) return hours;
  return `${hours} · ${today.reason || (today.closed ? 'día especial' : 'horario especial')}`;
}

/** The dates of a special day in a list: "Vie 25 dic", or "Lun 21 dic al lun 4 ene" for several. */
export function specialDayDates(from: string, to: string): string {
  if (from === to) return formatShortCalendarDay(from);
  const last = formatShortCalendarDay(to);
  return `${formatShortCalendarDay(from)} al ${last.charAt(0).toLowerCase()}${last.slice(1)}`;
}

/** The dates of a special day in a sentence: "viernes 25 de diciembre", "del lunes 21 de diciembre al lunes 4 de enero". */
export function specialDayDatesInWords(from: string, to: string): string {
  return from === to ? formatLongCalendarDay(from) : `del ${formatLongCalendarDay(from)} al ${formatLongCalendarDay(to)}`;
}

/** What a special day does: "Cerrado", or its hours ("9:00 a 18:00 y 19:00 a 22:00"). */
export function specialDayRule(day: Pick<SpecialDayText, 'kind' | 'openings'>): string {
  return day.kind === SpecialDayKind.Closed ? 'Cerrado' : describeOpenings(day.openings);
}

/** A special day in one line: "Jue 24 dic · 9:00 a 18:00 · Nochebuena", "Lun 21 dic al lun 4 ene · Cerrado · Vacaciones". */
export function describeSpecialDay(day: SpecialDayText): string {
  return [specialDayDates(day.from, day.to), specialDayRule(day), day.reason].filter(Boolean).join(' · ');
}
