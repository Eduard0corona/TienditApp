import { formatMoney } from '../../utils/money';
import { formatQuantity, getQuantityStep, getUnitShortLabel, lineAmount } from '../../utils/units';
import type { CartLine } from './sellModel';

interface CartLinesProps {
  cart: CartLine[];
  /** What the cashier is typing in a quantity box ("1." on the way to "1.5"), until the box loses focus. */
  quantityDrafts: Record<string, string>;
  onChangeQuantity: (key: string, quantity: number) => void;
  onTypeQuantity: (line: CartLine, text: string) => void;
  onFinishTypingQuantity: (key: string) => void;
  onUpdateLine: (key: string, changes: Partial<CartLine>) => void;
}

/** The lines of the ticket: price, a stepper (a tenth of a kilo or one piece) or a typed quantity, and a note. */
export default function CartLines({ cart, quantityDrafts, onChangeQuantity, onTypeQuantity, onFinishTypingQuantity, onUpdateLine }: CartLinesProps) {
  return (
    <ul className="sell-lines list-unstyled mb-2">
      {cart.map((line) => {
        const step = getQuantityStep(line.unit);
        const unitLabel = getUnitShortLabel(line.unit);
        const removesLine = line.quantity - step <= 0;
        const stepLabel = line.fractional ? `${formatQuantity(step, line.unit)} de` : 'uno de';
        return (
          <li key={line.key} className="sell-line">
            <div className="d-flex justify-content-between gap-2">
              <div className="sell-line-name">
                {line.name}
                {line.optionsLabel && <div className="small sell-line-options">{line.optionsLabel}</div>}
                <div className="small text-muted">
                  {line.fractional
                    ? `${formatQuantity(line.quantity, line.unit)} × ${formatMoney(line.unitPrice)}/${unitLabel}`
                    : `${formatMoney(line.unitPrice)} c/u`}
                </div>
              </div>
              <strong className="ta-money">{formatMoney(lineAmount(line.unitPrice, line.quantity))}</strong>
            </div>
            <div className="d-flex align-items-center justify-content-between gap-2 mt-1">
              <div className="sell-stepper" role="group" aria-label={`Cantidad de ${line.name}`}>
                <button
                  type="button"
                  className="btn btn-light"
                  aria-label={removesLine ? `Quitar ${line.name}` : `Quitar ${stepLabel} ${line.name}`}
                  onClick={() => onChangeQuantity(line.key, line.quantity - step)}
                >
                  <i className={removesLine ? 'ri-delete-bin-line' : 'ri-subtract-line'} aria-hidden="true"></i>
                </button>
                <input
                  className={`form-control text-center ta-money ${line.fractional ? 'sell-quantity-measured' : ''}`}
                  inputMode={line.fractional ? 'decimal' : 'numeric'}
                  aria-label={line.fractional ? `Cantidad de ${line.name} en ${unitLabel}` : `Cantidad de ${line.name}`}
                  value={quantityDrafts[line.key] ?? String(line.quantity)}
                  onChange={(event) => onTypeQuantity(line, event.target.value)}
                  onBlur={() => onFinishTypingQuantity(line.key)}
                />
                <button
                  type="button"
                  className="btn btn-light"
                  aria-label={`Agregar ${stepLabel} ${line.name}`}
                  onClick={() => onChangeQuantity(line.key, line.quantity + step)}
                >
                  <i className="ri-add-line" aria-hidden="true"></i>
                </button>
              </div>
              <button
                type="button"
                className={`btn btn-sm ${line.showNotes ? 'btn-soft-warning' : 'btn-light'}`}
                onClick={() => onUpdateLine(line.key, { showNotes: !line.showNotes })}
                aria-expanded={line.showNotes}
              >
                <i className="ri-sticky-note-line me-1" aria-hidden="true"></i>Nota
              </button>
            </div>
            {line.showNotes && (
              <input
                className="form-control form-control-sm mt-2"
                placeholder="Ej. sin cebolla, para llevar"
                aria-label={`Nota para ${line.name}`}
                maxLength={300}
                value={line.notes}
                onChange={(event) => onUpdateLine(line.key, { notes: event.target.value })}
              />
            )}
          </li>
        );
      })}
    </ul>
  );
}
