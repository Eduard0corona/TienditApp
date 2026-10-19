import { describe, expect, it } from 'vitest';
import { OnlinePaymentRefundStatus } from '../api/generated/enums';
import { describeOnlineRefund } from './onlineRefunds';

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
});
