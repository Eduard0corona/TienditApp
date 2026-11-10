import type { ChargeResult } from '../../components/sales/ChargePanel';
import { buildOfflineTicketOrder } from '../../offline/offlineTicket';
import { enqueueSale } from '../../offline/saleQueue';
import type { CreateOrderRequest, OrderDto } from '../../types';
import { newUuid } from '../../utils/uuid';
import type { SaleTotals } from './saleRequest';
import type { CartLine } from './sellModel';

export interface OfflineSale {
  tenantId: string;
  userId: string;
  request: CreateOrderRequest;
  charge: ChargeResult;
  cart: CartLine[];
  totals: SaleTotals;
  pricesIncludeTax: boolean;
}

/**
 * Keeps a charged sale on the device when the connection is down; it is sent with its key when it comes back. The ticket
 * for the customer is built here with the totals the screen showed; null when the device cannot keep it (no IndexedDB in
 * a private window): the sale needs the connection.
 */
export async function keepSaleOnDevice(sale: OfflineSale): Promise<OrderDto | null> {
  const { request, charge, totals } = sale;
  const capturedAt = new Date().toISOString();
  try {
    const pending = await enqueueSale({
      id: request.idempotencyKey ?? newUuid(),
      tenantId: sale.tenantId,
      userId: sale.userId,
      request: { ...request, idempotencyKey: undefined },
      total: totals.total,
      capturedAt,
    });
    return buildOfflineTicketOrder({
      folio: pending.folio,
      capturedAt,
      lines: sale.cart,
      totals,
      pricesIncludeTax: sale.pricesIncludeTax,
      payments: charge.payments.map((payment) => ({
        amount: payment.amount,
        method: payment.method,
        reference: payment.reference,
        tip: payment.tip,
        cashReceived: payment.cashReceived,
      })),
      customerName: request.customerName,
      tableNumber: request.tableNumber,
    });
  } catch {
    return null;
  }
}
