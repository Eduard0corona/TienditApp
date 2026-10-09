import { useState } from 'react';
import { KitchenTicketStatus, type CancelTabItemRequest, type OrderItemDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { describeItem } from '../../utils/productOptions';
import { cancellableQuantity } from '../../utils/ticket';
import { allowsFractions, formatQuantity, parseQuantity } from '../../utils/units';

/** Common reasons, one tap away; the waiter can still write their own. */
const CANCEL_ITEM_REASONS = [
  'Se capturó por error',
  'El cliente ya no lo quiso',
  'Tardó demasiado',
  'Salió mal de la cocina',
];

interface CancelTabItemFormProps {
  item: OrderItemDto;
  /** Where its round is in the kitchen: a dish still waiting there goes back to stock by default. */
  roundStatus?: KitchenTicketStatus;
  submitting: boolean;
  onSubmit: (request: Omit<CancelTabItemRequest, 'orderItemId'>) => void;
  onBack: () => void;
}

/**
 * Cancels units of a dish already sent to the kitchen: how many, why, and whether its supplies go back to stock (not
 * when the kitchen already prepared it). The API decides how much the tab goes down.
 */
export default function CancelTabItemForm({ item, roundStatus, submitting, onSubmit, onBack }: CancelTabItemFormProps) {
  const available = cancellableQuantity(item);
  const fractional = allowsFractions(item.unit);
  const [quantityText, setQuantityText] = useState(String(fractional ? available : Math.min(1, available)));
  const [reason, setReason] = useState('');
  const [returnToStock, setReturnToStock] = useState(roundStatus === undefined || roundStatus === KitchenTicketStatus.Pending);

  const quantity = parseQuantity(quantityText, item.unit);
  const quantityValid = quantity !== null && quantity <= available;
  const reasonValid = reason.trim().length >= 3;
  const canSubmit = !submitting && quantityValid && reasonValid;

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (!canSubmit || quantity === null) return;
    onSubmit({ quantity, reason: reason.trim(), returnToStock });
  };

  return (
    <form onSubmit={submit} noValidate>
      <p className="mb-2">
        Cancelar <strong>{describeItem(item)}</strong>{' '}
        <span className="text-muted">({formatQuantity(available, item.unit)} en la cuenta, {formatMoney(item.unitPrice)} c/u)</span>
      </p>

      <div className="mb-3">
        <label className="form-label" htmlFor="cancel-item-quantity">Cantidad a cancelar</label>
        <input
          id="cancel-item-quantity"
          className={`form-control${quantityValid ? '' : ' is-invalid'}`}
          inputMode={fractional ? 'decimal' : 'numeric'}
          value={quantityText}
          onChange={(event) => setQuantityText(event.target.value)}
          aria-describedby="cancel-item-quantity-help"
        />
        <div id="cancel-item-quantity-help" className={quantityValid ? 'form-text' : 'invalid-feedback'}>
          {quantityValid ? 'Lo cancelado deja de cobrarse.' : `Escribe una cantidad de hasta ${formatQuantity(available, item.unit)}.`}
        </div>
      </div>

      <div className="mb-2">
        <label className="form-label" htmlFor="cancel-item-reason">Motivo</label>
        <input
          id="cancel-item-reason"
          className="form-control"
          maxLength={500}
          placeholder="Ej. el cliente ya no lo quiso"
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          autoFocus
        />
      </div>
      <div className="d-flex flex-wrap gap-2 mb-3" role="group" aria-label="Motivos frecuentes">
        {CANCEL_ITEM_REASONS.map((option) => (
          <button
            key={option}
            type="button"
            className={`btn btn-sm ${reason === option ? 'btn-secondary' : 'btn-outline-secondary'}`}
            onClick={() => setReason(option)}
          >
            {option}
          </button>
        ))}
      </div>

      <div className="form-check form-switch mb-1">
        <input
          id="cancel-item-restock"
          type="checkbox"
          role="switch"
          className="form-check-input"
          checked={returnToStock}
          onChange={(event) => setReturnToStock(event.target.checked)}
        />
        <label className="form-check-label" htmlFor="cancel-item-restock">Regresar sus insumos al inventario</label>
      </div>
      <div className="form-text mb-3">Desmárcalo si la cocina ya lo preparó: lo que usó se da por perdido.</div>

      <div className="d-flex justify-content-end gap-2">
        <button type="button" className="btn btn-light" onClick={onBack} disabled={submitting}>Volver</button>
        <button type="submit" className="btn btn-danger" disabled={!canSubmit}>
          {submitting ? 'Cancelando…' : 'Cancelar platillo'}
        </button>
      </div>
    </form>
  );
}
