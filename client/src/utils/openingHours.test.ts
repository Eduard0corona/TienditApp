import { describe, expect, it } from 'vitest';
import { DayOfWeek, SpecialDayKind } from '../types';
import {
  clock,
  dayOfWeekOf,
  describeDay,
  describeInterval,
  describeOpeningStatus,
  describeSpecialDay,
  describeTodayHours,
  endsAfterMidnight,
  firstDayMissingTime,
  fromDraft,
  openingsOn,
  rangeHint,
  sameHours,
  setDayOpen,
  specialDayDates,
  specialDayDatesInWords,
  specialDayRule,
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
    // The one o'clock hour is singular, as the API words it: "a la 1:30".
    expect(say({ isOpen: true, closesAt: '01:30', closesInDays: 1 })).toBe('Abierto · cierra mañana a la 1:30');
    expect(say({ isOpen: false, opensAt: '01:00', opensInDays: 1 })).toBe('Cerrado · abre mañana a la 1:00');
    expect(say({ isOpen: true, closesAt: '13:00', closesInDays: 0 })).toBe('Abierto · cierra a las 13:00');
    expect(say({ isOpen: false, opensAt: '10:00', opensInDays: 0 })).toBe('Cerrado · abre hoy a las 10:00');
  });

  it('explains the openings that are not plain', () => {
    expect(rangeHint({ opens: '09:00', closes: '20:00' })).toBeNull();
    expect(rangeHint({ opens: '18:00', closes: '02:00' })).toBe('Cierra al día siguiente');
    expect(rangeHint({ opens: '13:00', closes: '00:00' })).toBe('Cierra a medianoche');
    expect(rangeHint({ opens: '08:00', closes: '08:00' })).toBe('Abierto las 24 horas');
    expect(rangeHint({ opens: '09:00', closes: '' })).toBeNull();

    // Only what still runs after midnight reaches into the next date.
    expect(endsAfterMidnight({ opens: '18:00', closes: '02:00' })).toBe(true);
    expect(endsAfterMidnight({ opens: '08:00', closes: '08:00' })).toBe(true);
    expect(endsAfterMidnight({ opens: '13:00', closes: '00:00' })).toBe(false);
    expect(endsAfterMidnight({ opens: '00:00', closes: '00:00' })).toBe(false);
    expect(endsAfterMidnight({ opens: '09:00', closes: '20:00' })).toBe(false);
  });

  it('finds the weekday of a date and its weekly openings', () => {
    expect(dayOfWeekOf('2026-12-25')).toBe(DayOfWeek.Friday);
    expect(dayOfWeekOf('2027-01-03')).toBe(DayOfWeek.Sunday);
    expect(openingsOn(splitMonday, DayOfWeek.Monday)).toEqual([
      { opens: '09:00', closes: '14:00' },
      { opens: '16:00', closes: '20:00' },
    ]);
    expect(openingsOn(null, DayOfWeek.Monday)).toEqual([]);
  });

  it('says today on the menu, with the reason of a special day', () => {
    const today = { date: '2026-12-24', closed: false, special: false, openings: [{ opens: '09:00', closes: '20:00' }] };

    expect(describeTodayHours(today)).toBe('9:00 a 20:00');
    expect(describeTodayHours({ ...today, closed: true, openings: [] })).toBe('Cerrado');
    expect(describeTodayHours({ ...today, special: true, openings: [{ opens: '09:00', closes: '18:00' }], reason: 'Nochebuena' }))
      .toBe('9:00 a 18:00 · Nochebuena');
    expect(describeTodayHours({ ...today, special: true, openings: [{ opens: '09:00', closes: '18:00' }], reason: null }))
      .toBe('9:00 a 18:00 · horario especial');
    expect(describeTodayHours({ ...today, closed: true, special: true, openings: [], reason: 'Navidad' })).toBe('Cerrado · Navidad');
    expect(describeTodayHours({ ...today, closed: true, special: true, openings: [] })).toBe('Cerrado · día especial');
  });

  it('writes a special day as a row of a list and in a sentence', () => {
    const christmasEve = {
      from: '2026-12-24', to: '2026-12-24', kind: SpecialDayKind.SpecialHours, openings: [{ opens: '09:00', closes: '18:00' }], reason: 'Nochebuena',
    };
    const vacations = { from: '2026-12-21', to: '2027-01-04', kind: SpecialDayKind.Closed, openings: [], reason: 'Vacaciones' };

    expect(describeSpecialDay(christmasEve)).toBe('Jue 24 dic · 9:00 a 18:00 · Nochebuena');
    expect(describeSpecialDay({ ...christmasEve, from: '2026-12-25', to: '2026-12-25', kind: SpecialDayKind.Closed, openings: [], reason: 'Navidad' }))
      .toBe('Vie 25 dic · Cerrado · Navidad');
    expect(describeSpecialDay(vacations)).toBe('Lun 21 dic al lun 4 ene · Cerrado · Vacaciones');
    expect(describeSpecialDay({ ...vacations, reason: null })).toBe('Lun 21 dic al lun 4 ene · Cerrado');
    expect(specialDayRule({
      kind: SpecialDayKind.SpecialHours,
      openings: [{ opens: '19:00', closes: '22:00' }, { opens: '09:00', closes: '18:00' }],
    })).toBe('9:00 a 18:00 y 19:00 a 22:00');
    expect(specialDayDates('2026-12-25', '2026-12-25')).toBe('Vie 25 dic');
    expect(specialDayDatesInWords('2026-12-25', '2026-12-25')).toBe('viernes 25 de diciembre');
    expect(specialDayDatesInWords('2026-12-21', '2027-01-04')).toBe('del lunes 21 de diciembre al lunes 4 de enero');
  });
});
