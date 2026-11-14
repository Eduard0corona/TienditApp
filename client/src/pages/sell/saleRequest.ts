import type { CustomerAccount } from '../../api/customerCredit';
import type { ChargeBalance, ChargeCredit } from '../../components/sales/ChargePanel';
import type { CreateOrderRequest } from '../../types';
import { OrderChannel, OrderType } from '../../types';
import { parsePaysWith } from '../../utils/courierCash';
import { normalizePlatformFolio, normalizePlatformOrderId, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { estimateDeliveryTotals, normalizePostalCode, type DeliveryZoneTerms } from '../../utils/homeDelivery';
import { estimateVisitTotals } from '../../utils/homeVisits';
import { estimateOrderTotals, type OrderTotalsEstimate } from '../../utils/orderTotals';
import type { CartLine, ChatOrigin, SaleAppointment, SaleDelivery, SaleDetails } from './sellModel';

/** What the ticket shows: the products, and the fee of a delivery or a visit as one more concept when there is one. */
export type SaleTotals = OrderTotalsEstimate & { productsTotal: number | null; fee: number | null };

/**
 * Quick feedback only, as the server will compute it: the fee of the zone that reaches the address, or the charge for the
 * visit agreed, is one more concept at the business's IVA rate.
 */
export function saleTotals(
  cart: CartLine[],
  pricesIncludeTax: boolean,
  defaultTaxRate: number,
  deliveryZone: DeliveryZoneTerms | null,
  visitFee: number | null | undefined,
): SaleTotals {
  const lines = cart.map((line) => ({
    unitPrice: line.unitPrice,
    quantity: line.quantity,
    ratePercent: line.ratePercent,
    isExempt: line.isExempt,
  }));
  if (deliveryZone) return estimateDeliveryTotals(lines, pricesIncludeTax, deliveryZone, defaultTaxRate);
  if (visitFee) return estimateVisitTotals(lines, pricesIncludeTax, visitFee, defaultTaxRate);
  return { ...estimateOrderTotals(lines, pricesIncludeTax), productsTotal: null, fee: null };
}

/** A weighed product counts as one article, whatever it weighs. */
export function countArticles(cart: CartLine[]): number {
  return cart.reduce((sum, line) => sum + (line.fractional ? 1 : line.quantity), 0);
}

/** «Paga con» goes only as an amount; anything else was refused before sending. */
export function payingWith(delivery: SaleDelivery | null): number | null {
  const typed = delivery ? parsePaysWith(delivery.paysWith ?? '') : null;
  return typeof typed === 'number' ? typed : null;
}

/** Where the sale on screen comes from and goes, as the request carries it. */
export interface SaleContext {
  cart: CartLine[];
  details: SaleDetails;
  totals: SaleTotals;
  /** The platform whose app took the order; null at the counter, in a chat or a tab. */
  platform: DeliveryPlatform | null;
  /** The chat the sale was started from: a WhatsApp order. */
  chatOrigin: ChatOrigin | null;
  delivery: SaleDelivery | null;
  appointment: SaleAppointment | null;
  isFood: boolean;
  hasServices: boolean;
  branchId?: string;
}

/** The order of the sale on screen, without its payments or its key. */
export function buildOrderRequest(sale: SaleContext): CreateOrderRequest {
  const { details, platform, delivery } = sale;
  const paysWith = payingWith(delivery);
  return {
    customerName: details.customerName.trim() || undefined,
    // The app of a platform shares neither the customer's phone nor a table.
    customerPhone: platform ? undefined : details.customerPhone.trim() || undefined,
    customerNotes: details.orderNotes.trim() || undefined,
    // An order sent to an address has no table: the address says where it goes.
    tableNumber: sale.isFood && !platform && !delivery ? details.tableNumber.trim() || undefined : undefined,
    // A service in the cart makes it a service order (pieces to track, if the service is trackable).
    orderType: sale.hasServices ? OrderType.Service : OrderType.Retail,
    items: sale.cart.map((line) => ({
      productId: line.productId,
      quantity: line.quantity,
      notes: line.notes.trim() || undefined,
      optionIds: line.optionIds.length > 0 ? line.optionIds : undefined,
    })),
    expectedTotal: sale.totals.total,
    channel: sale.chatOrigin ? OrderChannel.WhatsApp : platform ?? undefined,
    externalOrderNumber: platform ? normalizePlatformFolio(details.externalOrderNumber) : undefined,
    externalOrderId: platform && details.externalOrderId.trim() ? normalizePlatformOrderId(details.externalOrderId) : undefined,
    collectedBy: platform ? details.collectedBy : undefined,
    branchId: sale.branchId,
    delivery: delivery
      ? {
        postalCode: normalizePostalCode(delivery.postalCode),
        addressLine: delivery.addressLine.trim(),
        references: delivery.references.trim() || undefined,
        paysWith: paysWith ?? undefined,
      }
      : undefined,
    appointmentId: sale.appointment?.id,
    // The deposit paid online this screen took off (Fase 31O): if the appointment no longer holds it, the server refuses
    // the sale instead of saving it short.
    expectedAppointmentDeposit: sale.appointment ? sale.appointment.deposit ?? 0 : undefined,
  };
}

/**
 * What the customer's account lets them pay with: fiado (Fase 26A) when the business sells to them on credit, and the
 * balance in their favor (Fase 26D). The server checks both again as it charges.
 */
export function chargeFundsOf(
  account: CustomerAccount | undefined,
  customerName: string,
): { credit?: ChargeCredit; balance?: ChargeBalance } {
  if (!account) return {};
  const name = account.name ?? (customerName.trim() || 'El cliente');
  return {
    credit: account.sellsOnCredit && account.available != null
      ? { name, available: account.available, overdue: account.overdue }
      : undefined,
    balance: account.creditBalance > 0 ? { name, available: account.creditBalance } : undefined,
  };
}
