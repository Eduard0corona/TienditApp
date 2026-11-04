import { describe, expect, it } from 'vitest';
import { ResponseTime } from '../api/generated/enums';
import { ownResponseTimeNote, responseTimeLabel } from './responseTime';

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

describe('ownResponseTimeNote (Fase 29K)', () => {
  it('tells its owner where customers read it, or why nobody reads it yet', () => {
    expect(ownResponseTimeNote(ResponseTime.WithinThreeHours)).toBe(
      'Lo calculamos con tus solicitudes en línea de los últimos 90 días. Tus clientes lo leen en tu tarjeta, en tu tienda y donde te piden citas o visitas.',
    );
    expect(ownResponseTimeNote(ResponseTime.Slower)).toBe(
      'Sueles tardar un día o más en responder tus solicitudes en línea, así que tus clientes no lo leen.',
    );
    expect(ownResponseTimeNote(undefined)).toBe(
      'Todavía no contamos 3 solicitudes en línea para saber en cuánto sueles responder: una cuenta cuando la respondes, o cuando llega su hora o pasa un día sin respuesta.',
    );
    expect(ownResponseTimeNote(null)).toBe(ownResponseTimeNote(undefined));
  });
});
