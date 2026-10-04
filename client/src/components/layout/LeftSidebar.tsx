import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { TenantCapability } from '../../types';
import { useTenant } from '../../hooks/useTenant';
import { COMMERCE_CAPABILITIES, hasAnyCapability, hasCapability } from '../../utils/tenantCapabilities';

interface LeftSidebarProps {
  /** Closes the slide-out menu on phones (the close button only shows in that mode). */
  onClose: () => void;
}

export default function LeftSidebar({ onClose }: LeftSidebarProps) {
  const { pathname } = useLocation();
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const isPrivilegedRole = user?.role === 'TenantAdmin' || user?.role === 'SuperAdmin';
  const userPermissions = new Set((user?.permissions ?? []).map((permission) => permission.toLowerCase()));
  const hasPermission = (permission: string) =>
    isPrivilegedRole || userPermissions.has(permission.toLowerCase());

  const isActive = (path: string) => pathname === path || pathname.startsWith(path + '/');
  const isServicesCatalogActive = pathname === '/services';
  const isServiceBoardActive = pathname === '/services/board';
  const canUseCommerce = hasAnyCapability(capabilities, COMMERCE_CAPABILITIES);
  const canUseInventory = hasCapability(capabilities, TenantCapability.Inventory);
  const canUseAppointments = hasCapability(capabilities, TenantCapability.Appointments);
  const canUseKitchen = hasCapability(capabilities, TenantCapability.Kitchen);
  const canUseServiceBoard = hasCapability(capabilities, TenantCapability.Service);
  const canManageCatalog = hasPermission('ManageCatalog');
  const canManageOrders = hasPermission('ManageOrders');
  const canManageServiceBoard = hasPermission('ManageServiceBoard');
  const canManageInventory = hasPermission('ManageInventory');
  const canManageCash = hasPermission('ManageCash');
  const canManageCustomers = hasPermission('ManageCustomers');
  const canViewReports = hasPermission('ViewReports');
  const canManageWhatsApp = hasPermission('ManageWhatsApp');
  const canManageAiAgent = hasPermission('ManageAiAgent');
  const canManageEmployees = hasPermission('ManageEmployees');
  const canManageAppointments = hasPermission('ManageAppointments');
  const showCatalog = canUseCommerce && canManageCatalog;
  const showInventory = canUseInventory && canManageInventory;
  const showBranches = canUseInventory && canManageInventory;
  const showCash = canUseCommerce && canManageCash;
  const showOrders = canUseCommerce && canManageOrders;
  const showServiceBoard = canUseServiceBoard && canManageServiceBoard;
  const showKitchen = canUseKitchen && canManageServiceBoard;
  const showCustomers = canUseCommerce && canManageCustomers;
  const showEmployees = isPrivilegedRole && canUseCommerce && canManageEmployees;
  const showAppointments = canUseAppointments && canManageAppointments;
  const showReports = canUseCommerce && canViewReports;
  const showOperationSection = showInventory || showBranches || showCash || showOrders || showServiceBoard || showKitchen;
  const showAiAgent = isPrivilegedRole && canUseCommerce && canManageAiAgent;
  const showWhatsAppSettings = isPrivilegedRole && canUseCommerce && canManageWhatsApp;
  const showWhatsAppChat = isPrivilegedRole && canUseCommerce && canManageWhatsApp;
  const showAudit = isPrivilegedRole && canUseCommerce && canViewReports;
  const tenantLogoLg = tenant?.logoImages?.large || tenant?.logoImages?.medium || tenant?.logoUrl || '/images/logo.png';
  const tenantLogoSm = tenant?.logoImages?.small || tenant?.logoImages?.thumbnail || tenant?.logoUrl || '/images/logo-sm.png';

  return (
    <div className="leftside-menu">
      <Link to="/dashboard" className="logo logo-light">
        <span className="logo-lg">
          <img src={tenantLogoLg} alt="logo" />
        </span>
        <span className="logo-sm">
          <img src={tenantLogoSm} alt="small logo" />
        </span>
      </Link>

      <Link to="/dashboard" className="logo logo-dark">
        <span className="logo-lg">
          <img src={tenantLogoLg} alt="dark logo" />
        </span>
        <span className="logo-sm">
          <img src={tenantLogoSm} alt="small logo" />
        </span>
      </Link>

      <div
        className="button-sm-hover"
        data-bs-toggle="tooltip"
        data-bs-placement="right"
        title="Show Full Sidebar"
      >
        <i className="ri-checkbox-blank-circle-line align-middle"></i>
      </div>

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
              src={user?.avatarImages?.thumbnail || '/images/users/avatar-1.jpg'}
              alt="user"
              height="42"
              className="rounded-circle shadow-sm"
            />
            <span className="leftbar-user-name mt-2">
              {user ? `${user.firstName} ${user.lastName}` : 'Usuario'}
            </span>
          </Link>
        </div>

        <ul className="side-nav">
          <li className="side-nav-title">Navigation</li>

          <li className={`side-nav-item ${isActive('/dashboard') ? 'menuitem-active' : ''}`}>
            <Link to="/dashboard" className="side-nav-link">
              <i className="ri-home-4-line"></i>
              <span>Dashboard</span>
            </Link>
          </li>

          {showCatalog && <li className="side-nav-title">Catalogo</li>}

          {showCatalog && (
            <li className={`side-nav-item ${isActive('/products') ? 'menuitem-active' : ''}`}>
              <Link to="/products" className="side-nav-link">
                <i className="ri-shopping-bag-line"></i>
                <span>Productos</span>
              </Link>
            </li>
          )}

          {showCatalog && (
            <li className={`side-nav-item ${isActive('/categories') ? 'menuitem-active' : ''}`}>
              <Link to="/categories" className="side-nav-link">
                <i className="ri-list-check-2"></i>
                <span>Categorias</span>
              </Link>
            </li>
          )}

          {showCatalog && (
            <li className={`side-nav-item ${isServicesCatalogActive ? 'menuitem-active' : ''}`}>
              <Link to="/services" className="side-nav-link">
                <i className="ri-service-line"></i>
                <span>Servicios</span>
              </Link>
            </li>
          )}

          {showOperationSection && <li className="side-nav-title">Operacion</li>}

          {showInventory && (
            <li className={`side-nav-item ${isActive('/inventory') ? 'menuitem-active' : ''}`}>
              <Link to="/inventory" className="side-nav-link">
                <i className="ri-archive-drawer-line"></i>
                <span>Inventario</span>
              </Link>
            </li>
          )}

          {showBranches && (
            <li className={`side-nav-item ${isActive('/branches') ? 'menuitem-active' : ''}`}>
              <Link to="/branches" className="side-nav-link">
                <i className="ri-store-3-line"></i>
                <span>Sucursales</span>
              </Link>
            </li>
          )}

          {showInventory && (
            <li className={`side-nav-item ${isActive('/suppliers') ? 'menuitem-active' : ''}`}>
              <Link to="/suppliers" className="side-nav-link">
                <i className="ri-truck-line"></i>
                <span>Proveedores</span>
              </Link>
            </li>
          )}

          {showInventory && (
            <li className={`side-nav-item ${isActive('/purchases') ? 'menuitem-active' : ''}`}>
              <Link to="/purchases" className="side-nav-link">
                <i className="ri-shopping-cart-2-line"></i>
                <span>Compras</span>
              </Link>
            </li>
          )}

          {showCash && (
            <li className={`side-nav-item ${isActive('/cash') ? 'menuitem-active' : ''}`}>
              <Link to="/cash" className="side-nav-link">
                <i className="ri-money-dollar-circle-line"></i>
                <span>Caja</span>
              </Link>
            </li>
          )}

          {showOrders && (
            <li className={`side-nav-item ${isActive('/orders') ? 'menuitem-active' : ''}`}>
              <Link to="/orders" className="side-nav-link">
                <i className="ri-file-list-3-line"></i>
                <span>Ordenes</span>
              </Link>
            </li>
          )}

          {showServiceBoard && (
            <li className={`side-nav-item ${isServiceBoardActive ? 'menuitem-active' : ''}`}>
              <Link to="/services/board" className="side-nav-link">
                <i className="ri-dashboard-line"></i>
                <span>Tablero Servicios</span>
              </Link>
            </li>
          )}

          {showKitchen && (
            <li className={`side-nav-item ${isActive('/kitchen') ? 'menuitem-active' : ''}`}>
              <Link to="/kitchen" className="side-nav-link">
                <i className="ri-restaurant-2-line"></i>
                <span>Cocina</span>
              </Link>
            </li>
          )}

          {showCustomers && (
            <li className={`side-nav-item ${isActive('/customers') ? 'menuitem-active' : ''}`}>
              <Link to="/customers" className="side-nav-link">
                <i className="ri-team-line"></i>
                <span>Clientes</span>
              </Link>
            </li>
          )}

          {showEmployees && (
            <li className={`side-nav-item ${isActive('/employees') ? 'menuitem-active' : ''}`}>
              <Link to="/employees" className="side-nav-link">
                <i className="ri-user-settings-line"></i>
                <span>Empleados</span>
              </Link>
            </li>
          )}

          {showAppointments && (
            <li className={`side-nav-item ${isActive('/appointments') ? 'menuitem-active' : ''}`}>
              <Link to="/appointments" className="side-nav-link">
                <i className="ri-calendar-schedule-line"></i>
                <span>Citas</span>
              </Link>
            </li>
          )}

          {showReports && (
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
              <span>Mi Perfil</span>
            </Link>
          </li>

          {isPrivilegedRole && canUseCommerce && (
            <>
              {showAiAgent && (
                <li className={`side-nav-item ${isActive('/ai-agent') ? 'menuitem-active' : ''}`}>
                  <Link to="/ai-agent" className="side-nav-link">
                    <i className="ri-robot-2-line"></i>
                    <span>Agente IA</span>
                  </Link>
                </li>
              )}
              <li className={`side-nav-item ${isActive('/settings/business') ? 'menuitem-active' : ''}`}>
                <Link to="/settings/business" className="side-nav-link">
                  <i className="ri-store-2-line"></i>
                  <span>Mi Empresa</span>
                </Link>
              </li>
              {showWhatsAppSettings && (
                <li className={`side-nav-item ${isActive('/settings/whatsapp') ? 'menuitem-active' : ''}`}>
                  <Link to="/settings/whatsapp" className="side-nav-link">
                    <i className="ri-whatsapp-line"></i>
                    <span>WhatsApp</span>
                  </Link>
                </li>
              )}
              {showWhatsAppChat && (
                <li className={`side-nav-item ${isActive('/whatsapp/chat') ? 'menuitem-active' : ''}`}>
                  <Link to="/whatsapp/chat" className="side-nav-link">
                    <i className="ri-chat-3-line"></i>
                    <span>Chat WhatsApp</span>
                  </Link>
                </li>
              )}
              {showAudit && (
                <li className={`side-nav-item ${isActive('/audit') ? 'menuitem-active' : ''}`}>
                  <Link to="/audit" className="side-nav-link">
                    <i className="ri-shield-check-line"></i>
                    <span>Auditoria</span>
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
