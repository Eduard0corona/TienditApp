import { useId } from 'react';
import { Modal } from 'react-bootstrap';
import type { ProductDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { formatQuantity, getUnitShortLabel, lineAmount, parseQuantity } from '../../utils/units';
import { getQuickQuantities, MAX_QUANTITY_PER_LINE, type MeasureRequest } from './sellModel';

interface MeasureModalProps {
  request: MeasureRequest | null;
  onChange: (request: MeasureRequest) => void;
  /** A valid quantity: it goes to the ticket (or replaces the one there). */
  onConfirm: (product: ProductDto, quantity: number) => void;
  onClose: () => void;
}

/** How much of a weighed or measured product: typed, or one of the quick picks of its unit. */
export default function MeasureModal({ request, onChange, onConfirm, onClose }: MeasureModalProps) {
  const titleId = useId();
  const quantity = request ? parseQuantity(request.text, request.product.unit) : null;
  const tooLarge = quantity !== null && quantity > MAX_QUANTITY_PER_LINE;
  const invalid = !!request?.text && (quantity === null || tooLarge);

  return (
    <Modal show={!!request} onHide={onClose} centered aria-labelledby={titleId}>
      {request && (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            if (quantity === null || tooLarge) return;
            onConfirm(request.product, quantity);
          }}
        >
          <Modal.Header closeButton>
            <Modal.Title id={titleId} as="h2" className="h5">{request.product.name}</Modal.Title>
          </Modal.Header>
          <Modal.Body>
            <label className="form-label" htmlFor="sell-measure">
              ¿Cuánto? <span className="text-muted">({formatMoney(request.product.priceAmount)}/{getUnitShortLabel(request.product.unit)})</span>
            </label>
            <div className="input-group input-group-lg">
              <input
                id="sell-measure"
                className={`form-control ta-money ${invalid ? 'is-invalid' : ''}`}
                inputMode="decimal"
                autoComplete="off"
                autoFocus
                placeholder="0.000"
                value={request.text}
                onChange={(event) => onChange({ ...request, text: event.target.value })}
                aria-describedby="sell-measure-help"
              />
              <span className="input-group-text">{getUnitShortLabel(request.product.unit)}</span>
            </div>
            <div id="sell-measure-help" className={invalid ? 'invalid-feedback d-block' : 'form-text'}>
              {tooLarge
                ? `Máximo ${MAX_QUANTITY_PER_LINE.toLocaleString('es-MX')} por producto.`
                : request.text && quantity === null
                  ? 'Escribe una cantidad mayor a cero, con hasta 3 decimales.'
                  : quantity !== null
                    ? `Importe: ${formatMoney(lineAmount(request.product.priceAmount, quantity))}`
                    : 'Hasta 3 decimales.'}
            </div>
            <div className="sell-measure-picks mt-3" role="group" aria-label="Cantidades rápidas">
              {getQuickQuantities(request.product.unit).map((pick) => (
                <button
                  key={pick}
                  type="button"
                  className="btn btn-light btn-touch"
                  onClick={() => onChange({ ...request, text: String(pick) })}
                >
                  {formatQuantity(pick, request.product.unit)}
                </button>
              ))}
            </div>
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light" onClick={onClose}>Cancelar</button>
            <button type="submit" className="btn btn-primary" disabled={quantity === null || tooLarge}>
              {request.replacing ? 'Cambiar cantidad' : 'Agregar'}
            </button>
          </Modal.Footer>
        </form>
      )}
    </Modal>
  );
}
