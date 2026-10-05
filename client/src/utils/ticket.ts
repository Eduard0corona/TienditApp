import type { OrderDto } from '../types';
import { getPaymentMethodLabel } from './labels';
import { formatMoney } from './money';
import { getVatLabel } from './orderTotals';
import { describeItem } from './productOptions';
import { allowsFractions, formatQuantity } from './units';

export interface TicketExtras {
  businessName: string;
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

/** "2 ×" for pieces, "1.255 kg" for what is weighed or measured. */
export function formatTicketQuantity(item: OrderDto['items'][number]): string {
  return allowsFractions(item.unit) ? formatQuantity(item.quantity, item.unit) : `${item.quantity} ×`;
}

export function formatTicketDate(value: string): string {
  return new Date(value).toLocaleString('es-MX', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** Ticket as plain text, for WhatsApp or any app that shares text. */
export function buildTicketText(order: OrderDto, extras: TicketExtras): string {
  const lines: string[] = [
    `*${extras.businessName}*`,
    `Ticket ${order.orderNumber} · ${formatTicketDate(order.createdAt)}`,
    ...(extras.provisional ? ['_Folio provisional: venta registrada sin conexión._'] : []),
    '',
    ...order.items.map((item) => {
      const note = item.notes ? `\n   _${item.notes}_` : '';
      return `${formatTicketQuantity(item)} ${describeItem(item)}  ${formatMoney(item.totalPrice)}${note}`;
    }),
    '',
  ];

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
  if (order.balance > 0) lines.push(`Saldo pendiente: ${formatMoney(order.balance)}`);

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
