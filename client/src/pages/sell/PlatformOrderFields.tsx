import { useId } from 'react';
import { PlatformCollectedBy } from '../../types';
import {
  PLATFORM_FOLIO_MAX_LENGTH,
  PLATFORM_ORDER_ID_MAX_INPUT_LENGTH,
  getPlatformFolioHint,
  getPlatformName,
  getPlatformOrderIdHelp,
  type DeliveryPlatform,
} from '../../utils/deliveryPlatforms';
import type { SaleDetails } from './sellModel';

interface PlatformOrderFieldsProps {
  platform: DeliveryPlatform;
  details: SaleDetails;
  onChange: (changes: Partial<SaleDetails>) => void;
  /** What is wrong with the folio, once there is something to say (the server checks it again). */
  folioError: string;
  /** What is wrong with the optional long id, as typed (the server checks it again). */
  orderIdError: string;
  disabled: boolean;
  /** Whoever cannot charge gets no «charge it» help: under the totals the ticket says the cashier does. */
  canCharge: boolean;
}

/**
 * What an order of a delivery platform needs, in sight without opening anything: the folio of its app (focused as the
 * platform is chosen, with the keyboard that platform's folio takes), its long id when the cashier has it (optional: its
 * statement names the order by it), who charged it and the name for the bag.
 */
export default function PlatformOrderFields({
  platform,
  details,
  onChange,
  folioError,
  orderIdError,
  disabled,
  canCharge,
}: PlatformOrderFieldsProps) {
  // The ticket shows twice (beside the catalog, and in the sheet of a phone): each copy names its own fields.
  const id = useId();
  const collectedLabelId = `${id}-collected`;
  const hint = getPlatformFolioHint(platform);
  const name = getPlatformName(platform);
  const businessCollects = details.collectedBy === PlatformCollectedBy.Business;
  const choices = [
    { value: PlatformCollectedBy.Platform, label: `${name} (pagó en la app)` },
    { value: PlatformCollectedBy.Business, label: 'El negocio (el repartidor nos paga)' },
  ];

  return (
    <div className="sell-details mb-2">
      <div className="mb-2">
        <label className="form-label small mb-1" htmlFor={`${id}-folio`}>{hint.label}</label>
        <input
          // Another platform is another folio to type: the field takes the focus again.
          key={platform}
          id={`${id}-folio`}
          className={`form-control ${folioError ? 'is-invalid' : ''}`}
          maxLength={PLATFORM_FOLIO_MAX_LENGTH}
          placeholder={hint.placeholder}
          inputMode={hint.inputMode}
          autoCapitalize={hint.autoCapitalize ?? 'none'}
          autoComplete="off"
          spellCheck={false}
          // Only while it is empty: a sheet opened again on a phone does not pop the keyboard over a folio already typed.
          autoFocus={!details.externalOrderNumber}
          required
          aria-invalid={folioError ? true : undefined}
          aria-describedby={`${id}-folio-help`}
          value={details.externalOrderNumber}
          onChange={(event) => onChange({ externalOrderNumber: event.target.value })}
          disabled={disabled}
        />
        <div id={`${id}-folio-help`} className={folioError ? 'invalid-feedback d-block' : 'form-text'}>
          {folioError || hint.help}
        </div>
      </div>

      <div className="mb-2">
        <label className="form-label small mb-1" htmlFor={`${id}-order-id`}>ID largo del pedido (opcional)</label>
        <input
          id={`${id}-order-id`}
          className={`form-control ${orderIdError ? 'is-invalid' : ''}`}
          maxLength={PLATFORM_ORDER_ID_MAX_INPUT_LENGTH}
          autoCapitalize="characters"
          autoComplete="off"
          spellCheck={false}
          aria-invalid={orderIdError ? true : undefined}
          aria-describedby={`${id}-order-id-help`}
          value={details.externalOrderId}
          onChange={(event) => onChange({ externalOrderId: event.target.value })}
          disabled={disabled}
        />
        <div id={`${id}-order-id-help`} className={orderIdError ? 'invalid-feedback d-block' : 'form-text'}>
          {orderIdError || getPlatformOrderIdHelp(platform)}
        </div>
      </div>

      <div className="mb-2">
        <div id={collectedLabelId} className="form-label small mb-1">¿Quién cobró?</div>
        <div className="d-flex flex-wrap gap-2" role="radiogroup" aria-labelledby={collectedLabelId}>
          {choices.map((choice) => (
            <button
              key={choice.value}
              type="button"
              role="radio"
              aria-checked={details.collectedBy === choice.value}
              className={`btn btn-sm ${details.collectedBy === choice.value ? 'btn-primary' : 'btn-outline-secondary'}`}
              onClick={() => onChange({ collectedBy: choice.value })}
              disabled={disabled}
            >
              {choice.label}
            </button>
          ))}
        </div>
        {businessCollects && canCharge && (
          <div className="form-text">El repartidor te paga al recoger: cóbralo en la caja como siempre.</div>
        )}
      </div>

      <div>
        <label className="form-label small mb-1" htmlFor={`${id}-bag`}>Nombre para la bolsa (opcional)</label>
        <input
          id={`${id}-bag`}
          className="form-control"
          maxLength={200}
          autoComplete="off"
          aria-describedby={`${id}-bag-help`}
          value={details.customerName}
          onChange={(event) => onChange({ customerName: event.target.value })}
          disabled={disabled}
        />
        <div id={`${id}-bag-help`} className="form-text">El nombre que muestra la app. No escribas teléfono ni dirección.</div>
      </div>
    </div>
  );
}
