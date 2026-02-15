import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';

export default function LeftSidebar() {
  const { pathname } = useLocation();
  const { user } = useAuth();

  const isActive = (path: string) => pathname === path || pathname.startsWith(path + '/');

  return (
    <div className="leftside-menu">
      {/* Brand Logo Light */}
      <Link to="/dashboard" className="logo logo-light">
        <span className="logo-lg">
          <img src="/images/logo.png" alt="logo" />
        </span>
        <span className="logo-sm">
          <img src="/images/logo-sm.png" alt="small logo" />
        </span>
      </Link>

      {/* Brand Logo Dark */}
      <Link to="/dashboard" className="logo logo-dark">
        <span className="logo-lg">
          <img src="/images/logo-dark.png" alt="dark logo" />
        </span>
        <span className="logo-sm">
          <img src="/images/logo-dark-sm.png" alt="small logo" />
        </span>
      </Link>

      {/* Sidebar Hover Menu Toggle Button */}
      <div
        className="button-sm-hover"
        data-bs-toggle="tooltip"
        data-bs-placement="right"
        title="Show Full Sidebar"
      >
        <i className="ri-checkbox-blank-circle-line align-middle"></i>
      </div>

      {/* Full Sidebar Menu Close Button */}
      <div className="button-close-fullsidebar">
        <i className="ri-close-fill align-middle"></i>
      </div>

      {/* Sidebar */}
      <div className="h-100" id="leftside-menu-container" data-simplebar>
        {/* Leftbar User */}
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

        {/* Sidemenu */}
        <ul className="side-nav">
          <li className="side-nav-title">Navigation</li>

          <li className={`side-nav-item ${isActive('/dashboard') ? 'menuitem-active' : ''}`}>
            <Link to="/dashboard" className="side-nav-link">
              <i className="ri-home-4-line"></i>
              <span>Dashboard</span>
            </Link>
          </li>

          <li className="side-nav-title">Catálogo</li>

          <li className={`side-nav-item ${isActive('/products') ? 'menuitem-active' : ''}`}>
            <Link to="/products" className="side-nav-link">
              <i className="ri-shopping-bag-line"></i>
              <span>Productos</span>
            </Link>
          </li>

          <li className={`side-nav-item ${isActive('/categories') ? 'menuitem-active' : ''}`}>
            <Link to="/categories" className="side-nav-link">
              <i className="ri-list-check-2"></i>
              <span>Categorías</span>
            </Link>
          </li>

          <li className="side-nav-title">Cuenta</li>

          <li className={`side-nav-item ${isActive('/profile') ? 'menuitem-active' : ''}`}>
            <Link to="/profile" className="side-nav-link">
              <i className="ri-user-3-line"></i>
              <span>Mi Perfil</span>
            </Link>
          </li>

          {user?.role === 'TenantAdmin' && (
            <>
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
            </>
          )}
        </ul>

        <div className="clearfix"></div>
      </div>
    </div>
  );
}
