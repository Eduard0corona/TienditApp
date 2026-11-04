import { describe, expect, it } from 'vitest';
import { ResponseTime } from '../api/generated/enums';
import { responseTimeLabel } from './responseTime';

describe('responseTimeLabel (Fase 29J)', () => {
  it('says within what the business answers, up to a day', () => {
    expect(responseTimeLabel(ResponseTime.WithinAnHour)).toBe('Suele responder en menos de una hora');
    expect(responseTimeLabel(ResponseTime.WithinThreeHours)).toBe('Suele responder en menos de 3 horas');
    expect(responseTimeLabel(ResponseTime.WithinTwelveHours)).toBe('Suele responder en menos de 12 horas');
    expect(responseTimeLabel(ResponseTime.WithinADay)).toBe('Suele responder en menos de un día');
  });

  it('says nothing past a day or without enough requests', () => {
    expect(responseTimeLabel(ResponseTime.Slower)).toBeNull();
    expect(responseTimeLabel(null)).toBeNull();
    expect(responseTimeLabel(undefined)).toBeNull();
  });

  it('has a label for every time the server sends but the slowest', () => {
    const shown = Object.values(ResponseTime).filter((value) => value !== ResponseTime.Slower);
    expect(shown.map(responseTimeLabel).every((label) => label?.startsWith('Suele responder en menos de'))).toBe(true);
  });
});
