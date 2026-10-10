// The hours of a business (TenantSettings.OpeningHours): up to two openings a day, in its own time zone. The API
// keeps them and decides whether it is open (PublicOpeningHoursDto); here they only become words and a form.
import { DayOfWeek, type OpeningIntervalDto } from '../types';

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

/** Whether the business is open now, as the public menu receives it. */
export interface PublicOpeningHours {
  intervals: OpeningIntervalDto[];
  today: DayOfWeek;
  isOpen: boolean;
  closesAt?: string | null;
  closesInDays?: number | null;
  opensAt?: string | null;
  opensInDays?: number | null;
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

/** "9:00 a 20:00", "13:00 a medianoche", or "Abierto las 24 horas" when it closes at the time it opens. */
export function describeInterval(range: TimeRange): string {
  if (range.opens === range.closes) return 'Abierto las 24 horas';
  return `${clock(range.opens)} a ${range.closes === '00:00' ? 'medianoche' : clock(range.closes)}`;
}

/** The hours of one day: "9:00 a 14:00 y 16:00 a 20:00", or "Cerrado". */
export function describeDay(intervals: readonly OpeningIntervalDto[], day: DayOfWeek): string {
  const ranges = intervals.filter((interval) => interval.day === day).sort(byOpening);
  return ranges.length === 0 ? 'Cerrado' : ranges.map(describeInterval).join(' y ');
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
    const ranges = (intervals ?? [])
      .filter((interval) => interval.day === day)
      .map(({ opens, closes }) => ({ opens, closes }))
      .sort(byOpening);
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

/** "Abierto" and "cierra a las 20:00", or "Cerrado" and "abre mañana a las 9:00" (when: "mañana a las 9:00"). */
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
      : `${inDays <= 0 ? '' : `${onDay(hours.today, inDays)} `}a las ${clock(hours.closesAt)}`;
    return { isOpen: true, label: 'Abierto', detail: `cierra ${when}`, when };
  }
  if (!hours.opensAt) return { isOpen: false, label: 'Cerrado', detail: null, when: null };
  const when = `${onDay(hours.today, hours.opensInDays ?? 0)} a las ${clock(hours.opensAt)}`;
  return { isOpen: false, label: 'Cerrado', detail: `abre ${when}`, when };
}
