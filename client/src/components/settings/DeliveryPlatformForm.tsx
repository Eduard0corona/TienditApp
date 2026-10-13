import { useId, type FormEvent } from 'react';
import { Modal } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { PlatformCommissionBase } from '../../types';
import {
  COMMISSION_VAT_RATES,
  commissionHelp,
  formatPercent,
  platformName,
  type DeliveryPlatformDraft,
  type DraftField,
  type DraftProblem,
  type PlatformSetting,
} from './deliveryPlatformTerms';

interface DeliveryPlatformFormProps {
  platform: PlatformSetting;
  draft: DeliveryPlatformDraft;
  /** What the browser or the API found wrong, with the field it is about when there is one. */
  problem: DraftProblem | null;
  saving: boolean;
  onChange: (draft: DeliveryPlatformDraft) => void;
  onCancel: () => void;
  onSave: () => void;
  /** Where the prices of the products in the app are set, for whoever may open it. */
  pricesPath?: string;
}

/**
 * The terms of one delivery platform: whether the business takes its orders, the commission of its contract and what it
 * withholds when it charges an order. The server checks them and works out the commission of each order with them.
 */
export default function DeliveryPlatformForm({ platform, draft, problem, saving, onChange, onCancel, onSave, pricesPath }: DeliveryPlatformFormProps) {
  const titleId = useId();
  const id = useId();
  const name = platformName(platform.channel);
  const change = (patch: Partial<DeliveryPlatformDraft>) => onChange({ ...draft, ...patch });
  const invalid = (field: DraftField) => problem?.field === field;
  // A rate saved before the three the API takes today still shows as it is: saving it again says what to choose.
  const vatRates = COMMISSION_VAT_RATES.some((rate) => String(rate) === draft.commissionVatPercent)
    ? COMMISSION_VAT_RATES.map(String)
    : [...COMMISSION_VAT_RATES.map(String), draft.commissionVatPercent];

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSave();
  };

  const percentField = (field: DraftField, key: 'commissionPercent' | 'isrWithholdingPercent' | 'vatWithholdingPercent', label: string, help: string) => (
    <>
      <label className="form-label" htmlFor={`${id}-${field}`}>{label}</label>
      <input
        id={`${id}-${field}`}
        className={`form-control ${invalid(field) ? 'is-invalid' : ''}`}
        inputMode="decimal"
        autoComplete="off"
        required={field === 'commission'}
        aria-invalid={invalid(field) ? true : undefined}
        aria-describedby={`${id}-${field}-help`}
        value={draft[key]}
        onChange={(event) => change({ [key]: event.target.value })}
      />
      <div id={`${id}-${field}-help`} className="form-text">{help}</div>
    </>
  );

  return (
    <Modal show onHide={() => !saving && onCancel()} centered scrollable fullscreen="sm-down" aria-labelledby={titleId}>
      {/* A column that may shrink: the body scrolls between the title and the buttons, as in any scrollable dialog. */}
      <form noValidate onSubmit={submit} className="d-flex flex-column overflow-hidden">
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">{name}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {problem && <div className="alert alert-danger py-2" role="alert">{problem.message}</div>}
          <fieldset disabled={saving}>
            <div className="form-check form-switch mb-3">
              <input
                id={`${id}-active`}
                className="form-check-input"
                type="checkbox"
                role="switch"
                checked={draft.isActive}
                onChange={(event) => change({ isActive: event.target.checked })}
              />
              <label className="form-check-label" htmlFor={`${id}-active`}>Recibo pedidos de {name}</label>
            </div>

            <div className="mb-3">
              {percentField('commission', 'commissionPercent', 'Comisión (%)', commissionHelp(platform.channel))}
            </div>

            <div className="mb-3" role="radiogroup" aria-labelledby={`${id}-base-label`} aria-describedby={`${id}-base-help`}>
              <div id={`${id}-base-label`} className="form-label">La comisión se cobra sobre</div>
              <div className="form-check">
                <input
                  id={`${id}-base-total`}
                  className="form-check-input"
                  type="radio"
                  name={`${id}-base`}
                  checked={draft.commissionBase === PlatformCommissionBase.TotalWithTax}
                  onChange={() => change({ commissionBase: PlatformCommissionBase.TotalWithTax })}
                />
                <label className="form-check-label" htmlFor={`${id}-base-total`}>El total del pedido con IVA</label>
              </div>
              <div className="form-check">
                <input
                  id={`${id}-base-subtotal`}
                  className="form-check-input"
                  type="radio"
                  name={`${id}-base`}
                  checked={draft.commissionBase === PlatformCommissionBase.SubTotalWithoutTax}
                  onChange={() => change({ commissionBase: PlatformCommissionBase.SubTotalWithoutTax })}
                />
                <label className="form-check-label" htmlFor={`${id}-base-subtotal`}>El subtotal sin IVA</label>
              </div>
              <div id={`${id}-base-help`} className="form-text">
                DiDi y Rappi la cobran sobre el total con IVA; revisa una línea de tu estado de cuenta.
              </div>
            </div>

            <div className="mb-3">
              <label className="form-label" htmlFor={`${id}-vat`}>IVA de la comisión</label>
              <select
                id={`${id}-vat`}
                className="form-select"
                aria-describedby={`${id}-vat-help`}
                value={draft.commissionVatPercent}
                onChange={(event) => change({ commissionVatPercent: event.target.value })}
              >
                {vatRates.map((rate) => <option key={rate} value={rate}>{formatPercent(Number(rate))}</option>)}
              </select>
              <div id={`${id}-vat-help`} className="form-text">La plataforma te factura su comisión con IVA; tu contador puede acreditarlo.</div>
            </div>

            <h3 className="h6 mt-4 mb-2">Retenciones cuando cobra la plataforma</h3>
            <div className="mb-3">
              {percentField('isr', 'isrWithholdingPercent', 'ISR (%)', '2.5\u00a0% de la venta sin IVA en 2026 si la plataforma tiene tu RFC; 20\u00a0% si no lo tiene.')}
            </div>
            <div>
              {percentField(
                'vatWithholding',
                'vatWithholdingPercent',
                'IVA retenido (% del IVA del pedido)',
                'La mitad del IVA del pedido; todo si la plataforma no tiene tu RFC.',
              )}
            </div>

            <div className="alert alert-warning small mt-3 mb-2">
              Las tasas por omisión son las de 2026 (Ley de Ingresos de la Federación y Ley del IVA): ISR de 2.5{'\u00a0'}% y la
              mitad del IVA. Cambian cada año y dependen de tu caso (persona física o moral, RFC, pago definitivo): confírmalas
              con tu contador antes de usar estas cifras para declarar. Por lo general las retenciones son pagos a cuenta de tus
              impuestos, no un costo.
            </div>
            <div className="alert alert-light border small mb-0">
              Los pedidos de {name} se cobran con los precios que pongas en{' '}
              {pricesPath ? <Link to={pricesPath}>Catálogo › Precios en apps</Link> : 'Catálogo › Precios en apps'}; los productos
              que no tengan uno, con el de tu menú.
            </div>
          </fieldset>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onCancel} disabled={saving}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? 'Guardando…' : 'Guardar'}</button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
