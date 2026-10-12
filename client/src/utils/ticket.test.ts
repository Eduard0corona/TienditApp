import { describe, expect, it } from 'vitest';
import { OrderChannel, OrderPaymentMethod, OrderType, PlatformCollectedBy, UnitType, type OrderDto } from '../types';
import { billedItems, buildTicketText, cancellableQuantity, formatCancelled, formatTicketQuantity, getTicketPlatform } from './ticket';

const line = (overrides: Partial<OrderDto['items'][number]>): OrderDto['items'][number] => ({
  id: 'i1',
  productId: 'p1',
  productName: 'Tacos',
  quantity: 3,
  unitPrice: 25,
  totalPrice: 75,
  taxRate: 16,
  isTaxExempt: false,
  unit: UnitType.Unit,
  ...overrides,
});

const tab = (items: OrderDto['items']): OrderDto => ({
  id: 'o1',
  createdByBusiness: true,
  orderNumber: 'ORD-000007',
  businessType: 1 as OrderDto['businessType'],
  orderType: OrderType.Retail,
  status: 2 as OrderDto['status'],
  canCancel: false,
  allowedTransitions: [],
  customerName: 'Público en general',
  pricesIncludeTax: true,
  subTotal: 43.1,
  tax: 6.9,
  total: 50,
  taxBreakdown: [],
  paidAmount: 0,
  balance: 50,
  createdAt: '2026-10-08T20:00:00Z',
  items,
  statusHistory: [],
  servicePieces: [],
  payments: [],
});

describe('ticket of a tab with cancelled dishes', () => {
  it('bills only what the tab still charges, at what it charges', () => {
    const order = tab([
      line({ id: 'tacos', cancelledQuantity: 1, chargedPrice: 50 }),
      line({ id: 'beer', productName: 'Cerveza', quantity: 2, unitPrice: 45, totalPrice: 90, cancelledQuantity: 2, chargedPrice: 0 }),
    ]);

    expect(billedItems(order).map((item) => item.id)).toEqual(['tacos']);
    expect(formatTicketQuantity(order.items[0])).toBe('2 ×');
    const text = buildTicketText(order, { businessName: 'Fonda Lupita' });
    expect(text).toContain('2 × Tacos  $50.00');
    expect(text).not.toContain('Cerveza');
  });

  it('keeps an order from before cancellations as it was', () => {
    const order = tab([line({})]);

    expect(formatTicketQuantity(order.items[0])).toBe('3 ×');
    expect(buildTicketText(order, { businessName: 'Fonda Lupita' })).toContain('3 × Tacos  $75.00');
  });

  it('says what is left to cancel and what was cancelled, with the unit', () => {
    expect(cancellableQuantity(line({ cancelledQuantity: 1, returnedQuantity: 1 }))).toBe(1);
    expect(formatCancelled(line({ cancelledQuantity: 1 }))).toBe('1 cancelado');
    expect(formatCancelled(line({ cancelledQuantity: 2 }))).toBe('2 cancelados');
    expect(formatCancelled(line({ unit: UnitType.Kg, quantity: 1.255, cancelledQuantity: 0.505 }))).toBe('0.505 kg cancelado');
  });
});

describe('ticket of an order of a delivery platform', () => {
  const rappi: OrderDto = {
    ...tab([line({ notes: 'sin cebolla' })]),
    total: 75,
    channel: OrderChannel.Rappi,
    customerName: 'Juan',
    platformOrder: { externalOrderNumber: 'BC953', collectedBy: PlatformCollectedBy.Platform },
    payments: [{ id: 'p1', amount: 75, method: OrderPaymentMethod.Platform, paidAt: '2026-10-08T20:00:00Z', reference: 'BC953' }],
    paidAmount: 75,
    balance: 0,
  };

  it('lists the products of an order the platform charged without prices, and says where it was paid', () => {
    const text = buildTicketText(rappi, { businessName: 'Fonda Lupita' });

    expect(text).toContain('*Rappi · Folio BC953*');
    expect(text).toContain('Bolsa: Juan');
    expect(text).toContain('3 × Tacos\n   _sin cebolla_');
    expect(text).toContain('Pagado en la app de Rappi');
    expect(text).not.toContain('$');
    expect(text).not.toContain('Total');
    expect(text).not.toMatch(/comisi[oó]n|retenci|neto/i);
  });

  it('keeps the prices of an order the business collects, with what the courier still pays', () => {
    const didi: OrderDto = {
      ...rappi,
      channel: OrderChannel.DidiFood,
      platformOrder: { externalOrderNumber: '12', collectedBy: PlatformCollectedBy.Business },
      payments: [],
      paidAmount: 0,
      balance: 75,
    };

    const text = buildTicketText(didi, { businessName: 'Fonda Lupita' });

    expect(text).toContain('*DiDi Food · Folio 12*');
    expect(text).toContain('3 × Tacos  $75.00');
    expect(text).toContain('*Total: $75.00*');
    expect(text).toContain('*Cobrar al repartidor: $75.00*');
    expect(text).not.toContain('Saldo pendiente');
    expect(text).not.toMatch(/comisi[oó]n|retenci|neto/i);
  });

  it('reads the platform only from an order of one', () => {
    expect(getTicketPlatform(rappi)).toEqual({ name: 'Rappi', folio: 'BC953', packing: true });
    expect(getTicketPlatform({ ...rappi, channel: OrderChannel.Counter })).toBeNull();
    expect(getTicketPlatform({ ...rappi, platformOrder: null })).toBeNull();
  });
});
