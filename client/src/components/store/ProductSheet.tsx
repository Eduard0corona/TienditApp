import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import type { PublicMenuProduct } from '../../api/publicMenuApi';
import type { ProductOptionGroupDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { getPriceWithOptions, getSelectionError } from '../../utils/productOptions';
import { MAX_LINE_NOTES, MAX_LINE_QUANTITY } from '../../utils/savedCart';
import { optionGroupsOf } from '../../utils/storeMenu';
import { lineAmount } from '../../utils/units';
import ProductPrice from './ProductPrice';

/** A group that asks for exactly one option starts with its first available one, as the delivery apps do. */
function preselected(groups: ProductOptionGroupDto[]): string[] {
  return groups
    .filter((group) => group.minSelections === 1 && group.maxSelections === 1)
    .flatMap((group) => group.options.filter((option) => option.isAvailable).slice(0, 1).map((option) => option.id));
}

/** What a group asks for, beside its name: «Elige 1», «Elige hasta 2», «Opcional». */
function groupHint(group: ProductOptionGroupDto): string {
  if (group.minSelections > 0) {
    return group.maxSelections === group.minSelections
      ? `Elige ${group.minSelections}`
      : `Elige de ${group.minSelections} a ${group.maxSelections}`;
  }
  return group.maxSelections > 1 ? `Elige hasta ${group.maxSelections}` : 'Opcional';
}

interface ProductSheetProps {
  product: PublicMenuProduct | null;
  /** The menu takes orders now: otherwise the sheet only shows the product. */
  acceptsOrders: boolean;
  onClose: () => void;
  onAdd: (product: PublicMenuProduct, optionIds: string[], notes: string, quantity: number) => void;
}

/**
 * The sheet of a product of the online menu (Fase 24E), as the delivery apps open one: its photo across the top, its
 * name, price, description and allergens, its options with what each group asks for, instructions for this product
 * («sin cebolla») and how many, with the button that adds them and what they cost. Full screen on a phone.
 */
export default function ProductSheet({ product, acceptsOrders, onClose, onAdd }: ProductSheetProps) {
  const titleId = useId();
  return (
    <Modal
      show={!!product}
      onHide={onClose}
      centered
      scrollable
      fullscreen="sm-down"
      dialogClassName="pm-product-dialog"
      contentClassName="pm-product-sheet"
      aria-labelledby={titleId}
    >
      {product && (
        <ProductSheetContent
          key={product.id}
          product={product}
          titleId={titleId}
          canOrder={acceptsOrders && product.availableNow}
          onClose={onClose}
          onAdd={(optionIds, notes, quantity) => onAdd(product, optionIds, notes, quantity)}
        />
      )}
    </Modal>
  );
}

interface ProductSheetContentProps {
  product: PublicMenuProduct;
  titleId: string;
  canOrder: boolean;
  onClose: () => void;
  onAdd: (optionIds: string[], notes: string, quantity: number) => void;
}

function ProductSheetContent({ product, titleId, canOrder, onClose, onAdd }: ProductSheetContentProps) {
  const groups = optionGroupsOf(product);
  const notesId = useId();
  const [optionIds, setOptionIds] = useState<string[]>(() => preselected(groups));
  const [notes, setNotes] = useState('');
  const [quantity, setQuantity] = useState(1);
  const selectionError = getSelectionError(groups, optionIds);
  const unitPrice = getPriceWithOptions(product.price, groups, optionIds);
  const photo = product.imageLargeUrl ?? product.imageUrl;

  const toggleOption = (group: ProductOptionGroupDto, optionId: string) => {
    const inGroup = new Set(group.options.map((option) => option.id));
    const selected = optionIds.includes(optionId);
    if (group.maxSelections === 1) {
      const others = optionIds.filter((id) => !inGroup.has(id));
      setOptionIds(selected && group.minSelections === 0 ? others : [...others, optionId]);
    } else if (selected) {
      setOptionIds(optionIds.filter((id) => id !== optionId));
    } else if (optionIds.filter((id) => inGroup.has(id)).length < group.maxSelections) {
      setOptionIds([...optionIds, optionId]);
    }
  };

  return (
    <>
      <button type="button" className="pm-round pm-sheet-close" onClick={onClose} aria-label="Cerrar">
        <i className="ri-close-line" aria-hidden="true"></i>
      </button>
      <Modal.Body className="pm-product-body">
        {photo && <img className="pm-product-photo" src={photo} alt="" />}
        <div className={`pm-product-info ${photo ? '' : 'no-photo'}`}>
          <h2 id={titleId} className="pm-product-title">{product.name}</h2>
          <ProductPrice price={product.price} compareAtPrice={product.compareAtPrice} />
          {product.description && <p className="pm-product-text">{product.description}</p>}
          {product.allergens && (
            <p className="pm-product-allergens">
              <i className="ri-information-line" aria-hidden="true"></i>
              <span>Alérgenos: {product.allergens}</span>
            </p>
          )}
          {!product.availableNow && <p className="pm-item-note mb-0">No disponible ahora</p>}
        </div>
        {groups.map((group) => (
          <fieldset key={group.id} className="pm-group">
            <legend className="pm-group-head">
              <span className="pm-group-name">{group.name}</span>
              <span className="pm-group-hint">{groupHint(group)}</span>
              {group.minSelections > 0 && <span className="pm-group-badge">Obligatorio</span>}
            </legend>
            {group.options.map((option) => {
              const checked = optionIds.includes(option.id);
              return (
                <label key={option.id} className={`pm-option ${checked ? 'active' : ''} ${option.isAvailable ? '' : 'disabled'}`}>
                  <span className="pm-option-name">{option.name}{!option.isAvailable && ' (agotado)'}</span>
                  {option.priceDelta !== 0 && (
                    <span className="pm-option-price ta-money">
                      {option.priceDelta > 0 ? '+' : '−'}{formatMoney(Math.abs(option.priceDelta))}
                    </span>
                  )}
                  <input
                    type={group.maxSelections === 1 ? 'radio' : 'checkbox'}
                    className="form-check-input"
                    name={`pm-${group.id}`}
                    checked={checked}
                    disabled={!option.isAvailable || !canOrder}
                    onChange={() => toggleOption(group, option.id)}
                  />
                </label>
              );
            })}
          </fieldset>
        ))}
        {canOrder && (
          <div className="pm-group">
            <label className="pm-group-name d-block mb-1" htmlFor={notesId}>Instrucciones especiales</label>
            <textarea
              id={notesId}
              className="form-control"
              rows={2}
              maxLength={MAX_LINE_NOTES}
              placeholder="Ej. sin cebolla, bien dorado"
              value={notes}
              onChange={(event) => setNotes(event.target.value)}
            />
          </div>
        )}
      </Modal.Body>
      {canOrder && (
        <Modal.Footer className="pm-product-footer">
          {selectionError && <p className="pm-product-error" role="status">{selectionError}</p>}
          <div className="pm-stepper" role="group" aria-label={`Cantidad de ${product.name}`}>
            <button type="button" onClick={() => setQuantity(Math.max(1, quantity - 1))} disabled={quantity <= 1} aria-label="Uno menos">
              <i className="ri-subtract-line" aria-hidden="true"></i>
            </button>
            <span className="ta-money" aria-live="polite">{quantity}</span>
            <button
              type="button"
              onClick={() => setQuantity(Math.min(MAX_LINE_QUANTITY, quantity + 1))}
              disabled={quantity >= MAX_LINE_QUANTITY}
              aria-label="Uno más"
            >
              <i className="ri-add-line" aria-hidden="true"></i>
            </button>
          </div>
          <button
            type="button"
            className="btn btn-primary btn-touch pm-product-add"
            disabled={Boolean(selectionError)}
            onClick={() => onAdd(optionIds, notes, quantity)}
          >
            Agregar {quantity} · <span className="ta-money">{formatMoney(lineAmount(unitPrice, quantity))}</span>
          </button>
        </Modal.Footer>
      )}
    </>
  );
}
