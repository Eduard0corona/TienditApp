import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { authApi } from '../../api/authApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import type { MyTenantDto } from '../../types';
import { getRoleLabel, getUserRoleLabel } from '../../utils/labels';
import { getAccess } from '../../utils/access';
import { getApiErrorMessage } from '../../api/errors';
import BrandLogo from '../common/BrandLogo';
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
}

export default function Topbar({ onToggleSidebar, isMobileMenuOpen }: TopbarProps) {
  const { user, logout, switchTenant } = useAuth();
  const { tenant, capabilities, refreshTenant } = useTenant();
  const canOpenBusinessSettings = getAccess(user, capabilities).businessSettings;
  const [availableTenants, setAvailableTenants] = useState<MyTenantDto[]>([]);
  const [loadingTenants, setLoadingTenants] = useState(false);
  const [switchingTenant, setSwitchingTenant] = useState(false);
  const [tenantError, setTenantError] = useState('');
  const [autoSwitchAttempted, setAutoSwitchAttempted] = useState(false);

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

  const handleSwitchTenant = async (event: React.ChangeEvent<HTMLSelectElement>) => {
    const nextTenantId = event.target.value;
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

  const handleFullscreen = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    if (!document.fullscreenElement) {
      document.documentElement.requestFullscreen();
    } else {
      document.exitFullscreen();
    }
  }, []);

  const handleDarkModeToggle = useCallback(() => {
    const next = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
    applyTheme(next);
    storeTheme(next);
  }, []);

  return (
    <div className="navbar-custom">
      <div className="topbar container-fluid">
        <div className="d-flex align-items-center gap-lg-2 gap-1">
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
        </div>

        <ul className="topbar-menu d-flex align-items-center gap-3">
          {availableTenants.length > 1 && (
            <li className="d-none d-lg-inline-block" style={{ minWidth: 260 }}>
              <select
                className="form-select form-select-sm"
                value={selectedTenantId}
                onChange={handleSwitchTenant}
                disabled={loadingTenants || switchingTenant}
                title={tenantError || 'Cambiar de negocio'}
                aria-label="Cambiar de negocio"
              >
                {availableTenants.map((entry) => (
                  <option key={entry.tenantId} value={entry.tenantId}>
                    {entry.name} ({getRoleLabel(entry.role)})
                  </option>
                ))}
              </select>
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

          {/* Fullscreen Toggle */}
          <li className="d-none d-md-inline-block">
            <a className="nav-link" href="#" onClick={handleFullscreen} aria-label="Pantalla completa" title="Pantalla completa">
              <i className="ri-fullscreen-line font-22" aria-hidden="true"></i>
            </a>
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
                  <i className="ri-store-2-line font-16 me-1"></i>
                  <span>Mi empresa</span>
                </Link>
              )}

              <a href="#" className="dropdown-item" onClick={handleLogout}>
                <i className="ri-login-circle-line font-16 me-1"></i>
                <span>Cerrar sesión</span>
              </a>
            </div>
          </li>
        </ul>
      </div>
    </div>
  );
}
