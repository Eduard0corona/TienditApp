import { describe, expect, it } from 'vitest';
import type { CustomerAccount } from '../../api/customerCredit';
import { OrderChannel, OrderType, PlatformCollectedBy } from '../../types';
import { buildOrderRequest, chargeFundsOf, countArticles, payingWith, saleTotals, type SaleContext } from './saleRequest';
import { NO_SALE_DETAILS, type CartLine } from './sellModel';

function line(overrides: Partial<CartLine>): CartLine {
  return {
    key: 'taco', productId: 'taco', optionIds: [], optionsLabel: '', name: 'Taco', unitPrice: 25, quantity: 2, notes: '',
    showNotes: false, isService: false, ratePercent: 16, isExempt: false, fractional: false, ...overrides,
  };
}

const cart = [line({}), line({ key: 'agua', productId: 'agua', name: 'Agua', unitPrice: 20, quantity: 1, notes: ' fría ', optionIds: ['grande'] })];
const totals = saleTotals(cart, true, 16, null, null);
const counter: SaleContext = {
  cart, details: NO_SALE_DETAILS, totals, platform: null, chatOrigin: null, delivery: null, appointment: null, isFood: true, hasServices: false,
};

describe('saleTotals', () => {
  it('adds the products, as the server will', () => {
    expect(totals).toMatchObject({ total: 70, productsTotal: null, fee: null });
  });

  it('adds the fee of the zone, or the charge for the visit, as one more concept', () => {
    const zone = { name: 'Centro', isActive: true, postalCodes: ['06700'], fee: 30, minimumOrder: 0 };
    expect(saleTotals(cart, true, 16, zone, null)).toMatchObject({ total: 100, productsTotal: 70, fee: 30 });
    expect(saleTotals(cart, true, 16, null, 80)).toMatchObject({ total: 150, productsTotal: 70, fee: 80 });
  });
});

describe('countArticles', () => {
  it('counts a weighed product as one article, whatever it weighs', () => {
    expect(countArticles([line({ quantity: 3 }), line({ quantity: 0.75, fractional: true })])).toBe(4);
  });
});

describe('payingWith', () => {
  it('sends «paga con» only as an amount', () => {
    expect(payingWith({ postalCode: '', addressLine: '', references: '', paysWith: '500' })).toBe(500);
    expect(payingWith({ postalCode: '', addressLine: '', references: '', paysWith: 'quinientos' })).toBeNull();
    expect(payingWith({ postalCode: '', addressLine: '', references: '' })).toBeNull();
    expect(payingWith(null)).toBeNull();
  });
});

describe('buildOrderRequest', () => {
  it('sends a counter sale with what was typed, trimmed, and the table of a kitchen', () => {
    const request = buildOrderRequest({
      ...counter,
      details: { ...NO_SALE_DETAILS, customerName: ' Lupita ', customerPhone: ' 5512345678 ', tableNumber: ' 4 ', orderNotes: '  ' },
      branchId: 'norte',
    });
    expect(request).toEqual({
      customerName: 'Lupita',
      customerPhone: '5512345678',
      customerNotes: undefined,
      tableNumber: '4',
      orderType: OrderType.Retail,
      items: [
        { productId: 'taco', quantity: 2, notes: undefined, optionIds: undefined },
        { productId: 'agua', quantity: 1, notes: 'fría', optionIds: ['grande'] },
      ],
      expectedTotal: 70,
      channel: undefined,
      externalOrderNumber: undefined,
      externalOrderId: undefined,
      collectedBy: undefined,
      branchId: 'norte',
      delivery: undefined,
      appointmentId: undefined,
    });
    // A shop has no tables.
    expect(buildOrderRequest({ ...counter, isFood: false, details: { ...NO_SALE_DETAILS, tableNumber: '4' } }).tableNumber).toBeUndefined();
  });

  it('sends an order of a platform with its folio and long id normalized, never the phone or a table', () => {
    const request = buildOrderRequest({
      ...counter,
      platform: OrderChannel.UberEats,
      details: {
        ...NO_SALE_DETAILS, channel: OrderChannel.UberEats, customerName: 'Bolsa de Ana', customerPhone: '5512345678', tableNumber: '4',
        externalOrderNumber: ' 4f2a1 ', externalOrderId: ' 9c0b-77 ', collectedBy: PlatformCollectedBy.Business,
      },
    });
    expect(request).toMatchObject({
      customerName: 'Bolsa de Ana',
      customerPhone: undefined,
      tableNumber: undefined,
      channel: OrderChannel.UberEats,
      externalOrderNumber: '4F2A1',
      externalOrderId: '9C0B-77',
      collectedBy: PlatformCollectedBy.Business,
    });
  });

  it('sends a chat order as of WhatsApp, a service as a service order and the appointment it charges', () => {
    expect(buildOrderRequest({ ...counter, chatOrigin: { phone: '5512345678', conversationId: 'c1' } }).channel).toBe(OrderChannel.WhatsApp);
    expect(buildOrderRequest({ ...counter, hasServices: true }).orderType).toBe(OrderType.Service);
    const appointment = { id: 'cita', customerName: 'Ana', description: 'Corte', visitFee: null, zoneName: null };
    expect(buildOrderRequest({ ...counter, appointment }).appointmentId).toBe('cita');
  });

  it('sends a delivery with its address trimmed, «paga con» as an amount, and no table', () => {
    const request = buildOrderRequest({
      ...counter,
      details: { ...NO_SALE_DETAILS, tableNumber: '4' },
      delivery: { postalCode: ' 06700 ', addressLine: ' Calle 5 #12 ', references: ' ', paysWith: '500' },
    });
    expect(request.tableNumber).toBeUndefined();
    expect(request.delivery).toEqual({ postalCode: '06700', addressLine: 'Calle 5 #12', references: undefined, paysWith: 500 });
  });
});

describe('chargeFundsOf', () => {
  const account = { name: 'Ana López', sellsOnCredit: true, available: 300, overdue: 50, creditBalance: 40 } as CustomerAccount;

  it('offers the fiado the business gives and the balance in their favor', () => {
    expect(chargeFundsOf(account, 'Ana')).toEqual({
      credit: { name: 'Ana López', available: 300, overdue: 50 },
      balance: { name: 'Ana López', available: 40 },
    });
  });

  it('offers neither without them, naming the customer as typed when the account has no name', () => {
    expect(chargeFundsOf(undefined, 'Ana')).toEqual({});
    expect(chargeFundsOf({ ...account, sellsOnCredit: false, creditBalance: 0 }, 'Ana')).toEqual({ credit: undefined, balance: undefined });
    expect(chargeFundsOf({ ...account, name: null, available: null } as unknown as CustomerAccount, ' ')).toEqual({
      credit: undefined,
      balance: { name: 'El cliente', available: 40 },
    });
  });
});
