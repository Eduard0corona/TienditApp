import type { RangeHints } from '../../utils/openingHours';

/** How a week of hours reads (WeeklyHoursFields): a business opens and closes; a person comes in and leaves. */
export interface WeeklyHoursWords {
  opens: string;
  closes: string;
  closed: string;
  /** The business's words when missing. */
  hints?: RangeHints;
}

export const BUSINESS_HOURS_WORDS: WeeklyHoursWords = { opens: 'Abre', closes: 'Cierra', closed: 'Cerrado' };
export const PERSON_HOURS_WORDS: WeeklyHoursWords = {
  opens: 'Entra',
  closes: 'Sale',
  closed: 'Descansa',
  hints: { allDay: 'Las 24 horas', untilMidnight: 'Sale a medianoche', nextDay: 'Sale al día siguiente' },
};
