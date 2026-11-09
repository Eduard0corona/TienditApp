import { Link } from 'react-router-dom';
import { getVatLabel, type OrderTotalsEstimate } from '../../utils/orderTotals';
import { formatMoney } from '../../utils/money';
import { MAX_QUANTITY_PER_LINE, type CartLine } from './ordersCartModel';

interface CartSummaryProps {
  cart: CartLine[];
  /** Subtotal, IVA and total as the API will compute them, with the business IVA policy. */
  totals: OrderTotalsEstimate;
  pricesIncludeTax: boolean;
  submitting: boolean;
  onQuantityChange: (productId: string, quantity: number) => void;
  onRemove: (productId: string) => void;
  onSubmit: () => void;
}

/** The cart of Nuevo pedido: its lines with their quantities, the estimated totals and the button that registers it. */
export default function CartSummary({
  cart,
  totals,
  pricesIncludeTax,
  submitting,
  onQuantityChange,
  onRemove,
  onSubmit,
}: CartSummaryProps) {
  return (
    <div className="card">
      <div className="card-body">
        <h4 className="header-title mb-3">Carrito</h4>
        {cart.length === 0 ? (
          <p className="text-muted mb-0">No hay items en el carrito.</p>
        ) : (
          <>
            <div className="table-responsive">
              <table className="table table-sm mb-2">
                <thead>
                  <tr>
                    <th>Item</th>
                    <th className="text-center">Cant.</th>
                    <th className="text-end">Subtotal</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {cart.map((line) => (
                    <tr key={line.productId}>
                      <td>
                        {line.productName}
                        <span className={`badge ms-2 ${line.isService ? 'bg-info' : 'bg-secondary'}`}>
                          {line.isService ? 'Servicio' : 'Producto'}
                        </span>
                      </td>
                      <td className="text-center" style={{ width: 90 }}>
                        <input
                          type="number"
                          min={1}
                          max={MAX_QUANTITY_PER_LINE}
                          step={1}
                          inputMode="numeric"
                          aria-label={`Cantidad de ${line.productName}`}
                          className="form-control form-control-sm text-center"
                          value={line.quantity}
                          onChange={(e) => {
                            // Quantities are whole units in the API; an empty field keeps the line.
                            if (e.target.value.trim() === '') return;
                            const parsed = Number.parseInt(e.target.value, 10);
                            if (Number.isNaN(parsed)) return;
                            onQuantityChange(line.productId, Math.min(parsed, MAX_QUANTITY_PER_LINE));
                          }}
                        />
                      </td>
                      <td className="text-end">
                        {formatMoney(line.unitPrice * line.quantity)}
                      </td>
                      <td className="text-end">
                        <button
                          type="button"
                          className="btn btn-sm btn-soft-danger"
                          aria-label={`Quitar ${line.productName}`}
                          onClick={() => onRemove(line.productId)}
                        >
                          <i className="ri-delete-bin-line" aria-hidden="true"></i>
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {!pricesIncludeTax && (
              <div className="d-flex justify-content-between align-items-center">
                <span>Subtotal</span>
                <span className="ta-money">{formatMoney(totals.subtotal)}</span>
              </div>
            )}
            {totals.breakdown.filter((line) => line.taxAmount > 0).map((line) => (
              <div className="d-flex justify-content-between align-items-center" key={`${line.ratePercent}-${line.isExempt}`}>
                <span>{pricesIncludeTax ? `Incluye ${getVatLabel(line)}` : getVatLabel(line)}</span>
                <span className="ta-money">{formatMoney(line.taxAmount)}</span>
              </div>
            ))}
            <div className="d-flex justify-content-between align-items-center">
              <span className="fw-semibold">Total</span>
              <span className="fw-bold ta-money">{formatMoney(totals.total)}</span>
            </div>
            <small className="text-muted d-block mb-3">
              El total definitivo lo calcula el sistema al generar la orden.
            </small>
          </>
        )}

        <div className="d-grid gap-2">
          <button
            type="button"
            className="btn btn-primary"
            disabled={submitting}
            onClick={onSubmit}
          >
            {submitting ? 'Registrando...' : 'Registrar orden'}
          </button>
          <Link to="/orders" className="btn btn-light">
            Cancelar
          </Link>
        </div>
      </div>
    </div>
  );
}
