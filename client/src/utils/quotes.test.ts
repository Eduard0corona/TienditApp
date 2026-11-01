import { describe, expect, it } from 'vitest';
import { OutgoingMessageStatus, QuoteEmailFailure, QuoteReplyChannel, QuoteStatus } from '../types';
import {
  hasPendingSends, quoteFolio, quoteLink, quoteMessage, quoteSends, quoteStatusChip, quoteStatusLabel, quoteWhatsAppUrl,
  replyChannelPhrase,
} from './quotes';

describe('quotes', () => {
  it('names every status the server sends, an expired one as such', () => {
    expect(quoteStatusLabel(QuoteStatus.Expired)).toBe('Vencida');
    expect(quoteStatusLabel(QuoteStatus.Superseded)).toBe('Reemplazada');
    expect(quoteStatusChip(QuoteStatus.Accepted)).toBe('ta-chip ta-chip-ready');
  });

  it('says the version after the first', () => {
    expect(quoteFolio('COT-000007', 1)).toBe('COT-000007');
    expect(quoteFolio('COT-000007', 2)).toBe('COT-000007 · versión 2');
  });

  it('puts the link in the fragment', () => {
    expect(quoteLink('abc', 'https://tiendit.app')).toBe('https://tiendit.app/cotizacion#abc');
  });

  it('writes the folio, what is left to pay, the validity and the link', () => {
    const text = quoteMessage({ number: 'COT-000007', revision: 2, toPay: 1160, validUntil: '2026-10-19' }, 'Plomería Ruiz', 'https://x/cotizacion#t');

    expect(text).toContain('COT-000007 · versión 2 de Plomería Ruiz');
    expect(text).toContain('$1,160.00');
    expect(text).toContain('19 de octubre de 2026');
    expect(text).toContain('https://x/cotizacion#t');
  });

  it('leaves out the business when its name is not known yet', () => {
    expect(quoteMessage({ number: 'COT-000007', revision: 1, toPay: 10 }, '', 'L')).toBe('Hola, te comparto la cotización COT-000007 por $10.00. Puedes verla aquí: L');
  });

  it('says how the customer answered after the verb', () => {
    expect(replyChannelPhrase(QuoteReplyChannel.WhatsApp)).toBe('por WhatsApp');
    expect(replyChannelPhrase(QuoteReplyChannel.InPerson)).toBe('en persona');
  });

  it('opens WhatsApp only with a phone', () => {
    expect(quoteWhatsAppUrl('55 2210 7765', 'Hola')).toBe('https://wa.me/525522107765?text=Hola');
    expect(quoteWhatsAppUrl('123', 'Hola')).toBeNull();
  });

  it('says where each send of a version is, the newest first, and whether one is still on its way', () => {
    const quote = {
      emails: [
        { id: 'e1', to: 'a@correo.mx', requestedAt: '2026-10-04T17:00:00Z', sentAt: '2026-10-04T17:00:10Z', failedAttempts: 0 },
        { id: 'e2', to: 'b@correo.mx', requestedAt: '2026-10-04T16:00:00Z', failedAt: '2026-10-04T20:00:00Z', failure: QuoteEmailFailure.NotDelivered, failedAttempts: 8 },
      ],
      whatsApps: [
        { id: 'w1', phone: '+525522107765', requestedAt: '2026-10-04T18:00:00Z', status: OutgoingMessageStatus.Sent, deliveredAt: '2026-10-04T18:00:30Z', attempts: 0 },
        { id: 'w2', phone: '+525511112222', requestedAt: '2026-10-04T15:00:00Z', status: OutgoingMessageStatus.Failed, error: 'El número no tiene WhatsApp.', attempts: 6 },
      ],
    };

    const sends = quoteSends(quote);

    expect(sends.map((line) => [line.channel, line.state])).toEqual([
      ['whatsapp', 'sent'], ['email', 'sent'], ['email', 'failed'], ['whatsapp', 'failed'],
    ]);
    expect(sends[0].text).toMatch(/^Le llegó por WhatsApp a \+525522107765 el /);
    expect(sends[2].text).toBe('No se envió por correo a b@correo.mx: el servidor de correo no lo tomó después de varios intentos.');
    expect(sends[3].text).toBe('No se envió por WhatsApp a +525511112222: El número no tiene WhatsApp.');
    expect(hasPendingSends(quote)).toBe(false);
    expect(hasPendingSends({ whatsApps: [{ id: 'w3', phone: '+52', requestedAt: '2026-10-04T19:00:00Z', status: OutgoingMessageStatus.Pending, attempts: 0 }] })).toBe(true);
    expect(quoteSends({})).toEqual([]);
  });

  it('keeps asking a few minutes after a WhatsApp went out, until WhatsApp says whether it arrived', () => {
    const sentAt = '2026-10-04T18:00:00Z';
    const quote = { whatsApps: [{ id: 'w1', phone: '+52', requestedAt: sentAt, status: OutgoingMessageStatus.Sent, sentAt, attempts: 0 }] };
    const at = (minutes: number) => Date.parse(sentAt) + minutes * 60_000;

    expect(hasPendingSends(quote, at(2))).toBe(true);
    expect(hasPendingSends(quote, at(6))).toBe(false);
    expect(hasPendingSends({ whatsApps: [{ ...quote.whatsApps[0], deliveredAt: '2026-10-04T18:00:05Z' }] }, at(2))).toBe(false);
  });

  it('stops the history at the oldest send of a channel that came full, so nothing hidden sits between', () => {
    const email = (i: number) => ({ id: `e${i}`, to: 'a@correo.mx', requestedAt: `2026-10-0${i + 2}T17:00:00Z`, sentAt: `2026-10-0${i + 2}T17:00:10Z`, failedAttempts: 0 });
    const quote = {
      // The latest five emails, from October 3 to 7; one WhatsApp on October 1, older than all of them.
      emails: [5, 4, 3, 2, 1].map(email),
      whatsApps: [{ id: 'w1', phone: '+52', requestedAt: '2026-10-01T17:00:00Z', status: OutgoingMessageStatus.Sent, sentAt: '2026-10-01T17:00:03Z', attempts: 0 }],
    };

    expect(quoteSends(quote).map((line) => line.key)).toEqual(['email-e5', 'email-e4', 'email-e3', 'email-e2', 'email-e1']);
  });
});
