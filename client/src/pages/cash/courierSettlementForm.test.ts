import { describe, expect, it } from 'vitest';
import type { CourierOrder } from '../../api/courierSettlements';
import { CourierSettlementAdjustmentKind, DeliveryCourier, OrderPaymentMethod } from '../../types';
import {
  ADJUSTMENT_AMOUNT_MESSAGE,
  ADJUSTMENT_DESCRIPTION_REQUIRED_MESSAGE,
  ADJUSTMENT_KIND_MESSAGE,
  adjustmentForDifference,
  checkAdjustment,
  doesNotBalanceMessage,
  newAdjustment,
  parseHandedAmount,
  summarize,
  toBody,
} from './courierSettlementForm';

const order = (orderId: string, balance: number) => ({ orderId, balance } as CourierOrder);
const plain = (text: string) => text.replace(/\u00a0/g, ' ');

describe('courierSettlementForm', () => {
  it('takes off the trip, the fee and a shortage, and lets «Otro» take off or add with what it was', () => {
    expect(checkAdjustment(newAdjustment({ kind: CourierSettlementAdjustmentKind.TripFee, amountText: '45' })).amount).toBe(-45);
    expect(checkAdjustment(newAdjustment({ kind: CourierSettlementAdjustmentKind.Shortage, amountText: '$10.50' })).amount).toBe(-10.5);
    expect(checkAdjustment(newAdjustment({ kind: CourierSettlementAdjustmentKind.Other, amountText: '5', description: 'Redondeo' })).amount).toBe(-5);
    expect(checkAdjustment(newAdjustment({
      kind: CourierSettlementAdjustmentKind.Other, amountText: '5', description: 'Redondeo', adds: true,
    })).amount).toBe(5);

    expect(checkAdjustment(newAdjustment({ amountText: '5' })).kindProblem).toBe(ADJUSTMENT_KIND_MESSAGE);
    expect(checkAdjustment(newAdjustment({ kind: CourierSettlementAdjustmentKind.TripFee, amountText: '0' })).amountProblem)
      .toBe(ADJUSTMENT_AMOUNT_MESSAGE);
    expect(checkAdjustment(newAdjustment({ kind: CourierSettlementAdjustmentKind.TripFee, amountText: '-45' })).amountProblem)
      .toBe(ADJUSTMENT_AMOUNT_MESSAGE);
    expect(checkAdjustment(newAdjustment({ kind: CourierSettlementAdjustmentKind.Other, amountText: '5', description: ' ' })).descriptionProblem)
      .toBe(ADJUSTMENT_DESCRIPTION_REQUIRED_MESSAGE);
  });

  it('adds up what the courier should hand over to the cent, and the difference with what it handed', () => {
    const orders = [order('a', 275.1), order('b', 274.9)];
    const rows = [newAdjustment({ kind: CourierSettlementAdjustmentKind.TripFee, amountText: '45' })];

    expect(summarize(orders, rows, '505')).toMatchObject({ orders: 550, adjustments: -45, expected: 505, handed: 505, difference: 0, adjustmentsValid: true });
    expect(summarize(orders, rows, '500').difference).toBe(-5);
    expect(summarize(orders, rows, '').difference).toBeUndefined();
    expect(summarize(orders, [newAdjustment()], '550').adjustmentsValid).toBe(false);
    expect(parseHandedAmount('-1')).toBeUndefined();
    expect(parseHandedAmount('0')).toBe(0);
  });

  it('explains a difference with a shortage or with «Otro» that adds', () => {
    const shortage = adjustmentForDifference(-5);
    const surplus = adjustmentForDifference(2.5);

    expect([shortage.kind, checkAdjustment(shortage).amount]).toEqual([CourierSettlementAdjustmentKind.Shortage, -5]);
    expect([surplus.kind, surplus.description, checkAdjustment(surplus).amount]).toEqual([CourierSettlementAdjustmentKind.Other, 'Sobrante', 2.5]);
    expect(plain(doesNotBalanceMessage(500, 505)))
      .toBe('Entregó $500.00 y los pedidos con sus ajustes dan $505.00: la diferencia debe ser $0.00. Registra lo que falta como un ajuste.');
  });

  it('sends the orders, the signed adjustments and what was handed over', () => {
    const body = toBody(
      DeliveryCourier.Paquetenvia, OrderPaymentMethod.Transfer, 230, [order('a', 275)],
      [newAdjustment({ kind: CourierSettlementAdjustmentKind.TripFee, amountText: '45', description: ' ' })], ' SPEI 1 ', '',
    );

    expect(body).toEqual({
      courier: DeliveryCourier.Paquetenvia,
      method: OrderPaymentMethod.Transfer,
      handedAmount: 230,
      orderIds: ['a'],
      ordersTotal: 275,
      adjustments: [{ kind: CourierSettlementAdjustmentKind.TripFee, description: null, amount: -45 }],
      reference: 'SPEI 1',
      notes: null,
    });
  });
});
