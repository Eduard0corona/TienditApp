import { describe, expect, it } from 'vitest';
import {
  OrderChannel,
  OrderPaymentMethod,
  OrderStatus,
  OrderType,
  PlatformCollectedBy,
  TenantCapability,
  type OrderDto,
} from '../../types';
import {
  ORDER_TYPE_STATUS_OPTIONS,
  chargedByPlatform,
  getOrderTypes,
  getPaymentMethodText,
  getPieceAllowedTransitions,
  isOnTheWay,
  withItsQuote,
} from './ordersListModel';

const order = (overrides: Partial<OrderDto>) => ({
  id: 'o1',
  channel: OrderChannel.Counter,
  status: OrderStatus.Confirmed,
  delivery: null,
  platformOrder: null,
  quote: null,
  workAppointments: [],
  ...overrides,
}) as unknown as OrderDto;

const payment = (method: OrderPaymentMethod, reference: string | null = null) =>
  ({ id: 'p1', method, reference, amount: 100 }) as unknown as OrderDto['payments'][number];

describe('ordersListModel', () => {
  it('lists the kinds of order the server lists: sales, services or both while unknown', () => {
    expect(getOrderTypes(TenantCapability.Retail)).toEqual([OrderType.Retail]);
    expect(getOrderTypes(TenantCapability.Food | TenantCapability.Kitchen)).toEqual([OrderType.Retail]);
    expect(getOrderTypes(TenantCapability.Service | TenantCapability.Appointments)).toEqual([OrderType.Service]);
    expect(getOrderTypes(TenantCapability.Retail | TenantCapability.Service)).toEqual([OrderType.Retail, OrderType.Service]);
    expect(getOrderTypes(null)).toEqual([OrderType.Retail, OrderType.Service]);
  });

  it('offers each kind only its own states', () => {
    expect(ORDER_TYPE_STATUS_OPTIONS[OrderType.Retail].map((option) => option.value))
      .toEqual([OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.InPreparation, OrderStatus.Ready, OrderStatus.Delivered, OrderStatus.Cancelled]);
    expect(ORDER_TYPE_STATUS_OPTIONS[OrderType.Service].map((option) => option.value))
      .toEqual([OrderStatus.Pending, OrderStatus.Accepted, OrderStatus.InProgress, OrderStatus.Completed, OrderStatus.Cancelled]);
  });

  it('says an order is on the way only while it is out with the courier', () => {
    const dispatched = { dispatchedAt: '2026-11-09T12:00:00Z' } as OrderDto['delivery'];
    expect(isOnTheWay(order({ delivery: dispatched }))).toBe(true);
    expect(isOnTheWay(order({ delivery: { dispatchedAt: null } as unknown as OrderDto['delivery'] }))).toBe(false);
    expect(isOnTheWay(order({}))).toBe(false);
    for (const status of [OrderStatus.Delivered, OrderStatus.Completed, OrderStatus.Cancelled]) {
      expect(isOnTheWay(order({ delivery: dispatched, status }))).toBe(false);
    }
  });

  it('says a platform charged the order only when its app collected it', () => {
    const collected = (collectedBy: PlatformCollectedBy) =>
      ({ externalOrderNumber: 'BC953', collectedBy }) as OrderDto['platformOrder'];
    expect(chargedByPlatform(order({ channel: OrderChannel.Rappi, platformOrder: collected(PlatformCollectedBy.Platform) }))).toBe(true);
    expect(chargedByPlatform(order({ channel: OrderChannel.Rappi, platformOrder: collected(PlatformCollectedBy.Business) }))).toBe(false);
    expect(chargedByPlatform(order({ channel: OrderChannel.Counter, platformOrder: collected(PlatformCollectedBy.Platform) }))).toBe(false);
  });

  it('reads what a platform charged with the folio of its app', () => {
    const platformOrder = { externalOrderNumber: 'BC953' } as OrderDto['platformOrder'];
    expect(getPaymentMethodText(order({ platformOrder }), payment(OrderPaymentMethod.Platform, 'OTRO'))).toBe('Plataforma · BC953');
    expect(getPaymentMethodText(order({}), payment(OrderPaymentMethod.Platform, 'R-1'))).toBe('Plataforma · R-1');
    expect(getPaymentMethodText(order({}), payment(OrderPaymentMethod.Platform))).toBe('Plataforma');
    expect(getPaymentMethodText(order({ platformOrder }), payment(OrderPaymentMethod.Cash, 'BC953'))).toBe('Efectivo');
  });

  it('keeps the quote of the order when the answer of a charge does not bring it', () => {
    const quote = { id: 'q1', number: 'COT-7', revision: 1 } as OrderDto['quote'];
    const workAppointments = [{ id: 'a1' }] as OrderDto['workAppointments'];
    const previous = order({ quote, workAppointments });

    const kept = withItsQuote(order({ status: OrderStatus.Completed }), previous);
    expect(kept.status).toBe(OrderStatus.Completed);
    expect(kept.quote).toBe(quote);
    expect(kept.workAppointments).toBe(workAppointments);

    const answered = order({ quote: { id: 'q2', number: 'COT-8', revision: 1 } as OrderDto['quote'] });
    expect(withItsQuote(answered, previous)).toBe(answered);
  });

  it('moves a tracked piece forward or cancels it until it is completed', () => {
    expect(getPieceAllowedTransitions(0)).toEqual([1, 99]);
    expect(getPieceAllowedTransitions(1)).toEqual([2, 99]);
    expect(getPieceAllowedTransitions(2)).toEqual([]);
    expect(getPieceAllowedTransitions(99)).toEqual([]);
  });
});
