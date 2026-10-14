import { describeVisitFee } from '../../utils/homeVisits';

export interface VisitFieldsState {
  atHome: boolean;
  postalCode: string;
  addressLine: string;
  references: string;
}

/** What the visit charges: the zone that reaches the postal code now, or what was agreed at the same address. */
export type VisitQuoteView =
  | { kind: 'none' }
  | { kind: 'loading' }
  | { kind: 'quoted'; zoneName: string; fee: number }
  | { kind: 'agreed'; zoneName: string; fee: number }
  | { kind: 'refused'; message: string };

interface AppointmentVisitFieldsProps {
  value: VisitFieldsState;
  onChange: (changes: Partial<VisitFieldsState>) => void;
  quote: VisitQuoteView;
  pricesIncludeTax: boolean;
  disabled: boolean;
}

/** «En el negocio» or «A domicilio» with the customer's address, and what the visit charges. */
export default function AppointmentVisitFields({ value, onChange, quote, pricesIncludeTax, disabled }: AppointmentVisitFieldsProps) {
  return (
    <fieldset className="col-12 mb-2" disabled={disabled}>
      <legend className="form-label fw-semibold fs-6 mb-1">Dónde</legend>
      <div className="d-flex flex-wrap gap-3 mb-2">
        <div className="form-check">
          <input
            id="appointment-at-business"
            type="radio"
            className="form-check-input"
            name="appointment-place"
            checked={!value.atHome}
            onChange={() => onChange({ atHome: false })}
          />
          <label className="form-check-label" htmlFor="appointment-at-business">En el negocio</label>
        </div>
        <div className="form-check">
          <input
            id="appointment-at-home"
            type="radio"
            className="form-check-input"
            name="appointment-place"
            checked={value.atHome}
            onChange={() => onChange({ atHome: true })}
          />
          <label className="form-check-label" htmlFor="appointment-at-home">A domicilio</label>
        </div>
      </div>
      {value.atHome && (
        <div className="row g-2">
          <div className="col-sm-4">
            <label className="form-label" htmlFor="appointment-postal-code">Código postal *</label>
            <input
              id="appointment-postal-code"
              className="form-control"
              inputMode="numeric"
              autoComplete="postal-code"
              maxLength={5}
              value={value.postalCode}
              onChange={(e) => onChange({ postalCode: e.target.value })}
            />
          </div>
          <div className="col-sm-8">
            <label className="form-label" htmlFor="appointment-address">Dirección *</label>
            <input
              id="appointment-address"
              className="form-control"
              placeholder="Calle, número y colonia"
              maxLength={300}
              autoComplete="street-address"
              value={value.addressLine}
              onChange={(e) => onChange({ addressLine: e.target.value })}
            />
          </div>
          <div className="col-12">
            <label className="form-label" htmlFor="appointment-references">Referencias</label>
            <input
              id="appointment-references"
              className="form-control"
              placeholder="Entre calles, color de la fachada…"
              maxLength={500}
              value={value.references}
              onChange={(e) => onChange({ references: e.target.value })}
            />
          </div>
          <div className="col-12 small" aria-live="polite">
            {quote.kind === 'loading' && <span className="text-muted">Buscando la zona…</span>}
            {(quote.kind === 'quoted' || quote.kind === 'agreed') && (
              <span>
                <i className="ri-map-pin-line me-1" aria-hidden="true"></i>
                Zona {quote.zoneName} · {describeVisitFee(quote.fee, pricesIncludeTax)}
                {quote.kind === 'agreed' && <span className="text-muted"> (lo acordado con el cliente)</span>}
              </span>
            )}
            {quote.kind === 'refused' && <span className="text-danger">{quote.message}</span>}
          </div>
        </div>
      )}
    </fieldset>
  );
}
