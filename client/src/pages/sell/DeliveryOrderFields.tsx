import { useId } from 'react';
import {
  ADDRESS_LINE_MAX_LENGTH,
  REFERENCES_MAX_LENGTH,
  describeDeliveryTerms,
  type DeliveryZoneTerms,
} from '../../utils/homeDelivery';
import { NO_DELIVERY_ADDRESS, type SaleDelivery } from './sellModel';

interface DeliveryOrderFieldsProps {
  /** Where the order goes, or null while it is not sent to an address. */
  delivery: SaleDelivery | null;
  onChange: (delivery: SaleDelivery | null) => void;
  /** The active zone of the business that reaches the postal code written. */
  zone: DeliveryZoneTerms | null;
  /** Why the order cannot go there (not covered, below the minimum, no address), once there is something to say. */
  problem: string;
  disabled: boolean;
  /** An order sent to an address is registered with connection only (its fee comes from the zones). */
  online: boolean;
}

/**
 * Vender's switch for an order that goes to the customer's address (Fase 22C), with the postal code its zone is found
 * by, the address and references. The zone's fee and minimum show as typed; the server charges them.
 */
export default function DeliveryOrderFields({ delivery, onChange, zone, problem, disabled, online }: DeliveryOrderFieldsProps) {
  // The ticket shows twice (beside the catalog, and in the sheet of a phone): each copy names its own fields.
  const id = useId();
  const update = (changes: Partial<SaleDelivery>) => onChange({ ...(delivery ?? NO_DELIVERY_ADDRESS), ...changes });

  return (
    <div className="mb-2">
      <div className="form-check form-switch">
        <input
          id={`${id}-to-address`}
          className="form-check-input"
          type="checkbox"
          role="switch"
          checked={delivery !== null}
          disabled={disabled}
          onChange={(event) => onChange(event.target.checked ? NO_DELIVERY_ADDRESS : null)}
        />
        <label className="form-check-label" htmlFor={`${id}-to-address`}>
          <i className="ri-e-bike-2-line me-1" aria-hidden="true"></i>A domicilio
        </label>
      </div>
      {delivery && (
        <div className="sell-details mt-2">
          <div className="row g-2">
            <div className="col-5">
              <label className="form-label small mb-1" htmlFor={`${id}-postal-code`}>Código postal</label>
              <input
                id={`${id}-postal-code`}
                className="form-control"
                inputMode="numeric"
                autoComplete="off"
                maxLength={5}
                value={delivery.postalCode}
                disabled={disabled}
                onChange={(event) => update({ postalCode: event.target.value.replace(/\D/g, '') })}
                aria-describedby={`${id}-zone`}
              />
            </div>
            <div className="col-12">
              <label className="form-label small mb-1" htmlFor={`${id}-address`}>Dirección</label>
              <input
                id={`${id}-address`}
                className="form-control"
                maxLength={ADDRESS_LINE_MAX_LENGTH}
                autoComplete="off"
                placeholder="Calle, número y colonia"
                value={delivery.addressLine}
                disabled={disabled}
                onChange={(event) => update({ addressLine: event.target.value })}
              />
            </div>
            <div className="col-12">
              <label className="form-label small mb-1" htmlFor={`${id}-references`}>Referencias (opcional)</label>
              <input
                id={`${id}-references`}
                className="form-control"
                maxLength={REFERENCES_MAX_LENGTH}
                autoComplete="off"
                value={delivery.references}
                disabled={disabled}
                onChange={(event) => update({ references: event.target.value })}
              />
            </div>
            <div id={`${id}-zone`} className="col-12 small" role="status">
              {problem ? <span className="text-danger">{problem}</span>
                : zone ? <span className="text-muted">{zone.name} · {describeDeliveryTerms(zone)}</span>
                : <span className="text-muted">El código postal dice qué zona lo lleva y cuánto cobra.</span>}
            </div>
          </div>
          {!online && (
            <p className="small text-warning-emphasis mt-2 mb-0">
              <i className="ri-wifi-off-line me-1" aria-hidden="true"></i>
              Sin conexión no se registran pedidos a domicilio: espera a que vuelva.
            </p>
          )}
        </div>
      )}
    </div>
  );
}
