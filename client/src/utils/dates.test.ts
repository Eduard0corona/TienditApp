import { afterEach, describe, expect, it } from 'vitest';
import {
  BUSINESS_TIME_ZONES,
  businessDayEnd,
  businessDayStart,
  formatCalendarDate,
  formatTime,
  getBusinessTimeZone,
  getBusinessToday,
  setBusinessTimeZone,
} from './dates';

// Intl may separate "p. m." with narrow spaces; compare without them.
const plain = (text: string) => text.replace(/[\u00a0\u202f]/g, ' ');

describe('dates in the business time zone', () => {
  afterEach(() => setBusinessTimeZone(null));

  it('shows the hour of the business, not the device', () => {
    // 19:25 UTC is 1:25 p. m. in Mexico City and 12:25 p. m. in Tijuana (summer time).
    setBusinessTimeZone('America/Mexico_City');
    expect(plain(formatTime('2026-10-04T19:25:00Z'))).toMatch(/^1:25 p\.\s?m\.$/);

    setBusinessTimeZone('America/Tijuana');
    expect(plain(formatTime('2026-10-04T19:25:00Z'))).toMatch(/^12:25 p\.\s?m\.$/);
  });

  it('knows which day it is for the business around midnight', () => {
    // 05:30 UTC on the 5th is still the 4th in Mexico City (UTC-6) and already the 5th in Cancún (UTC-5).
    const instant = new Date('2026-10-05T05:30:00Z');

    setBusinessTimeZone('America/Mexico_City');
    expect(getBusinessToday(instant)).toBe('2026-10-04');
    setBusinessTimeZone('America/Cancun');
    expect(getBusinessToday(instant)).toBe('2026-10-05');
  });

  it('starts and ends the business day at its midnight', () => {
    setBusinessTimeZone('America/Mexico_City');
    expect(businessDayStart('2026-10-05')).toBe('2026-10-05T06:00:00.000Z');
    expect(businessDayEnd('2026-10-05')).toBe('2026-10-06T05:59:59.000Z');

    // Baja California follows summer time: the day starts at 07:00 UTC in October and at 08:00 UTC in December.
    setBusinessTimeZone('America/Tijuana');
    expect(businessDayStart('2026-10-05')).toBe('2026-10-05T07:00:00.000Z');
    expect(businessDayStart('2026-12-05')).toBe('2026-12-05T08:00:00.000Z');
  });

  it('shows a calendar date as words without shifting it', () => {
    setBusinessTimeZone('America/Tijuana');
    expect(formatCalendarDate('2026-10-05')).toBe('5 de octubre de 2026');
  });

  it('ignores zones it does not know', () => {
    setBusinessTimeZone('Mars/Olympus_Mons');
    expect(getBusinessTimeZone()).toBeUndefined();
  });

  it('offers zones every browser knows', () => {
    for (const zone of BUSINESS_TIME_ZONES) {
      setBusinessTimeZone(zone.id);
      expect(getBusinessTimeZone()).toBe(zone.id);
    }
  });
});
