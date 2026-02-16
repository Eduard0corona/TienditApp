import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';

export default function LeftSidebar() {
  const { pathname } = useLocation();
  const { user } = useAuth();

  const isActive = (path: string) => pathname === path || pathname.startsWith(path + '/');

  return (
    <div className="leftside-menu">
      <Link to="/dashboard" className="logo logo-light">
        <span className="logo-lg">
          <img src="/images/logo.png" alt="logo" />
        </span>
        <span className="logo-sm">
          <img src="/images/logo-sm.png" alt="small logo" />
        </span>
      </Link>

      <Link to="/dashboard" className="logo logo-dark">
        <span className="logo-lg">
          <img src="/images/logo-dark.png" alt="dark logo" />
        </span>
        <span className="logo-sm">
          <img src="/images/logo-dark-sm.png" alt="small logo" />
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

      <div className="button-close-fullsidebar">
        <i className="ri-close-fill align-middle"></i>
      </div>

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

          <li className="side-nav-title">Catalogo</li>

          <li className={`side-nav-item ${isActive('/products') ? 'menuitem-active' : ''}`}>
            <Link to="/products" className="side-nav-link">
              <i className="ri-shopping-bag-line"></i>
              <span>Productos</span>
            </Link>
          </li>

          <li className={`side-nav-item ${isActive('/categories') ? 'menuitem-active' : ''}`}>
            <Link to="/categories" className="side-nav-link">
              <i className="ri-list-check-2"></i>
              <span>Categorias</span>
            </Link>
          </li>

          <li className={`side-nav-item ${isActive('/services') ? 'menuitem-active' : ''}`}>
            <Link to="/services" className="side-nav-link">
              <i className="ri-service-line"></i>
              <span>Servicios</span>
            </Link>
          </li>

          <li className="side-nav-title">Operacion</li>

          <li className={`side-nav-item ${isActive('/orders') ? 'menuitem-active' : ''}`}>
            <Link to="/orders" className="side-nav-link">
              <i className="ri-file-list-3-line"></i>
              <span>Ordenes</span>
            </Link>
          </li>

          <li className={`side-nav-item ${isActive('/customers') ? 'menuitem-active' : ''}`}>
            <Link to="/customers" className="side-nav-link">
              <i className="ri-team-line"></i>
              <span>Clientes</span>
            </Link>
          </li>

          <li className="side-nav-title">Cuenta</li>

          <li className={`side-nav-item ${isActive('/profile') ? 'menuitem-active' : ''}`}>
            <Link to="/profile" className="side-nav-link">
              <i className="ri-user-3-line"></i>
              <span>Mi Perfil</span>
            </Link>
          </li>

          {(user?.role === 'TenantAdmin' || user?.role === 'SuperAdmin') && (
            <>
              <li className={`side-nav-item ${isActive('/ai-agent') ? 'menuitem-active' : ''}`}>
                <Link to="/ai-agent" className="side-nav-link">
                  <i className="ri-robot-2-line"></i>
                  <span>Agente IA</span>
                </Link>
              </li>
              <li className={`side-nav-item ${isActive('/settings/business') ? 'menuitem-active' : ''}`}>
                <Link to="/settings/business" className="side-nav-link">
                  <i className="ri-store-2-line"></i>
                  <span>Mi Empresa</span>
                </Link>
              </li>
              <li className={`side-nav-item ${isActive('/settings/whatsapp') ? 'menuitem-active' : ''}`}>
                <Link to="/settings/whatsapp" className="side-nav-link">
                  <i className="ri-whatsapp-line"></i>
                  <span>WhatsApp</span>
                </Link>
              </li>
              <li className={`side-nav-item ${isActive('/whatsapp/chat') ? 'menuitem-active' : ''}`}>
                <Link to="/whatsapp/chat" className="side-nav-link">
                  <i className="ri-chat-3-line"></i>
                  <span>Chat WhatsApp</span>
                </Link>
              </li>
            </>
          )}
        </ul>

        <div className="clearfix"></div>
      </div>
    </div>
  );
}
