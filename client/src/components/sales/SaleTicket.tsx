import type { OrderDto } from '../../types';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import { getVatLabel } from '../../utils/orderTotals';
import { formatTicketDate, formatTicketQuantity, getTicketPaymentTotals, type TicketExtras } from '../../utils/ticket';

interface SaleTicketProps extends TicketExtras {
  order: OrderDto;
}

/** Customer ticket: what was bought, the IVA breakdown computed by the server and how it was paid. */
export default function SaleTicket({ order, businessName, provisional }: SaleTicketProps) {
  const taxLines = order.taxBreakdown.filter((line) => line.taxAmount > 0);
  const { tip, cashReceived, change } = getTicketPaymentTotals(order);
  const showCustomer = order.customerName && order.customerName !== 'Público en general';

  return (
    <div className="ta-ticket ta-print-area" aria-label={`Ticket ${order.orderNumber}`}>
      <div className="text-center mb-2">
        <div className="ta-ticket-business">{businessName}</div>
        <div className="small">Ticket {order.orderNumber}</div>
        {provisional && <div className="small">Folio provisional: venta registrada sin conexión</div>}
        <div className="small">{formatTicketDate(order.createdAt)}</div>
        {order.tableNumber && <div className="small">Mesa/Ref.: {order.tableNumber}</div>}
        {showCustomer && <div className="small">Cliente: {order.customerName}</div>}
      </div>

      <table className="ta-ticket-lines">
        <tbody>
          {order.items.map((item) => (
            <tr key={item.id}>
              <td className="pe-1 align-top text-nowrap">{formatTicketQuantity(item)}</td>
              <td className="w-100">
                {item.productName}
                {item.options && item.options.length > 0 && (
                  <div className="ta-ticket-note">{item.options.map((option) => option.optionName).join(', ')}</div>
                )}
                {item.notes && <div className="ta-ticket-note">{item.notes}</div>}
              </td>
              <td className="text-end align-top ta-money">{formatMoney(item.totalPrice)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="ta-ticket-totals">
        {order.pricesIncludeTax ? (
          <>
            <div className="ta-ticket-row ta-ticket-total">
              <span>Total</span>
              <span className="ta-money">{formatMoney(order.total)}</span>
            </div>
            {taxLines.map((line) => (
              <div className="ta-ticket-row small" key={`${line.ratePercent}-${line.isExempt}`}>
                <span>Incluye {getVatLabel(line)}</span>
                <span className="ta-money">{formatMoney(line.taxAmount)}</span>
              </div>
            ))}
          </>
        ) : (
          <>
            <div className="ta-ticket-row">
              <span>Subtotal</span>
              <span className="ta-money">{formatMoney(order.subTotal)}</span>
            </div>
            {taxLines.map((line) => (
              <div className="ta-ticket-row" key={`${line.ratePercent}-${line.isExempt}`}>
                <span>{getVatLabel(line)}</span>
                <span className="ta-money">{formatMoney(line.taxAmount)}</span>
              </div>
            ))}
            <div className="ta-ticket-row ta-ticket-total">
              <span>Total</span>
              <span className="ta-money">{formatMoney(order.total)}</span>
            </div>
          </>
        )}
      </div>

      {order.payments.length > 0 && (
        <div className="ta-ticket-totals">
          {order.payments.map((payment) => (
            <div className="ta-ticket-row" key={payment.id}>
              <span>{getPaymentMethodLabel(payment.method)}</span>
              <span className="ta-money">{formatMoney(payment.amount)}</span>
            </div>
          ))}
          {tip > 0 && (
            <div className="ta-ticket-row">
              <span>Propina</span>
              <span className="ta-money">{formatMoney(tip)}</span>
            </div>
          )}
          {cashReceived > 0 && (
            <div className="ta-ticket-row small">
              <span>Efectivo recibido</span>
              <span className="ta-money">{formatMoney(cashReceived)}</span>
            </div>
          )}
          {change > 0 && (
            <div className="ta-ticket-row fw-bold">
              <span>Cambio</span>
              <span className="ta-money">{formatMoney(change)}</span>
            </div>
          )}
        </div>
      )}
      {order.balance > 0 && (
        <div className="ta-ticket-row fw-bold">
          <span>Saldo pendiente</span>
          <span className="ta-money">{formatMoney(order.balance)}</span>
        </div>
      )}

      <div className="text-center small mt-2">¡Gracias por tu compra!</div>
    </div>
  );
}
