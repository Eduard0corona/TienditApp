import { afterEach, describe, expect, it } from 'vitest';
import { setBusinessTimeZone } from './dates';
import { confirmInTimeLabel } from './orderConfirmInTime';

describe('confirmInTimeLabel (Fase 29X)', () => {
  // Monday 1 March 2027, 1:10 p.m. in Mexico City.
  const now = new Date('2027-03-01T19:10:00Z');
  afterEach(() => setBusinessTimeZone(null));

  it('says by when an order of the online menu is confirmed not to count among the slowest, in the business time zone', () => {
    setBusinessTimeZone('America/Mexico_City');

    expect(confirmInTimeLabel('2027-03-01T19:30:00Z', now))
      .toEqual({ text: 'Confírmalo antes de la 1:30 p.m. para que no cuente entre los más lentos', late: false });
    expect(confirmInTimeLabel('2027-03-01T20:05:00Z', now).text)
      .toBe('Confírmalo antes de las 2:05 p.m. para que no cuente entre los más lentos');
  });

  it('says when the half hour passed, at its very minute too', () => {
    setBusinessTimeZone('America/Mexico_City');

    const late = { text: 'Ya pasó la media hora: cuenta entre los más lentos en tu tiempo para confirmar', late: true };
    expect(confirmInTimeLabel('2027-03-01T19:10:00Z', now)).toEqual(late);
    expect(confirmInTimeLabel('2027-03-01T18:00:00Z', now)).toEqual(late);
  });
});
