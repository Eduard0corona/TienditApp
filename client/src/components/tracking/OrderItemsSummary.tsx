import type { Schemas } from '../../api/typedClient';
import { formatMoney } from '../../utils/money';

type Item = Schemas['CustomerOrderItemDto'];

interface OrderItemsSummaryProps {
  items: Item[];
  deliveryFee?: number | null;
  /** The visit to the customer's address (Fase 22E). */
  visitFee?: number | null;
  pricesIncludeTax: boolean;
  tax: number;
  total: number;
  paidAmount: number;
  /** A cancelled order owes nothing: «Por pagar» is not said. */
  cancelled: boolean;
}

/** What the order carries and what it costs, as the server totals it (Fase 24C). */
export default function OrderItemsSummary({ items, deliveryFee, visitFee, pricesIncludeTax, tax, total, paidAmount, cancelled }: OrderItemsSummaryProps) {
  const toPay = total - paidAmount;
  return (
    <>
      <ul className="list-unstyled mb-3">
        {items.map((item, index) => (
          <li key={`${item.productId}-${index}`} className="d-flex justify-content-between gap-3 py-1">
            <span>
              {item.quantity.toLocaleString('es-MX', { maximumFractionDigits: 3 })} × {item.productName}
              {item.options.length > 0 && <span className="d-block small text-muted">{item.options.join(', ')}</span>}
              {item.notes && <span className="d-block small text-muted">{item.notes}</span>}
            </span>
            <span className="ta-money text-nowrap">{formatMoney(item.amount)}</span>
          </li>
        ))}
      </ul>
      <dl className="mb-0 small">
        {deliveryFee != null && (
          <div className="d-flex justify-content-between">
            <dt className="fw-normal">Envío</dt>
            <dd className="mb-0 ta-money">{deliveryFee > 0 ? formatMoney(deliveryFee) : 'Gratis'}</dd>
          </div>
        )}
        {visitFee != null && (
          <div className="d-flex justify-content-between">
            <dt className="fw-normal">Visita a domicilio</dt>
            <dd className="mb-0 ta-money">{formatMoney(visitFee)}</dd>
          </div>
        )}
        {!pricesIncludeTax && tax > 0 && (
          <div className="d-flex justify-content-between">
            <dt className="fw-normal">IVA</dt>
            <dd className="mb-0 ta-money">{formatMoney(tax)}</dd>
          </div>
        )}
        <div className="d-flex justify-content-between fs-6 fw-semibold mt-1">
          <dt>Total</dt>
          <dd className="mb-0 ta-money">{formatMoney(total)}</dd>
        </div>
        {!cancelled && toPay > 0 && (
          <div className="d-flex justify-content-between text-danger">
            <dt className="fw-normal">Por pagar</dt>
            <dd className="mb-0 ta-money">{formatMoney(toPay)}</dd>
          </div>
        )}
      </dl>
      {!cancelled && toPay <= 0 && total > 0 && (
        <p className="small text-success mb-0 mt-1"><i className="ri-check-line me-1" aria-hidden="true"></i>Pagado</p>
      )}
    </>
  );
}
