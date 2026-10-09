import type { SaleDetails } from './sellModel';

interface SaleDetailsFormProps {
  details: SaleDetails;
  onChange: (changes: Partial<SaleDetails>) => void;
  open: boolean;
  onToggle: () => void;
  /** Food businesses take the table or a reference. */
  isFood: boolean;
  phoneError: string;
}

/** Customer, table and notes of the sale, behind a link: a counter sale goes to the general public. */
export default function SaleDetailsForm({ details, onChange, open, onToggle, isFood, phoneError }: SaleDetailsFormProps) {
  return (
    <>
      <button
        type="button"
        className="btn btn-link btn-sm px-0 mb-2"
        onClick={onToggle}
        aria-expanded={open}
      >
        <i className={`${open ? 'ri-arrow-up-s-line' : 'ri-user-add-line'} me-1`} aria-hidden="true"></i>
        {open ? 'Ocultar datos del cliente' : 'Cliente, mesa y notas (opcional)'}
      </button>
      {open && (
        <div className="sell-details mb-2">
          <div className="row g-2">
            <div className="col-sm-6">
              <label className="form-label small mb-1" htmlFor="sell-customer-name">Nombre</label>
              <input
                id="sell-customer-name"
                className="form-control"
                maxLength={200}
                placeholder="Público en general"
                value={details.customerName}
                onChange={(event) => onChange({ customerName: event.target.value })}
              />
            </div>
            <div className="col-sm-6">
              <label className="form-label small mb-1" htmlFor="sell-customer-phone">Teléfono</label>
              <input
                id="sell-customer-phone"
                type="tel"
                inputMode="tel"
                autoComplete="tel"
                className={`form-control ${phoneError ? 'is-invalid' : ''}`}
                placeholder="55 1234 5678"
                value={details.customerPhone}
                onChange={(event) => onChange({ customerPhone: event.target.value })}
                aria-describedby="sell-customer-phone-help"
              />
              <div id="sell-customer-phone-help" className={phoneError ? 'invalid-feedback d-block' : 'form-text'}>
                {phoneError || 'Con teléfono la venta queda en su historial.'}
              </div>
            </div>
            {isFood && (
              <div className="col-sm-6">
                <label className="form-label small mb-1" htmlFor="sell-table">Mesa o referencia</label>
                <input
                  id="sell-table"
                  className="form-control"
                  maxLength={50}
                  value={details.tableNumber}
                  onChange={(event) => onChange({ tableNumber: event.target.value })}
                />
              </div>
            )}
            <div className={isFood ? 'col-sm-6' : 'col-12'}>
              <label className="form-label small mb-1" htmlFor="sell-notes">Notas del pedido</label>
              <input
                id="sell-notes"
                className="form-control"
                maxLength={1000}
                value={details.orderNotes}
                onChange={(event) => onChange({ orderNotes: event.target.value })}
              />
            </div>
          </div>
        </div>
      )}
    </>
  );
}
