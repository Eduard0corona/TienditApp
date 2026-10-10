import { describe, expect, it } from 'vitest';
import { DayOfWeek } from '../types';
import {
  clock,
  describeDay,
  describeInterval,
  describeOpeningStatus,
  firstDayMissingTime,
  fromDraft,
  sameHours,
  setDayOpen,
  toDraft,
  type PublicOpeningHours,
} from './openingHours';

const splitMonday = [
  { day: DayOfWeek.Monday, opens: '16:00', closes: '20:00' },
  { day: DayOfWeek.Monday, opens: '09:00', closes: '14:00' },
];

describe('opening hours', () => {
  it('reads times and openings as people say them', () => {
    expect(clock('09:00')).toBe('9:00');
    expect(clock('20:30')).toBe('20:30');
    expect(describeInterval({ opens: '09:00', closes: '20:00' })).toBe('9:00 a 20:00');
    expect(describeInterval({ opens: '18:00', closes: '02:00' })).toBe('18:00 a 2:00');
    expect(describeInterval({ opens: '13:00', closes: '00:00' })).toBe('13:00 a medianoche');
    expect(describeInterval({ opens: '00:00', closes: '00:00' })).toBe('Abierto las 24 horas');
    expect(describeDay(splitMonday, DayOfWeek.Monday)).toBe('9:00 a 14:00 y 16:00 a 20:00');
    expect(describeDay(splitMonday, DayOfWeek.Tuesday)).toBe('Cerrado');
  });

  it('turns saved hours into a week starting on Monday and back', () => {
    const draft = toDraft([...splitMonday, { day: DayOfWeek.Sunday, opens: '10:00', closes: '15:00' }]);

    expect(draft.map((day) => day.day)).toEqual([
      DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
      DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ]);
    expect(draft[0]).toEqual({
      day: DayOfWeek.Monday,
      open: true,
      ranges: [{ opens: '09:00', closes: '14:00' }, { opens: '16:00', closes: '20:00' }],
    });
    expect(draft[1]).toEqual({ day: DayOfWeek.Tuesday, open: false, ranges: [] });
    // As the API keeps them: by day (Sunday first) and opening time.
    expect(fromDraft(draft)).toEqual([
      { day: DayOfWeek.Sunday, opens: '10:00', closes: '15:00' },
      { day: DayOfWeek.Monday, opens: '09:00', closes: '14:00' },
      { day: DayOfWeek.Monday, opens: '16:00', closes: '20:00' },
    ]);
    expect(toDraft(null).every((day) => !day.open)).toBe(true);
  });

  it('opens a day with the hours of the open day before it, or 9:00 to 20:00', () => {
    let draft = setDayOpen(toDraft([]), DayOfWeek.Wednesday, true);
    expect(draft[2].ranges).toEqual([{ opens: '09:00', closes: '20:00' }]);

    draft = setDayOpen(toDraft(splitMonday), DayOfWeek.Thursday, true);
    expect(draft[3].ranges).toEqual([{ opens: '09:00', closes: '14:00' }, { opens: '16:00', closes: '20:00' }]);

    // A day closed again keeps its hours until it opens back; a closed day is not sent.
    draft = setDayOpen(draft, DayOfWeek.Monday, false);
    expect(fromDraft(draft).filter((interval) => interval.day === DayOfWeek.Monday)).toEqual([]);
    expect(setDayOpen(draft, DayOfWeek.Monday, true)[0].ranges).toHaveLength(2);
  });

  it('compares hours whatever their order and finds a day without its times', () => {
    expect(sameHours(splitMonday, [...splitMonday].reverse())).toBe(true);
    expect(sameHours(splitMonday, splitMonday.slice(1))).toBe(false);
    expect(sameHours(null, [])).toBe(true);

    const draft = toDraft(splitMonday);
    expect(firstDayMissingTime(draft)).toBeNull();
    draft[0].ranges[1] = { opens: '16:00', closes: '' };
    expect(firstDayMissingTime(draft)).toBe(DayOfWeek.Monday);
  });

  it('says whether the business is open now and until when', () => {
    const base = { intervals: [], today: DayOfWeek.Saturday };
    const say = (hours: Partial<PublicOpeningHours> & { isOpen: boolean }) => {
      const status = describeOpeningStatus({ ...base, ...hours });
      return status.detail ? `${status.label} · ${status.detail}` : status.label;
    };

    expect(say({ isOpen: true, closesAt: '20:00', closesInDays: 0 })).toBe('Abierto · cierra a las 20:00');
    expect(say({ isOpen: true, closesAt: '02:00', closesInDays: 1 })).toBe('Abierto · cierra mañana a las 2:00');
    expect(say({ isOpen: true, closesAt: '00:00', closesInDays: 1 })).toBe('Abierto · cierra a medianoche');
    expect(say({ isOpen: true, closesAt: '00:00', closesInDays: 2 })).toBe('Abierto · cierra mañana a medianoche');
    expect(say({ isOpen: true, closesAt: null })).toBe('Abierto las 24 horas');
    expect(say({ isOpen: false, opensAt: '16:00', opensInDays: 0 })).toBe('Cerrado · abre hoy a las 16:00');
    expect(say({ isOpen: false, opensAt: '09:00', opensInDays: 1 })).toBe('Cerrado · abre mañana a las 9:00');
    expect(say({ isOpen: false, opensAt: '09:00', opensInDays: 2 })).toBe('Cerrado · abre el lunes a las 9:00');
    expect(say({ isOpen: false })).toBe('Cerrado');
    expect(describeOpeningStatus({ ...base, isOpen: false, opensAt: '09:00', opensInDays: 1 })).toMatchObject({ isOpen: false, when: 'mañana a las 9:00' });
  });
});
