import { describe, expect, it } from 'vitest';
import { describePeriod, previousWeek } from './reportPeriods';

describe('reportPeriods', () => {
  it('takes Monday to Sunday of the week before, whatever the day of this one', () => {
    // Monday the 12th, Wednesday the 14th and Sunday the 18th of October 2026.
    expect(previousWeek('2026-10-12')).toEqual({ from: '2026-10-05', to: '2026-10-11' });
    expect(previousWeek('2026-10-14')).toEqual({ from: '2026-10-05', to: '2026-10-11' });
    expect(previousWeek('2026-10-18')).toEqual({ from: '2026-10-05', to: '2026-10-11' });
    // Across a year: Monday the 4th of January 2027.
    expect(previousWeek('2027-01-04')).toEqual({ from: '2026-12-28', to: '2027-01-03' });
  });

  it('names a period briefly, with the months and years only where they change', () => {
    expect(describePeriod('2026-10-05', '2026-10-11')).toBe('5 al 11 oct.');
    // The browser's data abbreviates September «sep.» or «sept.».
    expect(describePeriod('2026-09-28', '2026-10-04')).toMatch(/^28 sept?\. al 4 oct\.$/);
    expect(describePeriod('2026-12-28', '2027-01-03')).toMatch(/^28 dic\. 2026 al 3 ene\. 2027$/);
    expect(describePeriod('2026-10-12', '2026-10-12')).toBe('12 oct.');
  });
});
