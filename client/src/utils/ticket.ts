import type { OrderDto } from '../types';
import { getPaymentMethodLabel } from './labels';
import { formatMoney } from './money';
import { getVatLabel } from './orderTotals';

export interface TicketExtras {
  businessName: string;
  /** Cash handed over and change, known only at the counter right after charging. */
  cashReceived?: number;
  change?: number;
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
    '',
    ...order.items.map((item) => {
      const note = item.notes ? `\n   _${item.notes}_` : '';
      return `${item.quantity} × ${item.productName}  ${formatMoney(item.totalPrice)}${note}`;
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
    lines.push('');
    order.payments.forEach((payment) => lines.push(`${getPaymentMethodLabel(payment.method)}: ${formatMoney(payment.amount)}`));
    if (extras.change && extras.change > 0) lines.push(`Cambio: ${formatMoney(extras.change)}`);
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
