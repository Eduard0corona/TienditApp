import type { CustomerFiscalData } from '../../types';
import { CFDI_REGIMES, CFDI_USES } from '../../utils/cfdi';

interface CfdiReceiverFieldsProps {
  /** Prefix of the fields' ids, unique in the page. */
  idPrefix: string;
  value: CustomerFiscalData;
  onChange: (value: CustomerFiscalData) => void;
  disabled?: boolean;
}

/** RFC, name, fiscal postal code, regime and CFDI use, as the customer's constancia de situación fiscal says. */
export default function CfdiReceiverFields({ idPrefix, value, onChange, disabled = false }: CfdiReceiverFieldsProps) {
  const update = (field: keyof CustomerFiscalData) => (event: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    onChange({ ...value, [field]: event.target.value });

  return (
    <div className="row g-2">
      <div className="col-md-3">
        <label className="form-label small mb-1" htmlFor={`${idPrefix}-rfc`}>RFC</label>
        <input id={`${idPrefix}-rfc`} className="form-control text-uppercase" maxLength={13} required disabled={disabled}
          autoComplete="off" value={value.rfc} onChange={update('rfc')} />
      </div>
      <div className="col-md-5">
        <label className="form-label small mb-1" htmlFor={`${idPrefix}-name`}>Nombre o razón social</label>
        <input id={`${idPrefix}-name`} className="form-control" maxLength={254} required disabled={disabled}
          value={value.name} onChange={update('name')} />
      </div>
      <div className="col-md-4">
        <label className="form-label small mb-1" htmlFor={`${idPrefix}-zip`}>Código postal fiscal</label>
        <input id={`${idPrefix}-zip`} className="form-control" inputMode="numeric" maxLength={5} required disabled={disabled}
          value={value.postalCode} onChange={update('postalCode')} />
      </div>
      <div className="col-md-6">
        <label className="form-label small mb-1" htmlFor={`${idPrefix}-regime`}>Régimen fiscal</label>
        <select id={`${idPrefix}-regime`} className="form-select" disabled={disabled}
          value={value.fiscalRegime} onChange={update('fiscalRegime')}>
          {CFDI_REGIMES.map(([code, label]) => <option key={code} value={code}>{code} · {label}</option>)}
        </select>
      </div>
      <div className="col-md-6">
        <label className="form-label small mb-1" htmlFor={`${idPrefix}-use`}>Uso del CFDI</label>
        <select id={`${idPrefix}-use`} className="form-select" disabled={disabled}
          value={value.cfdiUse} onChange={update('cfdiUse')}>
          {CFDI_USES.map(([code, label]) => <option key={code} value={code}>{code} · {label}</option>)}
        </select>
      </div>
    </div>
  );
}
