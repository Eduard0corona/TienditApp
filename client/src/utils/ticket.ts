import { PlatformCollectedBy, type OrderDto } from '../types';
import { changeFor, stillToCollect } from './courierCash';
import { getPlatformName, isDeliveryPlatform } from './deliveryPlatforms';
import { DELIVERY_COURIER_LABELS, getPaymentMethodLabel } from './labels';
import { formatMoney } from './money';
import { getVatLabel } from './orderTotals';
import { describeItem } from './productOptions';
import { allowsFractions, formatQuantity, roundQuantity } from './units';
import { formatInBusinessZone } from './dates';

export interface TicketExtras {
  businessName: string;
  /** The business's logo, printed above its name; only the name while it has none. */
  logoUrl?: string | null;
  /** Charged without connection: the folio is the device's until the server gives the real one. */
  provisional?: boolean;
}

export interface TicketPaymentTotals {
  /** Tips given with the payments (apart from the total). */
  tip: number;
  /** Cash the customer handed over. */
  cashReceived: number;
  /** Cash given back. */
  change: number;
}

/** Tip, cash handed over and change as the payments stored them, so a reprinted ticket shows them too. */
export function getTicketPaymentTotals(order: OrderDto): TicketPaymentTotals {
  const sumCents = (pick: (payment: OrderDto['payments'][number]) => number | null | undefined) =>
    order.payments.reduce((sum, payment) => sum + Math.round((pick(payment) ?? 0) * 100), 0) / 100;
  return {
    tip: sumCents((payment) => payment.tip),
    cashReceived: sumCents((payment) => payment.cashReceived),
    change: sumCents((payment) => payment.change),
  };
}

/** How the ticket of an order of a delivery platform reads. Its commission, withholdings and net never print. */
export interface TicketPlatform {
  /** "Rappi". */
  name: string;
  /** The folio of the platform's app, to match the bag with the courier. */
  folio: string;
  /**
   * The platform charged in its app: the ticket packs the bag, with the products and no prices or totals, so it never
   * contradicts what the app charged (until each platform has its own prices).
   */
  packing: boolean;
}

export function getTicketPlatform(order: OrderDto): TicketPlatform | null {
  if (!isDeliveryPlatform(order.channel) || !order.platformOrder) return null;
  return {
    name: getPlatformName(order.channel),
    folio: order.platformOrder.externalOrderNumber,
    packing: order.platformOrder.collectedBy === PlatformCollectedBy.Platform,
  };
}

/** How the ticket of an order sent to the customer's address reads: where it goes, what it charged and the courier's folio. */
export interface TicketDelivery {
  /** "Av. Reforma 123, Centro · CP 72000". */
  address: string;
  references: string | null;
  /** What the order charged for the delivery, in its total: 0 when it was free. */
  fee: number;
  /** "Paquetenvia · Folio PQ1234", once the trip is written down. */
  courier: string | null;
  /** "Cobrar $275.00 · paga con $500.00 · cambio $225.00": what the courier collects, while the order owes it (Fase 22D). */
  collection: string | null;
}

export function getTicketDelivery(order: Pick<OrderDto, 'delivery'> & Partial<Pick<OrderDto, 'balance'>>): TicketDelivery | null {
  const delivery = order.delivery;
  if (!delivery) return null;
  return {
    address: `${delivery.addressLine} · CP ${delivery.postalCode}`,
    references: delivery.references || null,
    fee: delivery.fee,
    courier: delivery.courier != null && delivery.courierFolio
      ? `${DELIVERY_COURIER_LABELS[delivery.courier]} · Folio ${delivery.courierFolio}`
      : null,
    collection: describeTicketCollection(delivery, order.balance ?? 0),
  };
}

/**
 * What the courier collects: what it was sent out to collect (less if part was paid meanwhile) or, before it goes out, the
 * balance with the customer's bill. Nothing once the order is paid.
 */
function describeTicketCollection(delivery: NonNullable<OrderDto['delivery']>, balance: number): string | null {
  if (balance <= 0) return null;
  const toCollect = delivery.cashToCollect != null
    ? stillToCollect(delivery.cashToCollect, balance)
    : !delivery.dispatchedAt && delivery.paysWith != null ? balance : null;
  if (toCollect == null) return null;
  const parts = [`Cobrar ${formatMoney(toCollect)}`];
  if (delivery.paysWith != null) {
    parts.push(`paga con ${formatMoney(delivery.paysWith)}`, `cambio ${formatMoney(changeFor(delivery.paysWith, toCollect))}`);
  }
  return parts.join(' · ');
}

/** What the sale of an appointment at the customer's address charged for the visit (Fase 22E), already in the total. */
export function getTicketVisitCharge(order: Pick<OrderDto, 'visitCharge'>): number | null {
  return order.visitCharge ? order.visitCharge.fee : null;
}

/** "$35.00", or "Gratis" when the zone delivered it free. */
export function formatDeliveryFee(fee: number): string {
  return fee > 0 ? formatMoney(fee) : 'Gratis';
}

/** The name on the order (or on the bag, for an order of a platform), unless it is the general public's. */
export function getTicketCustomerName(order: Pick<OrderDto, 'customerName'>): string | null {
  return order.customerName && order.customerName !== 'Público en general' ? order.customerName : null;
}

type OrderLine = OrderDto['items'][number];

/** Units a line still charges: what was ordered less what was cancelled from a tab. */
export function activeQuantity(item: OrderLine): number {
  return roundQuantity(item.quantity - (item.cancelledQuantity ?? 0));
}

/** What a line charges, as the API computed it (the whole line when nothing of it was cancelled). */
export function chargedPrice(item: OrderLine): number {
  return item.chargedPrice ?? item.totalPrice;
}

/** Units of a line still charged and not given back: what can be cancelled from a tab. */
export function cancellableQuantity(item: OrderLine): number {
  return roundQuantity(item.quantity - (item.cancelledQuantity ?? 0) - (item.returnedQuantity ?? 0));
}

/** "1 cancelado", "2 cancelados", "0.25 kg cancelado". */
export function formatCancelled(item: OrderLine): string {
  const cancelled = item.cancelledQuantity ?? 0;
  if (allowsFractions(item.unit)) return `${formatQuantity(cancelled, item.unit)} cancelado`;
  return `${cancelled} cancelado${cancelled === 1 ? '' : 's'}`;
}

/** The lines a bill shows: a dish whose units were all cancelled is left out. */
export function billedItems(order: OrderDto): OrderLine[] {
  return order.items.filter((item) => activeQuantity(item) > 0);
}

/** "2 ×" for pieces, "1.255 kg" for what is weighed or measured: the units the line still charges. */
export function formatTicketQuantity(item: OrderLine): string {
  const quantity = activeQuantity(item);
  return allowsFractions(item.unit) ? formatQuantity(quantity, item.unit) : `${quantity} ×`;
}

export function formatTicketDate(value: string): string {
  return formatInBusinessZone(value, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/**
 * Ticket as plain text, for WhatsApp or any app that shares text. An order of a platform says its folio and the name
 * for the bag; when the platform charged it, it lists the products without prices.
 */
export function buildTicketText(order: OrderDto, extras: TicketExtras): string {
  const platform = getTicketPlatform(order);
  const bagName = platform ? getTicketCustomerName(order) : null;
  const delivery = getTicketDelivery(order);
  const visitCharge = getTicketVisitCharge(order);
  const lines: string[] = [
    `*${extras.businessName}*`,
    `Ticket ${order.orderNumber} · ${formatTicketDate(order.createdAt)}`,
    ...(platform ? [`*${platform.name} · Folio ${platform.folio}*`] : []),
    ...(bagName ? [`Bolsa: ${bagName}`] : []),
    ...(delivery ? [`*A domicilio:* ${delivery.address}`] : []),
    ...(delivery?.references ? [`Referencias: ${delivery.references}`] : []),
    ...(delivery?.courier ? [delivery.courier] : []),
    ...(delivery?.collection ? [`*${delivery.collection}*`] : []),
    ...(extras.provisional ? ['_Folio provisional: venta registrada sin conexión._'] : []),
    '',
    ...billedItems(order).map((item) => {
      const note = item.notes ? `\n   _${item.notes}_` : '';
      const price = platform?.packing ? '' : `  ${formatMoney(chargedPrice(item))}`;
      return `${formatTicketQuantity(item)} ${describeItem(item)}${price}${note}`;
    }),
    ...(delivery ? [`Envío a domicilio  ${formatDeliveryFee(delivery.fee)}`] : []),
    ...(visitCharge !== null ? [`Cargo por visita  ${formatMoney(visitCharge)}`] : []),
    '',
  ];

  if (platform?.packing) {
    lines.push(`Pagado en la app de ${platform.name}`, '', '¡Gracias por tu compra!');
    return lines.join('\n');
  }

  if (order.pricesIncludeTax) {
    lines.push(`*Total: ${formatMoney(order.total)}*`);
    order.taxBreakdown
      .filter((line) => line.taxAmount > 0)
      .forEach((line) => lines.push(`Incluye ${getVatLabel(line)}: ${formatMoney(line.taxAmount)}`));
  } else {
    lines.push(`Subtotal: ${formatMoney(order.subTotal)}`);
    order.taxBreakdown
      .filter((line) => line.taxAmount > 0)
      .forEach((line) => lines.push(`${getVatLabel(line)}: ${formatMoney(line.taxAmount)}`));
    lines.push(`*Total: ${formatMoney(order.total)}*`);
  }

  if (order.payments.length > 0) {
    const { tip, cashReceived, change } = getTicketPaymentTotals(order);
    lines.push('');
    order.payments.forEach((payment) => lines.push(`${getPaymentMethodLabel(payment.method)}: ${formatMoney(payment.amount)}`));
    if (tip > 0) lines.push(`Propina: ${formatMoney(tip)}`);
    if (cashReceived > 0) lines.push(`Efectivo recibido: ${formatMoney(cashReceived)}`);
    if (change > 0) lines.push(`Cambio: ${formatMoney(change)}`);
  }
  // What the business forgave (Fase 26B): without it, the total less the payments is not the balance.
  if ((order.writtenOffAmount ?? 0) > 0) lines.push(`Condonado: ${formatMoney(order.writtenOffAmount ?? 0)}`);
  if (order.balance > 0) {
    // What the business still collects of an order of a platform comes from the courier.
    lines.push(platform ? `*Cobrar al repartidor: ${formatMoney(order.balance)}*` : `Saldo pendiente: ${formatMoney(order.balance)}`);
  }

  lines.push('', '¡Gracias por tu compra!');
  return lines.join('\n');
}

/**
 * WhatsApp link with the ticket. With a phone it opens that chat; without one WhatsApp asks for the
 * contact. Mexican 10-digit numbers get the country code.
 */
export function buildWhatsAppTicketUrl(text: string, phone?: string): string {
  const digits = (phone ?? '').replace(/\D/g, '');
  const target = digits.length === 10 ? `52${digits}` : digits;
  return `https://wa.me/${target}?text=${encodeURIComponent(text)}`;
}

/** Prints only the ticket: the page marks the body while the print dialog is open. */
export function printTicket() {
  document.body.classList.add('ta-printing-ticket');
  const cleanUp = () => {
    document.body.classList.remove('ta-printing-ticket');
    window.removeEventListener('afterprint', cleanUp);
  };
  window.addEventListener('afterprint', cleanUp);
  window.print();
}
