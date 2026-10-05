import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { authApi } from '../../api/authApi';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { usePendingSales } from '../../hooks/usePendingSales';
import { useTenant } from '../../hooks/useTenant';
import type { MyTenantDto } from '../../types';
import { getRoleLabel, getUserRoleLabel } from '../../utils/labels';
import { getAccess } from '../../utils/access';
import { getApiErrorMessage } from '../../api/errors';
import BrandLogo from '../common/BrandLogo';
import AttentionBell from './AttentionBell';
import CashStatusChip from './CashStatusChip';
import CreateBusinessModal from '../business/CreateBusinessModal';
import { applyTheme, storeTheme } from '../../utils/theme';

const AUTO_SWITCH_MARKER_KEY = 'fpd_auto_tenant_switch';
const AVATAR_PLACEHOLDER = '/images/placeholders/avatar.svg';

function readAutoSwitchMarker(): boolean {
  try {
    return window.sessionStorage.getItem(AUTO_SWITCH_MARKER_KEY) === '1';
  } catch {
    return false;
  }
}

function writeAutoSwitchMarker() {
  try {
    window.sessionStorage.setItem(AUTO_SWITCH_MARKER_KEY, '1');
  } catch {
    // Ignore storage failures (private mode / blocked storage)
  }
}

interface TopbarProps {
  onToggleSidebar: () => void;
  isMobileMenuOpen: boolean;
  onOpenSearch: () => void;
}

export default function Topbar({ onToggleSidebar, isMobileMenuOpen, onOpenSearch }: TopbarProps) {
  const { user, logout, switchTenant } = useAuth();
  const { tenant, capabilities, refreshTenant } = useTenant();
  const { confirm } = useConfirm();
  const pendingSales = usePendingSales();
  const access = getAccess(user, capabilities);
  const canOpenBusinessSettings = access.businessSettings;
  const [availableTenants, setAvailableTenants] = useState<MyTenantDto[]>([]);
  const [loadingTenants, setLoadingTenants] = useState(false);
  const [switchingTenant, setSwitchingTenant] = useState(false);
  const [tenantError, setTenantError] = useState('');
  const [autoSwitchAttempted, setAutoSwitchAttempted] = useState(false);
  const [creatingBusiness, setCreatingBusiness] = useState(false);
  // Employees and owners may open a business of their own; a customer account is customer everywhere.
  const canOpenBusiness = !!user && user.role !== 'CUSTOMER' && user.role !== 'SUPER_ADMIN';
  const isBusinessMember = !!tenant && !!user && user.role !== 'CUSTOMER' && user.role !== 'SUPER_ADMIN';

  const selectedTenantId = useMemo(
    () => tenant?.id ?? user?.tenantId ?? '',
    [tenant?.id, user?.tenantId],
  );
  // The business logo when it uploaded one; otherwise the TienditApp mark.
  const tenantLogoLg = tenant?.logoImages?.large || tenant?.logoImages?.medium || tenant?.logoUrl || '';
  const tenantLogoSm = tenant?.logoImages?.small || tenant?.logoImages?.thumbnail || tenant?.logoUrl || '';
  const tenantLogoAlt = tenant?.name ? `Logotipo de ${tenant.name}` : 'Logotipo';

  const handleLogout = async (e: React.MouseEvent) => {
    e.preventDefault();
    const unsent = pendingSales.sales.length;
    if (unsent > 0) {
      // They stay on this device and are sent the next time this user enters here.
      const accepted = await confirm({
        title: 'Tienes ventas sin enviar',
        message: `${unsent === 1 ? 'Una venta cobrada sin conexión sigue' : `${unsent} ventas cobradas sin conexión siguen`} en este dispositivo. `
          + 'Se enviarán cuando vuelvas a entrar con tu usuario aquí'
          + (navigator.onLine ? '.' : ', y sin conexión no podrás entrar hasta que regrese.'),
        confirmLabel: 'Cerrar sesión',
      });
      if (!accepted) return;
    }
    await logout();
  };

  const loadTenants = useCallback(async () => {
    if (!user) {
      setAvailableTenants([]);
      setAutoSwitchAttempted(false);
      return;
    }

    setLoadingTenants(true);
    try {
      const { data } = await authApi.getMyTenants();
      setAvailableTenants(data.filter((x) => x.status.toLowerCase() === 'active'));
      setAutoSwitchAttempted(false);
    } catch {
      setAvailableTenants([]);
    } finally {
      setLoadingTenants(false);
    }
  }, [user]);

  useEffect(() => {
    loadTenants();
  }, [loadTenants]);

  useEffect(() => {
    const ensureTenantContext = async () => {
      if (selectedTenantId || availableTenants.length === 0 || switchingTenant || autoSwitchAttempted) return;
      // Switching reloads the page; trying only once per tab avoids a reload loop if the tenant still
      // cannot be resolved afterwards.
      if (readAutoSwitchMarker()) return;

      writeAutoSwitchMarker();
      setAutoSwitchAttempted(true);
      setSwitchingTenant(true);
      try {
        await switchTenant(availableTenants[0].tenantId);
        await refreshTenant();
      } catch {
        // User can still switch manually from the selector.
      } finally {
        setSwitchingTenant(false);
      }
    };

    ensureTenantContext();
  }, [autoSwitchAttempted, availableTenants, refreshTenant, selectedTenantId, switchTenant, switchingTenant]);

  const handleSwitchTenant = async (nextTenantId: string) => {
    if (!nextTenantId || nextTenantId === selectedTenantId) return;

    setTenantError('');
    setSwitchingTenant(true);
    try {
      await switchTenant(nextTenantId);
      await refreshTenant();
    } catch (err: unknown) {
      setTenantError(getApiErrorMessage(err, 'No se pudo cambiar de negocio.'));
    } finally {
      setSwitchingTenant(false);
    }
  };

  const handleDarkModeToggle = useCallback(() => {
    const next = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
    applyTheme(next);
    storeTheme(next);
  }, []);

  return (
    <div className="navbar-custom">
      <div className="topbar container-fluid">
        <div className="topbar-start d-flex align-items-center gap-lg-2 gap-1">
          {/* Topbar Brand Logo */}
          <div className="logo-topbar">
            <Link to="/" className="logo-light">
              <span className="logo-lg">
                {tenantLogoLg ? <img src={tenantLogoLg} alt={tenantLogoAlt} /> : <BrandLogo tone="light" />}
              </span>
              <span className="logo-sm">
                {tenantLogoSm ? <img src={tenantLogoSm} alt={tenantLogoAlt} /> : <BrandLogo variant="icon" tone="light" />}
              </span>
            </Link>
            <Link to="/" className="logo-dark">
              <span className="logo-lg">
                {tenantLogoLg ? <img src={tenantLogoLg} alt={tenantLogoAlt} /> : <BrandLogo tone="dark" />}
              </span>
              <span className="logo-sm">
                {tenantLogoSm ? <img src={tenantLogoSm} alt={tenantLogoAlt} /> : <BrandLogo variant="icon" tone="dark" />}
              </span>
            </Link>
          </div>

          {/* Sidebar Menu Toggle Button */}
          <button
            type="button"
            className="button-toggle-menu"
            onClick={onToggleSidebar}
            aria-label="Mostrar u ocultar el menú"
            aria-expanded={isMobileMenuOpen}
          >
            <i className="ri-menu-5-line" aria-hidden="true"></i>
          </button>

          {/* Business switcher: visible at every size when the person works for more than one business. */}
          {availableTenants.length > 1 ? (
            <div className="dropdown topbar-business">
              <button
                type="button"
                className="btn btn-light btn-sm dropdown-toggle topbar-business-button"
                data-bs-toggle="dropdown"
                aria-expanded="false"
                disabled={loadingTenants || switchingTenant}
                title={tenantError || 'Cambiar de negocio'}
              >
                <i className="ri-store-2-line me-1" aria-hidden="true"></i>
                <span className="visually-hidden">Negocio actual: </span>
                <span className="text-truncate">{switchingTenant ? 'Cambiando…' : tenant?.name ?? 'Negocio'}</span>
              </button>
              <div className="dropdown-menu">
                <h6 className="dropdown-header">Cambiar de negocio</h6>
                {availableTenants.map((entry) => (
                  <button
                    key={entry.tenantId}
                    type="button"
                    className={`dropdown-item d-flex align-items-center gap-2 ${entry.tenantId === selectedTenantId ? 'active' : ''}`}
                    aria-current={entry.tenantId === selectedTenantId ? 'true' : undefined}
                    onClick={() => void handleSwitchTenant(entry.tenantId)}
                  >
                    <span className="flex-grow-1">
                      {entry.name}
                      <small className="d-block opacity-75">{getRoleLabel(entry.role)}</small>
                    </span>
                    {entry.tenantId === selectedTenantId && <i className="ri-check-line" aria-hidden="true"></i>}
                  </button>
                ))}
                {tenantError && <div className="dropdown-item-text text-danger small">{tenantError}</div>}
                {canOpenBusiness && (
                  <>
                    <div className="dropdown-divider"></div>
                    <button type="button" className="dropdown-item" onClick={() => setCreatingBusiness(true)}>
                      <i className="ri-add-line me-1" aria-hidden="true"></i>Abrir otro negocio
                    </button>
                  </>
                )}
              </div>
            </div>
          ) : (
            tenant?.name && <span className="topbar-business-name d-none d-md-inline text-truncate">{tenant.name}</span>
          )}
        </div>

        <ul className="topbar-menu d-flex align-items-center gap-2 gap-md-3">
          {/* Global search (Ctrl K) */}
          <li>
            <button type="button" className="topbar-search-button" onClick={onOpenSearch} aria-label="Buscar (Ctrl K)">
              <i className="ri-search-line" aria-hidden="true"></i>
              <span className="d-none d-lg-inline">Buscar</span>
              <kbd className="d-none d-xl-inline">Ctrl K</kbd>
            </button>
          </li>

          {access.sell && (
            <li className="d-none d-md-inline-block">
              <Link to="/sell" className="btn btn-primary btn-sm topbar-sell-button" title="Nueva venta (F2)">
                <i className="ri-add-line" aria-hidden="true"></i>
                <span className="ms-1">Nueva venta</span>
                <kbd className="d-none d-xl-inline ms-2">F2</kbd>
              </Link>
            </li>
          )}

          {access.cash && (
            <li className="d-none d-lg-inline-block">
              <CashStatusChip />
            </li>
          )}

          {/* What needs someone now: the API counts only what this person may handle. */}
          {isBusinessMember && (
            <li>
              <AttentionBell />
            </li>
          )}

          {/* Dark/Light mode toggle */}
          <li className="d-none d-sm-inline-block">
            <button
              type="button"
              className="nav-link border-0 bg-transparent"
              id="light-dark-mode"
              onClick={handleDarkModeToggle}
              aria-label="Cambiar entre modo claro y oscuro"
              title="Modo claro u oscuro"
            >
              <i className="ri-moon-line font-22" aria-hidden="true"></i>
            </button>
          </li>

          {/* User Dropdown */}
          <li className="dropdown">
            <a
              className="nav-link dropdown-toggle arrow-none nav-user px-2"
              data-bs-toggle="dropdown"
              href="#"
              role="button"
              aria-haspopup="false"
              aria-expanded="false"
            >
              <span className="account-user-avatar">
                <img
                  src={user?.avatarImages?.thumbnail || AVATAR_PLACEHOLDER}
                  alt=""
                  width="32"
                  className="rounded-circle"
                />
              </span>
              <span className="d-lg-flex flex-column gap-1 d-none">
                <h5 className="my-0">
                  {user ? `${user.firstName} ${user.lastName}` : 'Usuario'}
                </h5>
                <h6 className="my-0 fw-normal">{getUserRoleLabel(user?.role, user?.tenantRole)}</h6>
              </span>
            </a>
            <div className="dropdown-menu dropdown-menu-end dropdown-menu-animated profile-dropdown">
              {/* Welcome header */}
              <div className="dropdown-header noti-title">
                <h6 className="text-overflow m-0">¡Hola{user?.firstName ? `, ${user.firstName}` : ''}!</h6>
              </div>

              <Link to="/profile" className="dropdown-item">
                <i className="ri-user-smile-line font-16 me-1"></i>
                <span>Mi perfil</span>
              </Link>

              {canOpenBusinessSettings && (
                <Link to="/settings/business" className="dropdown-item">
                  <i className="ri-store-2-line font-16 me-1" aria-hidden="true"></i>
                  <span>Mi negocio</span>
                </Link>
              )}

              {canOpenBusiness && (
                <button type="button" className="dropdown-item" onClick={() => setCreatingBusiness(true)}>
                  <i className="ri-add-circle-line font-16 me-1" aria-hidden="true"></i>
                  <span>Abrir otro negocio</span>
                </button>
              )}

              <a href="#" className="dropdown-item" onClick={handleLogout}>
                <i className="ri-login-circle-line font-16 me-1"></i>
                <span>Cerrar sesión</span>
              </a>
            </div>
          </li>
        </ul>
      </div>
      <CreateBusinessModal show={creatingBusiness} onHide={() => setCreatingBusiness(false)} />
    </div>
  );
}
