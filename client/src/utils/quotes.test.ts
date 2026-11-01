import { describe, expect, it } from 'vitest';
import { QuoteReplyChannel, QuoteStatus } from '../types';
import { quoteFolio, quoteLink, quoteMailUrl, quoteMessage, quoteStatusChip, quoteStatusLabel, quoteWhatsAppUrl, replyChannelPhrase } from './quotes';

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

  it('opens WhatsApp only with a phone, and the email only with an address', () => {
    expect(quoteWhatsAppUrl('55 2210 7765', 'Hola')).toBe('https://wa.me/525522107765?text=Hola');
    expect(quoteWhatsAppUrl('123', 'Hola')).toBeNull();
    expect(quoteMailUrl(null, 'Cotización', 'Hola')).toBeNull();
    expect(quoteMailUrl('valeria@correo.mx', 'Cotización', 'Hola')).toBe('mailto:valeria%40correo.mx?subject=Cotizaci%C3%B3n&body=Hola');
  });
});
