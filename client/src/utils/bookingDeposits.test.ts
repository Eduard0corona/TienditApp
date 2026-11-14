import { describe, expect, it } from 'vitest';
import { AppointmentDepositStatus } from '../api/generated/enums';
import {
  businessDepositLabel, canReturnDeposit, cancelDepositSentence, customerDepositLine, depositPolicy, holdsDeposit,
} from './bookingDeposits';

const held = { depositAmount: 45, depositStatus: AppointmentDepositStatus.Held };

describe('bookingDeposits', () => {
  it('says where the deposit stands, for the business and for its customer', () => {
    expect(businessDepositLabel(held)).toBe('Anticipo de $45.00 pagado en línea: entra al cobrarla');
    expect(businessDepositLabel({ depositAmount: 45, depositStatus: AppointmentDepositStatus.Applied })).toBe('Anticipo de $45.00 en su venta');
    expect(businessDepositLabel({ depositAmount: 45, depositStatus: AppointmentDepositStatus.Returned })).toBe('Anticipo de $45.00 devuelto al cliente');
    expect(businessDepositLabel({ depositAmount: 45, depositStatus: AppointmentDepositStatus.Kept })).toBe('Anticipo de $45.00: te lo quedas');
    expect(businessDepositLabel({})).toBeNull();

    expect(customerDepositLine(held)).toBe('Pagaste un anticipo de $45.00: se descuenta al cobrarte la cita.');
    expect(customerDepositLine({ depositAmount: 45, depositStatus: AppointmentDepositStatus.Returned })).toMatch(/^Te devolvemos tu anticipo de \$45\.00/);
    expect(customerDepositLine({ depositAmount: 45, depositStatus: AppointmentDepositStatus.Kept })).toBe('El negocio se quedó con tu anticipo de $45.00.');
    expect(customerDepositLine({ depositAmount: 45, depositStatus: AppointmentDepositStatus.Applied })).toBe('Tu anticipo de $45.00 se descontó de tu cuenta.');
    expect(customerDepositLine({ depositAmount: null })).toBeNull();
  });

  it('gives back only a deposit held or kept', () => {
    expect([holdsDeposit(held), canReturnDeposit(held)]).toEqual([true, true]);
    const kept = { depositAmount: 45, depositStatus: AppointmentDepositStatus.Kept };
    expect([holdsDeposit(kept), canReturnDeposit(kept)]).toEqual([false, true]);
    for (const depositStatus of [AppointmentDepositStatus.Applied, AppointmentDepositStatus.Returned])
      expect(canReturnDeposit({ depositAmount: 45, depositStatus })).toBe(false);
    expect(canReturnDeposit({})).toBe(false);
  });

  it('says what cancelling does with the deposit, as the server decided it', () => {
    expect(cancelDepositSentence({ ...held, cancelReturnsDeposit: true })).toBe(' Te devolvemos tu anticipo de $45.00.');
    expect(cancelDepositSentence({ ...held, cancelReturnsDeposit: false }))
      .toBe(' Faltan menos de 24 horas: el negocio se queda con tu anticipo de $45.00.');
    expect(cancelDepositSentence({ depositAmount: 45, depositStatus: AppointmentDepositStatus.Returned })).toBe('');
    // The three cases in which the server gives it back when the customer cancels (BookingDeposits.CustomerCancelReturns).
    expect(depositPolicy(48)).toMatch(new RegExp('y si la cancelas mientras espera su confirmación, después de que el negocio la cambió de la hora '
      + 'que pediste o con al menos 48 horas\\. Si la cancelas ya confirmada con menos tiempo, o no llegas, el negocio se lo queda\\.$'));
  });
});
