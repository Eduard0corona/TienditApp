import { describe, expect, it } from 'vitest';
import { OnlinePaymentDispute, OnlinePaymentRefundReason, OnlinePaymentRefundStatus } from '../api/generated/enums';
import { describeOnlineDispute, describeOnlineRefund, isDisputeRefund } from './onlineRefunds';

describe('describeOnlineRefund', () => {
  it('says whether Mercado Pago gave the money back, is giving it back or refused', () => {
    expect(describeOnlineRefund(OnlinePaymentRefundStatus.Requested)).toEqual({ text: 'Mercado Pago lo está devolviendo.', tone: 'muted' });
    expect(describeOnlineRefund(OnlinePaymentRefundStatus.Completed)).toEqual({ text: 'Mercado Pago ya lo devolvió.', tone: 'success' });
  });

  it('tells the business to give back a refused one from its account, with Mercado Pago\'s reason', () => {
    expect(describeOnlineRefund(OnlinePaymentRefundStatus.Failed, 'El pago ya no se puede devolver.')).toEqual({
      text: 'El pago ya no se puede devolver. Devuélvelo desde tu cuenta de Mercado Pago.',
      tone: 'danger',
    });
    expect(describeOnlineRefund(OnlinePaymentRefundStatus.Failed, '  ')?.text)
      .toBe('Mercado Pago no lo devolvió. Devuélvelo desde tu cuenta de Mercado Pago.');
  });

  it('says nothing when what became of it is not known (the order as the list gives it)', () => {
    expect(describeOnlineRefund(null)).toBeNull();
    expect(describeOnlineRefund(undefined, null, null)).toBeNull();
  });

  it('says a refused one was given back later in Mercado Pago once TienditApp saw it there (Fase 24K)', () => {
    expect(describeOnlineRefund(OnlinePaymentRefundStatus.DoneInMercadoPago, 'El pago ya no se puede devolver.', OnlinePaymentRefundReason.ByBusiness))
      .toEqual({ text: 'Mercado Pago no lo devolvió al pedirlo; después se devolvió en tu cuenta de Mercado Pago.', tone: 'success' });
  });

  it('says where one made in Mercado Pago came from: the account, a claim or the customer\'s bank (Fase 24K)', () => {
    const completed = OnlinePaymentRefundStatus.Completed;
    expect(describeOnlineRefund(completed, null, OnlinePaymentRefundReason.InMercadoPago)).toEqual({ text: 'Se hizo en Mercado Pago.', tone: 'success' });
    expect(describeOnlineRefund(completed, null, OnlinePaymentRefundReason.Claim))
      .toEqual({ text: 'Mercado Pago lo devolvió por una reclamación.', tone: 'warning' });
    expect(describeOnlineRefund(completed, null, OnlinePaymentRefundReason.Chargeback))
      .toEqual({ text: 'El banco se lo regresó al cliente (contracargo).', tone: 'danger' });
    expect(describeOnlineRefund(completed, null, OnlinePaymentRefundReason.ByBusiness)).toEqual({ text: 'Mercado Pago ya lo devolvió.', tone: 'success' });
  });
});

describe('describeOnlineDispute', () => {
  it('warns while Mercado Pago holds the money (Fase 24K)', () => {
    expect(describeOnlineDispute(OnlinePaymentDispute.Open)).toEqual({
      title: 'Pago en disputa',
      text: 'El cliente reclamó el pago o su banco lo desconoció. Mercado Pago retiene el dinero mientras se resuelve; atiende la disputa en tu cuenta de Mercado Pago.',
      variant: 'warning',
    });
  });

  it('points to the refund of a lost one only when the order has it', () => {
    expect(describeOnlineDispute(OnlinePaymentDispute.Lost, true)).toMatchObject({
      title: 'Disputa perdida', variant: 'danger', text: expect.stringMatching(/regresó al cliente.*Lo ves en los reembolsos del pedido\.$/),
    });
    // The business had already given everything back: the bank took it again and the order has no refund of it.
    expect(describeOnlineDispute(OnlinePaymentDispute.Lost, false)?.text)
      .toMatch(/regresó al cliente.*Revisa el detalle en tu cuenta de Mercado Pago\.$/);
  });

  it('says a won one keeps the money, or warns that the refund recorded when it was lost stays on the order', () => {
    expect(describeOnlineDispute(OnlinePaymentDispute.Won)).toEqual({
      title: 'Disputa ganada', text: 'Se resolvió a tu favor: el dinero se queda contigo.', variant: 'success',
    });
    expect(describeOnlineDispute(OnlinePaymentDispute.Won, true)?.text)
      .toBe('Se resolvió a tu favor, pero el reembolso de la disputa sigue anotado en el pedido. Confirma en tu cuenta de Mercado Pago que el dinero te llegó.');
  });

  it('counts only the refunds of a claim or a chargeback as the dispute\'s', () => {
    expect(isDisputeRefund(OnlinePaymentRefundReason.Claim)).toBe(true);
    expect(isDisputeRefund(OnlinePaymentRefundReason.Chargeback)).toBe(true);
    expect(isDisputeRefund(OnlinePaymentRefundReason.InMercadoPago)).toBe(false);
    expect(isDisputeRefund(OnlinePaymentRefundReason.ByBusiness)).toBe(false);
    expect(isDisputeRefund(null)).toBe(false);
  });

  it('says nothing for an order with no dispute', () => {
    expect(describeOnlineDispute(OnlinePaymentDispute.None)).toBeNull();
    expect(describeOnlineDispute(null)).toBeNull();
    expect(describeOnlineDispute(undefined)).toBeNull();
  });
});
