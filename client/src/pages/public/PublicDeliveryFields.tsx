import { useId } from 'react';
import {
  ADDRESS_LINE_MAX_LENGTH,
  REFERENCES_MAX_LENGTH,
  describeDeliveryTerms,
  type DeliveryTerms,
} from '../../utils/homeDelivery';

export interface PublicDeliveryAddress {
  postalCode: string;
  addressLine: string;
  references: string;
}

interface PublicDeliveryFieldsProps {
  /** Only to the address: a kitchen without premises with delivery zones. */
  required: boolean;
  toAddress: boolean;
  onToAddressChange: (toAddress: boolean) => void;
  address: PublicDeliveryAddress;
  onAddressChange: (changes: Partial<PublicDeliveryAddress>) => void;
  /** What the business charges to the postal code written, once the server answered that it delivers there. */
  terms: DeliveryTerms | null;
  /** The server is checking the postal code. */
  checking: boolean;
  /** Why the order cannot go there yet (not covered, below the minimum, not a postal code), or ''. */
  problem: string;
  /** The fee goes like the prices of the business: «más IVA» when they do not include it. */
  pricesIncludeTax: boolean;
}

/**
 * How the customer of the online menu gets the order (Fase 22C): to their address, with the postal code the business's
 * zones are checked by, or picked up. What the delivery costs is the server's answer for that postal code.
 */
export default function PublicDeliveryFields({
  required, toAddress, onToAddressChange, address, onAddressChange, terms, checking, problem, pricesIncludeTax,
}: PublicDeliveryFieldsProps) {
  const id = useId();
  const delivering = required || toAddress;

  return (
    <div className="mb-3">
      {required ? (
        <p className="small text-muted mb-2">
          <i className="ri-e-bike-2-line me-1" aria-hidden="true"></i>Te lo llevamos a domicilio.
        </p>
      ) : (
        <>
          <div id={`${id}-how`} className="small fw-semibold mb-1">¿Cómo lo quieres?</div>
          <div className="d-flex gap-2 mb-2" role="radiogroup" aria-labelledby={`${id}-how`}>
            {[true, false].map((option) => (
              <button
                key={String(option)}
                type="button"
                role="radio"
                aria-checked={toAddress === option}
                className={`btn btn-touch flex-fill ${toAddress === option ? 'btn-primary' : 'btn-outline-secondary'}`}
                onClick={() => onToAddressChange(option)}
              >
                {option ? 'A domicilio' : 'Paso por él'}
              </button>
            ))}
          </div>
        </>
      )}
      {delivering && (
        <div className="row g-2">
          <div className="col-6 col-sm-4">
            <label className="form-label small mb-1" htmlFor={`${id}-postal-code`}>Código postal</label>
            <input
              id={`${id}-postal-code`}
              className={`form-control ${problem && !checking ? 'is-invalid' : ''}`}
              required
              inputMode="numeric"
              autoComplete="postal-code"
              maxLength={5}
              pattern="\d{5}"
              value={address.postalCode}
              onChange={(event) => onAddressChange({ postalCode: event.target.value.replace(/\D/g, '') })}
              aria-describedby={`${id}-quote`}
            />
          </div>
          <div className="col-12">
            <label className="form-label small mb-1" htmlFor={`${id}-address`}>Dirección</label>
            <input
              id={`${id}-address`}
              className="form-control"
              required
              maxLength={ADDRESS_LINE_MAX_LENGTH}
              autoComplete="street-address"
              placeholder="Calle, número y colonia"
              value={address.addressLine}
              onChange={(event) => onAddressChange({ addressLine: event.target.value })}
            />
          </div>
          <div className="col-12">
            <label className="form-label small mb-1" htmlFor={`${id}-references`}>Referencias (opcional)</label>
            <input
              id={`${id}-references`}
              className="form-control"
              maxLength={REFERENCES_MAX_LENGTH}
              placeholder="Ej. portón verde, entre qué calles"
              value={address.references}
              onChange={(event) => onAddressChange({ references: event.target.value })}
            />
          </div>
          <div id={`${id}-quote`} className="col-12 small" role="status">
            {checking ? <span className="text-muted">Revisando si llegamos…</span>
              : problem ? <span className="text-danger">{problem}</span>
              : terms ? <span className="text-muted">{describeDeliveryTerms(terms, pricesIncludeTax)}</span>
              : <span className="text-muted">Escribe tu código postal para ver el envío.</span>}
          </div>
        </div>
      )}
    </div>
  );
}
