import { describe, expect, it } from 'vitest';
import { OnlinePaymentDispute, OnlinePaymentRefundReason, OnlinePaymentRefundStatus } from '../api/generated/enums';
import { describeOnlineDispute, describeOnlineRefund } from './onlineRefunds';

describe('describeOnlineRefund', () => {
  it('says whether Mercado Pago gave the money back, is giving it back or refused', () => {
    expect(describeOnlineRefund(OnlinePaymentRefundStatus.Requested)).toEqual({ text: 'Mercado Pago lo está devolviendo.', tone: 'muted' });
    expect(describeOnlineRefund(null)).toEqual({ text: 'Mercado Pago lo está devolviendo.', tone: 'muted' });
    expect(describeOnlineRefund(OnlinePaymentRefundStatus.Completed)).toEqual({ text: 'Mercado Pago ya lo devolvió.', tone: 'success' });
  });

  it('tells the business to give back a refused one from its account, with Mercado Pago\'s reason', () => {
    expect(describeOnlineRefund(OnlinePaymentRefundStatus.Failed, 'El pago ya no se puede devolver.')).toEqual({
      text: 'El pago ya no se puede devolver. Devuélvelo desde tu cuenta de Mercado Pago.',
      tone: 'danger',
    });
    expect(describeOnlineRefund(OnlinePaymentRefundStatus.Failed, '  ').text)
      .toBe('Mercado Pago no lo devolvió. Devuélvelo desde tu cuenta de Mercado Pago.');
  });

  it('says the business gave back a refused one from its account once TienditApp saw it in Mercado Pago (Fase 24K)', () => {
    expect(describeOnlineRefund(OnlinePaymentRefundStatus.DoneInMercadoPago, 'El pago ya no se puede devolver.', OnlinePaymentRefundReason.ByBusiness))
      .toEqual({ text: 'Mercado Pago no lo devolvió; lo devolviste desde tu cuenta de Mercado Pago.', tone: 'success' });
  });

  it('says where one made in Mercado Pago came from: the account, a claim or the customer\'s bank (Fase 24K)', () => {
    const completed = OnlinePaymentRefundStatus.Completed;
    expect(describeOnlineRefund(completed, null, OnlinePaymentRefundReason.InMercadoPago)).toEqual({ text: 'Se hizo en Mercado Pago.', tone: 'success' });
    expect(describeOnlineRefund(completed, null, OnlinePaymentRefundReason.Claim))
      .toEqual({ text: 'Mercado Pago lo devolvió por una reclamación.', tone: 'warning' });
    expect(describeOnlineRefund(completed, null, OnlinePaymentRefundReason.Chargeback))
      .toEqual({ text: 'El banco del cliente lo regresó (contracargo).', tone: 'danger' });
    expect(describeOnlineRefund(completed, null, OnlinePaymentRefundReason.ByBusiness)).toEqual({ text: 'Mercado Pago ya lo devolvió.', tone: 'success' });
  });
});

describe('describeOnlineDispute', () => {
  it('warns while Mercado Pago holds the money, and says how it ended (Fase 24K)', () => {
    expect(describeOnlineDispute(OnlinePaymentDispute.Open)).toMatchObject({ title: 'Pago en disputa', variant: 'warning' });
    expect(describeOnlineDispute(OnlinePaymentDispute.Open)?.text).toMatch(/Mercado Pago retiene el dinero/);
    expect(describeOnlineDispute(OnlinePaymentDispute.Lost)).toMatchObject({ title: 'Disputa perdida', variant: 'danger' });
    expect(describeOnlineDispute(OnlinePaymentDispute.Won)).toMatchObject({ title: 'Disputa ganada', variant: 'success' });
  });

  it('says nothing for an order with no dispute', () => {
    expect(describeOnlineDispute(OnlinePaymentDispute.None)).toBeNull();
    expect(describeOnlineDispute(null)).toBeNull();
    expect(describeOnlineDispute(undefined)).toBeNull();
  });
});
