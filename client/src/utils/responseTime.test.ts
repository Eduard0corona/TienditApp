import { afterEach, describe, expect, it } from 'vitest';
import { OrderConfirmTime, QuoteTime, ResponseTime } from '../api/generated/enums';
import { setBusinessTimeZone } from './dates';
import { ownOrderConfirmTimeNote, ownQuoteTimeNote, ownResponseTimeNote } from './ownResponseTime';
import { answerInTimeLabel, orderConfirmTimeLabel, quoteTimeLabel, responseTimeLabel } from './responseTime';

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
      'Lo calculamos con tus solicitudes en línea de los últimos 90 días, la última de cada cliente y sin las de tu equipo. Tus clientes lo leen en tu tarjeta, en tu tienda y donde te piden citas o visitas.',
    );
    expect(ownResponseTimeNote(ResponseTime.Slower)).toBe(
      'Sueles tardar un día o más en responder tus solicitudes en línea, así que tus clientes no lo leen.',
    );
    expect(ownResponseTimeNote(undefined)).toBe(
      'Todavía no contamos solicitudes en línea de 3 clientes para saber en cuánto sueles responder: una cuenta cuando la respondes, o cuando llega su hora o pasa un día sin respuesta.',
    );
    expect(ownResponseTimeNote(null)).toBe(ownResponseTimeNote(undefined));
  });
});

describe('answerInTimeLabel (Fase 29L)', () => {
  // Monday 1 March 2027, 11:00 in Mexico City.
  const now = new Date('2027-03-01T17:00:00Z');
  afterEach(() => setBusinessTimeZone(null));

  it('says by when the business answers to be in time, today or tomorrow in its own time zone', () => {
    setBusinessTimeZone('America/Mexico_City');

    expect(answerInTimeLabel('2027-03-01T21:30:00Z', now)).toBe('Responde antes de las 3:30 p.m. para que cuente a tiempo');
    // Already the 2nd in UTC, still the 1st in Mexico City.
    expect(answerInTimeLabel('2027-03-02T03:00:00Z', now)).toBe('Responde antes de las 9:00 p.m. para que cuente a tiempo');
    expect(answerInTimeLabel('2027-03-02T16:00:00Z', now)).toBe('Responde mañana antes de las 10:00 a.m. para que cuente a tiempo');
  });

  it('says «la» in the one o\'clock hour', () => {
    setBusinessTimeZone('America/Mexico_City');

    expect(answerInTimeLabel('2027-03-01T19:30:00Z', now)).toBe('Responde antes de la 1:30 p.m. para que cuente a tiempo');
    expect(answerInTimeLabel('2027-03-02T07:00:00Z', now)).toBe('Responde mañana antes de la 1:00 a.m. para que cuente a tiempo');
  });

  it('names the day when the device clock is off by more than a day', () => {
    setBusinessTimeZone('America/Mexico_City');

    expect(answerInTimeLabel('2027-03-04T16:00:00Z', now)).toBe('Responde antes del jueves 4 a las 10:00 a.m. para que cuente a tiempo');
    expect(answerInTimeLabel('2027-03-04T19:00:00Z', now)).toBe('Responde antes del jueves 4 a la 1:00 p.m. para que cuente a tiempo');
  });

  it('says when that limit already passed', () => {
    setBusinessTimeZone('America/Mexico_City');

    expect(answerInTimeLabel('2027-03-01T17:00:00Z', now)).toBe('Ya pasó el límite para responder a tiempo');
  });
});

describe('quoteTimeLabel and ownQuoteTimeNote (Fase 29S)', () => {
  it('says within what the trade sends the quote after the visit, up to a week', () => {
    expect(quoteTimeLabel(QuoteTime.WithinADay)).toBe('Suele mandar la cotización en menos de un día');
    expect(quoteTimeLabel(QuoteTime.WithinThreeDays)).toBe('Suele mandar la cotización en menos de 3 días');
    expect(quoteTimeLabel(QuoteTime.WithinAWeek)).toBe('Suele mandar la cotización en menos de una semana');
    expect(quoteTimeLabel(QuoteTime.Slower)).toBeNull();
    expect(quoteTimeLabel(null)).toBeNull();
    expect(quoteTimeLabel(undefined)).toBeNull();
  });

  it('tells its owner where customers read it, that a week or more is not said, or that it is not counted yet', () => {
    expect(ownQuoteTimeNote(QuoteTime.WithinADay)).toMatch(/Tus clientes lo leen en tu tarjeta y donde te piden la visita\.$/);
    expect(ownQuoteTimeNote(QuoteTime.Slower)).toMatch(/^Sueles tardar una semana o más/);
    expect(ownQuoteTimeNote(null)).toMatch(/^Todavía no contamos visitas en línea de 3 clientes/);
  });
});

describe('orderConfirmTimeLabel and ownOrderConfirmTimeNote (Fase 29T)', () => {
  it('says within what the business confirms its online orders, up to half an hour', () => {
    expect(orderConfirmTimeLabel(OrderConfirmTime.WithinFiveMinutes)).toBe('Suele confirmar los pedidos en menos de 5 minutos');
    expect(orderConfirmTimeLabel(OrderConfirmTime.WithinFifteenMinutes)).toBe('Suele confirmar los pedidos en menos de 15 minutos');
    expect(orderConfirmTimeLabel(OrderConfirmTime.WithinHalfAnHour)).toBe('Suele confirmar los pedidos en menos de media hora');
    expect(orderConfirmTimeLabel(OrderConfirmTime.Slower)).toBeNull();
    expect(orderConfirmTimeLabel(null)).toBeNull();
    expect(orderConfirmTimeLabel(undefined)).toBeNull();
  });

  it('tells its owner where customers read it, that half an hour or more is not said, or that it is not counted yet', () => {
    expect(ownOrderConfirmTimeNote(OrderConfirmTime.WithinFiveMinutes)).toMatch(/Tus clientes lo leen en tu tarjeta y en tu menú\.$/);
    expect(ownOrderConfirmTimeNote(OrderConfirmTime.Slower)).toMatch(/^Sueles tardar media hora o más/);
    expect(ownOrderConfirmTimeNote(null)).toMatch(/^Todavía no contamos pedidos en línea de 3 clientes/);
  });
});
