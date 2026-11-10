import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ChargeResult } from '../../components/sales/ChargePanel';
import { enqueueSale } from '../../offline/saleQueue';
import type { CreateOrderRequest } from '../../types';
import { OrderPaymentMethod } from '../../types';
import { keepSaleOnDevice, type OfflineSale } from './offlineSale';
import { saleTotals } from './saleRequest';
import type { CartLine } from './sellModel';

// jsdom has no IndexedDB: the queue is replaced, as in Vender's tests.
vi.mock('../../offline/saleQueue', () => ({ enqueueSale: vi.fn() }));

const cart: CartLine[] = [{
  key: 'taco', productId: 'taco', optionIds: [], optionsLabel: '', name: 'Taco', unitPrice: 25, quantity: 2, notes: '',
  showNotes: false, isService: false, ratePercent: 16, isExempt: false, fractional: false,
}];
const charge: ChargeResult = {
  payments: [{ method: OrderPaymentMethod.Cash, amount: 50, cashReceived: 100, tip: 0 }],
  change: 50, cashReceived: 100, tip: 0, onCredit: false,
} as ChargeResult;
const request = {
  items: [{ productId: 'taco', quantity: 2 }], customerName: 'Lupita', tableNumber: '4', idempotencyKey: 'key-1', expectedTotal: 50,
} as CreateOrderRequest;
const sale: OfflineSale = { tenantId: 't1', userId: 'u1', request, charge, cart, totals: saleTotals(cart, true, 16, null, null), pricesIncludeTax: true };

describe('keepSaleOnDevice', () => {
  beforeEach(() => vi.mocked(enqueueSale).mockClear());

  it('keeps the sale with its key as its id, so sending it again never charges twice', async () => {
    vi.mocked(enqueueSale).mockImplementation(async (pending) => ({ ...pending, folio: 'SC-0001', status: 'pending', attempts: 0 }));

    const order = await keepSaleOnDevice(sale);

    const queued = vi.mocked(enqueueSale).mock.calls[0][0];
    expect(queued.id).toBe('key-1');
    // The key travels as the id only: the request is sent again with it when the connection comes back.
    expect(queued.request).toEqual({ ...request, idempotencyKey: undefined });
    expect(queued).toMatchObject({ tenantId: 't1', userId: 'u1', total: 50 });
    // The ticket for the customer, with the folio of the device and what the screen showed.
    expect(order).toMatchObject({
      id: 'SC-0001', orderNumber: 'SC-0001', total: 50, paidAmount: 50, balance: 0, customerName: 'Lupita', tableNumber: '4',
      createdAt: queued.capturedAt,
    });
  });

  it('says the device cannot keep it when its storage refuses (a private window)', async () => {
    vi.mocked(enqueueSale).mockRejectedValueOnce(new Error('IndexedDB unavailable'));
    await expect(keepSaleOnDevice(sale)).resolves.toBeNull();
  });
});
