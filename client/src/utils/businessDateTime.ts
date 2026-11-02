import { businessWallTimeToInstant } from './dates';

// Apart from dates.ts, which the first visit loads: only the screens that send a typed date and time load this.

/**
 * A time typed in a datetime-local field («2026-10-08T10:30»), read in the business's time zone (not the device's), as an
 * instant for the API (ISO).
 */
export function businessDateTimeToIso(value: string): string {
  const [date, time = '00:00'] = value.split('T');
  return businessWallTimeToInstant(date, time.length === 5 ? `${time}:00` : time).toISOString();
}
