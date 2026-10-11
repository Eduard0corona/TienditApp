import type { MyTenantDto } from '../../types';
import { getRoleLabel } from '../../utils/labels';
import PhotoOrInitial from '../common/PhotoOrInitial';

interface BusinessSwitcherProps {
  /** The active businesses of the person, more than one. */
  businesses: MyTenantDto[];
  currentId: string;
  currentName?: string;
  /** The logo of the business it is in, small; the store icon while it has none. */
  currentLogoUrl?: string | null;
  disabled: boolean;
  switching: boolean;
  error: string;
  canOpenBusiness: boolean;
  onSwitch: (tenantId: string) => void;
  onOpenBusiness: () => void;
}

/** The business switcher of the top bar: each business with its logo (or initial), name and role in it. */
export default function BusinessSwitcher({
  businesses,
  currentId,
  currentName,
  currentLogoUrl,
  disabled,
  switching,
  error,
  canOpenBusiness,
  onSwitch,
  onOpenBusiness,
}: BusinessSwitcherProps) {
  return (
    <div className="dropdown topbar-business">
      <button
        type="button"
        className="btn btn-light btn-sm dropdown-toggle topbar-business-button"
        data-bs-toggle="dropdown"
        aria-expanded="false"
        disabled={disabled}
        title={error || 'Cambiar de negocio'}
      >
        {currentLogoUrl
          ? <img src={currentLogoUrl} alt="" className="me-1 topbar-business-icon topbar-business-logo" />
          : <i className="ri-store-2-line me-1 topbar-business-icon" aria-hidden="true"></i>}
        <span className="visually-hidden">Negocio actual: </span>
        <span className="text-truncate">{switching ? 'Cambiando…' : currentName ?? 'Negocio'}</span>
      </button>
      <div className="dropdown-menu">
        <h6 className="dropdown-header">Cambiar de negocio</h6>
        {businesses.map((entry) => (
          <button
            key={entry.tenantId}
            type="button"
            className={`dropdown-item d-flex align-items-center gap-2 ${entry.tenantId === currentId ? 'active' : ''}`}
            aria-current={entry.tenantId === currentId ? 'true' : undefined}
            onClick={() => onSwitch(entry.tenantId)}
          >
            <PhotoOrInitial src={entry.logoUrl} name={entry.name} className="topbar-business-avatar" />
            <span className="flex-grow-1">
              {entry.name}
              <small className="d-block text-muted">{getRoleLabel(entry.role)}</small>
            </span>
            {entry.tenantId === currentId && <i className="ri-check-line" aria-hidden="true"></i>}
          </button>
        ))}
        {error && <div className="dropdown-item-text text-danger small">{error}</div>}
        {canOpenBusiness && (
          <>
            <div className="dropdown-divider"></div>
            <button type="button" className="dropdown-item" onClick={onOpenBusiness}>
              <i className="ri-add-line me-1" aria-hidden="true"></i>Abrir otro negocio
            </button>
          </>
        )}
      </div>
    </div>
  );
}
