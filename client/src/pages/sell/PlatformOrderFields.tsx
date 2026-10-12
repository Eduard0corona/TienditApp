import { useId } from 'react';
import { PlatformCollectedBy } from '../../types';
import {
  PLATFORM_FOLIO_MAX_LENGTH,
  getPlatformFolioHint,
  getPlatformName,
  type DeliveryPlatform,
} from '../../utils/deliveryPlatforms';
import type { SaleDetails } from './sellModel';

interface PlatformOrderFieldsProps {
  platform: DeliveryPlatform;
  details: SaleDetails;
  onChange: (changes: Partial<SaleDetails>) => void;
  /** What is wrong with the folio, once there is something to say (the server checks it again). */
  folioError: string;
  disabled: boolean;
}

/**
 * What an order of a delivery platform needs, in sight without opening anything: the folio of its app (focused as the
 * platform is chosen, with the keyboard that platform's folio takes), who charged it and the name for the bag.
 */
export default function PlatformOrderFields({ platform, details, onChange, folioError, disabled }: PlatformOrderFieldsProps) {
  const collectedLabelId = useId();
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
        <label className="form-label small mb-1" htmlFor="sell-platform-folio">{hint.label}</label>
        <input
          // Another platform is another folio to type: the field takes the focus again.
          key={platform}
          id="sell-platform-folio"
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
          aria-describedby="sell-platform-folio-help"
          value={details.externalOrderNumber}
          onChange={(event) => onChange({ externalOrderNumber: event.target.value })}
          disabled={disabled}
        />
        <div id="sell-platform-folio-help" className={folioError ? 'invalid-feedback d-block' : 'form-text'}>
          {folioError || hint.help}
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
        {businessCollects && (
          <div className="form-text">El repartidor te paga al recoger: cóbralo en la caja como siempre.</div>
        )}
      </div>

      <div>
        <label className="form-label small mb-1" htmlFor="sell-bag-name">Nombre para la bolsa (opcional)</label>
        <input
          id="sell-bag-name"
          className="form-control"
          maxLength={200}
          autoComplete="off"
          aria-describedby="sell-bag-name-help"
          value={details.customerName}
          onChange={(event) => onChange({ customerName: event.target.value })}
          disabled={disabled}
        />
        <div id="sell-bag-name-help" className="form-text">El nombre que muestra la app. No escribas teléfono ni dirección.</div>
      </div>
    </div>
  );
}
