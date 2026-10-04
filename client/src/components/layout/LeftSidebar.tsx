import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
import BrandLogo from '../common/BrandLogo';

const AVATAR_PLACEHOLDER = '/images/placeholders/avatar.svg';

interface LeftSidebarProps {
  /** Closes the slide-out menu on phones (the close button only shows in that mode). */
  onClose: () => void;
}

export default function LeftSidebar({ onClose }: LeftSidebarProps) {
  const { pathname } = useLocation();
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const access = getAccess(user, capabilities);

  const isActive = (path: string) => pathname === path || pathname.startsWith(path + '/');
  const isServicesCatalogActive = pathname === '/services';
  const isServiceBoardActive = pathname === '/services/board';
  const showOperationSection =
    access.inventory || access.cash || access.orders || access.serviceBoard || access.kitchen
    || access.customers || access.chat || access.employees || access.appointments || access.reports;
  const showSettingsSection = access.aiAgent || access.businessSettings || access.whatsAppSettings || access.audit;
  // The business logo when it uploaded one; otherwise the TienditApp mark.
  const tenantLogoLg = tenant?.logoImages?.large || tenant?.logoImages?.medium || tenant?.logoUrl || '';
  const tenantLogoSm = tenant?.logoImages?.small || tenant?.logoImages?.thumbnail || tenant?.logoUrl || '';
  const tenantLogoAlt = tenant?.name ? `Logotipo de ${tenant.name}` : 'Logotipo';

  return (
    <div className="leftside-menu">
      <Link to="/" className="logo logo-light">
        <span className="logo-lg">
          {tenantLogoLg ? <img src={tenantLogoLg} alt={tenantLogoAlt} /> : <BrandLogo tone="light" />}
        </span>
        <span className="logo-sm">
          {tenantLogoSm ? <img src={tenantLogoSm} alt={tenantLogoAlt} /> : <BrandLogo variant="icon" tone="light" />}
        </span>
      </Link>

      <Link to="/" className="logo logo-dark">
        <span className="logo-lg">
          {tenantLogoLg ? <img src={tenantLogoLg} alt={tenantLogoAlt} /> : <BrandLogo tone="dark" />}
        </span>
        <span className="logo-sm">
          {tenantLogoSm ? <img src={tenantLogoSm} alt={tenantLogoAlt} /> : <BrandLogo variant="icon" tone="dark" />}
        </span>
      </Link>

      <button
        type="button"
        className="button-close-fullsidebar border-0 bg-transparent p-0"
        onClick={onClose}
        aria-label="Cerrar el menú"
      >
        <i className="ri-close-fill align-middle" aria-hidden="true"></i>
      </button>

      <div className="h-100" id="leftside-menu-container" data-simplebar>
        <div className="leftbar-user">
          <Link to="/profile">
            <img
              src={user?.avatarImages?.thumbnail || AVATAR_PLACEHOLDER}
              alt=""
              height="42"
              className="rounded-circle shadow-sm"
            />
            <span className="leftbar-user-name mt-2">
              {user ? `${user.firstName} ${user.lastName}` : 'Usuario'}
            </span>
          </Link>
        </div>

        <ul className="side-nav">
          {access.dashboard && <li className="side-nav-title">Principal</li>}

          {access.dashboard && (
            <li className={`side-nav-item ${isActive('/dashboard') ? 'menuitem-active' : ''}`}>
              <Link to="/dashboard" className="side-nav-link">
                <i className="ri-home-4-line"></i>
                <span>Dashboard</span>
              </Link>
            </li>
          )}

          {access.catalog && <li className="side-nav-title">Catálogo</li>}

          {access.catalog && (
            <li className={`side-nav-item ${isActive('/products') ? 'menuitem-active' : ''}`}>
              <Link to="/products" className="side-nav-link">
                <i className="ri-shopping-bag-line"></i>
                <span>Productos</span>
              </Link>
            </li>
          )}

          {access.catalog && (
            <li className={`side-nav-item ${isActive('/categories') ? 'menuitem-active' : ''}`}>
              <Link to="/categories" className="side-nav-link">
                <i className="ri-list-check-2"></i>
                <span>Categorías</span>
              </Link>
            </li>
          )}

          {access.catalog && (
            <li className={`side-nav-item ${isServicesCatalogActive ? 'menuitem-active' : ''}`}>
              <Link to="/services" className="side-nav-link">
                <i className="ri-service-line"></i>
                <span>Servicios</span>
              </Link>
            </li>
          )}

          {showOperationSection && <li className="side-nav-title">Operación</li>}

          {access.inventory && (
            <li className={`side-nav-item ${isActive('/inventory') ? 'menuitem-active' : ''}`}>
              <Link to="/inventory" className="side-nav-link">
                <i className="ri-archive-drawer-line"></i>
                <span>Inventario</span>
              </Link>
            </li>
          )}

          {access.inventory && (
            <li className={`side-nav-item ${isActive('/branches') ? 'menuitem-active' : ''}`}>
              <Link to="/branches" className="side-nav-link">
                <i className="ri-store-3-line"></i>
                <span>Sucursales</span>
              </Link>
            </li>
          )}

          {access.inventory && (
            <li className={`side-nav-item ${isActive('/suppliers') ? 'menuitem-active' : ''}`}>
              <Link to="/suppliers" className="side-nav-link">
                <i className="ri-truck-line"></i>
                <span>Proveedores</span>
              </Link>
            </li>
          )}

          {access.inventory && (
            <li className={`side-nav-item ${isActive('/purchases') ? 'menuitem-active' : ''}`}>
              <Link to="/purchases" className="side-nav-link">
                <i className="ri-shopping-cart-2-line"></i>
                <span>Compras</span>
              </Link>
            </li>
          )}

          {access.cash && (
            <li className={`side-nav-item ${isActive('/cash') ? 'menuitem-active' : ''}`}>
              <Link to="/cash" className="side-nav-link">
                <i className="ri-money-dollar-circle-line"></i>
                <span>Caja</span>
              </Link>
            </li>
          )}

          {access.orders && (
            <li className={`side-nav-item ${isActive('/orders') ? 'menuitem-active' : ''}`}>
              <Link to="/orders" className="side-nav-link">
                <i className="ri-file-list-3-line"></i>
                <span>Órdenes</span>
              </Link>
            </li>
          )}

          {access.serviceBoard && (
            <li className={`side-nav-item ${isServiceBoardActive ? 'menuitem-active' : ''}`}>
              <Link to="/services/board" className="side-nav-link">
                <i className="ri-dashboard-line"></i>
                <span>Tablero de servicios</span>
              </Link>
            </li>
          )}

          {access.kitchen && (
            <li className={`side-nav-item ${isActive('/kitchen') ? 'menuitem-active' : ''}`}>
              <Link to="/kitchen" className="side-nav-link">
                <i className="ri-restaurant-2-line"></i>
                <span>Cocina</span>
              </Link>
            </li>
          )}

          {access.customers && (
            <li className={`side-nav-item ${isActive('/customers') ? 'menuitem-active' : ''}`}>
              <Link to="/customers" className="side-nav-link">
                <i className="ri-team-line"></i>
                <span>Clientes</span>
              </Link>
            </li>
          )}

          {access.chat && (
            <li className={`side-nav-item ${isActive('/whatsapp/chat') ? 'menuitem-active' : ''}`}>
              <Link to="/whatsapp/chat" className="side-nav-link">
                <i className="ri-chat-3-line"></i>
                <span>Chat de WhatsApp</span>
              </Link>
            </li>
          )}

          {access.employees && (
            <li className={`side-nav-item ${isActive('/employees') ? 'menuitem-active' : ''}`}>
              <Link to="/employees" className="side-nav-link">
                <i className="ri-user-settings-line"></i>
                <span>Empleados</span>
              </Link>
            </li>
          )}

          {access.appointments && (
            <li className={`side-nav-item ${isActive('/appointments') ? 'menuitem-active' : ''}`}>
              <Link to="/appointments" className="side-nav-link">
                <i className="ri-calendar-schedule-line"></i>
                <span>Citas</span>
              </Link>
            </li>
          )}

          {access.reports && (
            <li className={`side-nav-item ${isActive('/reports') ? 'menuitem-active' : ''}`}>
              <Link to="/reports" className="side-nav-link">
                <i className="ri-bar-chart-box-line"></i>
                <span>Reportes</span>
              </Link>
            </li>
          )}

          <li className="side-nav-title">Cuenta</li>

          <li className={`side-nav-item ${isActive('/profile') ? 'menuitem-active' : ''}`}>
            <Link to="/profile" className="side-nav-link">
              <i className="ri-user-3-line"></i>
              <span>Mi perfil</span>
            </Link>
          </li>

          {showSettingsSection && (
            <>
              {access.aiAgent && (
                <li className={`side-nav-item ${isActive('/ai-agent') ? 'menuitem-active' : ''}`}>
                  <Link to="/ai-agent" className="side-nav-link">
                    <i className="ri-robot-2-line"></i>
                    <span>Agente IA</span>
                  </Link>
                </li>
              )}
              {access.businessSettings && (
                <li className={`side-nav-item ${isActive('/settings/business') ? 'menuitem-active' : ''}`}>
                  <Link to="/settings/business" className="side-nav-link">
                    <i className="ri-store-2-line"></i>
                    <span>Mi empresa</span>
                  </Link>
                </li>
              )}
              {access.whatsAppSettings && (
                <li className={`side-nav-item ${isActive('/settings/whatsapp') ? 'menuitem-active' : ''}`}>
                  <Link to="/settings/whatsapp" className="side-nav-link">
                    <i className="ri-whatsapp-line"></i>
                    <span>WhatsApp</span>
                  </Link>
                </li>
              )}
              {access.audit && (
                <li className={`side-nav-item ${isActive('/audit') ? 'menuitem-active' : ''}`}>
                  <Link to="/audit" className="side-nav-link">
                    <i className="ri-shield-check-line"></i>
                    <span>Auditoría</span>
                  </Link>
                </li>
              )}
            </>
          )}
        </ul>

        <div className="clearfix"></div>
      </div>
    </div>
  );
}
