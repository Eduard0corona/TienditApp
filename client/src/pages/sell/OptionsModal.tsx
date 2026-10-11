import { useId } from 'react';
import { Modal } from 'react-bootstrap';
import type { ProductDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { getPriceWithOptions, getSelectionError } from '../../utils/productOptions';
import { allowsFractions, getUnitShortLabel, lineAmount, parseQuantity } from '../../utils/units';
import type { OptionsRequest } from './sellModel';

interface OptionsModalProps {
  request: OptionsRequest | null;
  onChange: (request: OptionsRequest) => void;
  /** The options and the quantity are valid: the line goes to the ticket. */
  onConfirm: (product: ProductDto, optionIds: string[], quantity: number) => void;
  onClose: () => void;
}

/** Variants and extras of a product, and how many, before it goes to the ticket. */
export default function OptionsModal({ request, onChange, onConfirm, onClose }: OptionsModalProps) {
  const titleId = useId();
  const quantity = request ? parseQuantity(request.quantityText, request.product.unit) : null;
  const choiceError = request ? getSelectionError(request.product.optionGroups, request.optionIds) : null;

  const toggleOption = (groupId: string, optionId: string) => {
    if (!request) return;
    const group = request.product.optionGroups?.find((item) => item.id === groupId);
    if (!group) return;
    const inGroup = new Set(group.options.map((option) => option.id));
    const selected = request.optionIds.includes(optionId);
    let optionIds: string[];
    if (group.maxSelections === 1) {
      // A single choice works like radio buttons (an optional one can be cleared by tapping it again).
      const others = request.optionIds.filter((id) => !inGroup.has(id));
      optionIds = selected && group.minSelections === 0 ? others : [...others, optionId];
    } else if (selected) {
      optionIds = request.optionIds.filter((id) => id !== optionId);
    } else {
      const pickedInGroup = request.optionIds.filter((id) => inGroup.has(id)).length;
      if (pickedInGroup >= group.maxSelections) return;
      optionIds = [...request.optionIds, optionId];
    }
    onChange({ ...request, optionIds });
  };

  return (
    <Modal show={!!request} onHide={onClose} centered scrollable fullscreen="sm-down" aria-labelledby={titleId}>
      {request && (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            if (choiceError || quantity === null) return;
            onConfirm(request.product, request.optionIds, quantity);
          }}
        >
          <Modal.Header closeButton>
            <Modal.Title id={titleId} as="h2" className="h5">{request.product.name}</Modal.Title>
          </Modal.Header>
          <Modal.Body>
            {(request.product.optionGroups ?? []).map((group) => {
              const single = group.maxSelections === 1;
              const pickedInGroup = group.options.filter((option) => request.optionIds.includes(option.id)).length;
              return (
                <fieldset key={group.id} className="mb-3">
                  <legend className="fs-6 fw-semibold mb-1">
                    {group.name}{' '}
                    <span className="small text-muted fw-normal">
                      {group.minSelections > 0
                        ? single ? '· obligatorio' : `· elige ${group.minSelections} a ${group.maxSelections}`
                        : single ? '· opcional' : `· hasta ${group.maxSelections}`}
                    </span>
                  </legend>
                  <div className="sell-option-list">
                    {group.options.map((option) => {
                      const checked = request.optionIds.includes(option.id);
                      const full = !single && !checked && pickedInGroup >= group.maxSelections;
                      return (
                        <label key={option.id} className={`sell-option ${checked ? 'active' : ''} ${!option.isAvailable || full ? 'disabled' : ''}`}>
                          <input
                            type={single ? 'radio' : 'checkbox'}
                            className="form-check-input me-2"
                            name={`option-${group.id}`}
                            checked={checked}
                            disabled={!option.isAvailable || full}
                            onChange={() => toggleOption(group.id, option.id)}
                            onClick={() => {
                              // Tapping the chosen option of an optional single choice clears it.
                              if (single && checked && group.minSelections === 0) toggleOption(group.id, option.id);
                            }}
                          />
                          <span className="flex-grow-1">{option.name}{!option.isAvailable && ' (agotado)'}</span>
                          {option.priceDelta !== 0 && (
                            <span className="ta-money small">{option.priceDelta > 0 ? '+' : '−'}{formatMoney(Math.abs(option.priceDelta))}</span>
                          )}
                        </label>
                      );
                    })}
                  </div>
                </fieldset>
              );
            })}
            <label className="form-label" htmlFor="sell-choice-quantity">
              {allowsFractions(request.product.unit) ? `Cantidad (${getUnitShortLabel(request.product.unit)})` : 'Cantidad'}
            </label>
            <input
              id="sell-choice-quantity"
              className={`form-control ta-money ${request.quantityText && quantity === null ? 'is-invalid' : ''}`}
              style={{ maxWidth: '8rem' }}
              inputMode={allowsFractions(request.product.unit) ? 'decimal' : 'numeric'}
              value={request.quantityText}
              onChange={(event) => onChange({ ...request, quantityText: event.target.value })}
            />
            {choiceError && <div className="text-danger small mt-2" role="status">{choiceError}</div>}
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light" onClick={onClose}>Cancelar</button>
            <button type="submit" className="btn btn-primary" disabled={Boolean(choiceError) || quantity === null}>
              Agregar · {formatMoney(lineAmount(
                getPriceWithOptions(request.product.priceAmount, request.product.optionGroups, request.optionIds),
                quantity ?? 0,
              ))}
            </button>
          </Modal.Footer>
        </form>
      )}
    </Modal>
  );
}
