import { Link } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { useCallback } from 'react';

export default function Topbar() {
  const { user, logout } = useAuth();

  const handleLogout = async (e: React.MouseEvent) => {
    e.preventDefault();
    await logout();
  };

  const handleToggleSidebar = () => {
    const html = document.documentElement;
    const size = html.getAttribute('data-sidenav-size');

    if (window.innerWidth < 768) {
      // Mobile: toggle sidebar overlay
      html.classList.toggle('sidebar-enable');
    } else {
      // Desktop: toggle between condensed and default
      if (size === 'condensed') {
        html.setAttribute('data-sidenav-size', 'default');
      } else {
        html.setAttribute('data-sidenav-size', 'condensed');
      }
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
    const html = document.documentElement;
    const current = html.getAttribute('data-bs-theme');
    html.setAttribute('data-bs-theme', current === 'dark' ? 'light' : 'dark');
  }, []);

  return (
    <div className="navbar-custom">
      <div className="topbar container-fluid">
        <div className="d-flex align-items-center gap-lg-2 gap-1">
          {/* Topbar Brand Logo */}
          <div className="logo-topbar">
            <Link to="/dashboard" className="logo-light">
              <span className="logo-lg">
                <img src="/images/logo.png" alt="logo" />
              </span>
              <span className="logo-sm">
                <img src="/images/logo-sm.png" alt="small logo" />
              </span>
            </Link>
            <Link to="/dashboard" className="logo-dark">
              <span className="logo-lg">
                <img src="/images/logo-dark.png" alt="dark logo" />
              </span>
              <span className="logo-sm">
                <img src="/images/logo-dark-sm.png" alt="small logo" />
              </span>
            </Link>
          </div>

          {/* Sidebar Menu Toggle Button */}
          <button className="button-toggle-menu" onClick={handleToggleSidebar}>
            <i className="ri-menu-5-line"></i>
          </button>
        </div>

        <ul className="topbar-menu d-flex align-items-center gap-3">
          {/* Notification Bell */}
          <li className="dropdown notification-list">
            <a
              className="nav-link dropdown-toggle arrow-none"
              data-bs-toggle="dropdown"
              href="#"
              role="button"
              aria-haspopup="false"
              aria-expanded="false"
            >
              <i className="ri-notification-3-line font-22"></i>
              <span className="noti-icon-badge"></span>
            </a>
            <div className="dropdown-menu dropdown-menu-end dropdown-menu-animated dropdown-lg py-0">
              <div className="p-2 border-top-0 border-start-0 border-end-0 border-dashed border">
                <div className="row align-items-center">
                  <div className="col">
                    <h6 className="m-0 font-16 fw-semibold">Notificaciones</h6>
                  </div>
                  <div className="col-auto">
                    <a href="#" className="text-dark text-decoration-underline">
                      <small>Limpiar todo</small>
                    </a>
                  </div>
                </div>
              </div>
              <div className="px-2" style={{ maxHeight: 300 }} data-simplebar>
                <div className="text-center py-4">
                  <p className="text-muted mb-0">No hay notificaciones nuevas</p>
                </div>
              </div>
              <a href="#" className="dropdown-item text-center text-primary notify-item border-top py-2">
                Ver todas
              </a>
            </div>
          </li>

          {/* Apps Grid Dropdown */}
          <li className="dropdown d-none d-sm-inline-block">
            <a
              className="nav-link dropdown-toggle arrow-none"
              data-bs-toggle="dropdown"
              href="#"
              role="button"
              aria-haspopup="false"
              aria-expanded="false"
            >
              <i className="ri-apps-2-line font-22"></i>
            </a>
            <div className="dropdown-menu dropdown-menu-end dropdown-menu-animated dropdown-lg p-0">
              <div className="p-2">
                <div className="row g-0">
                  <div className="col">
                    <a className="dropdown-icon-item" href="#">
                      <i className="ri-dashboard-line font-24 text-primary"></i>
                      <span>Dashboard</span>
                    </a>
                  </div>
                  <div className="col">
                    <a className="dropdown-icon-item" href="#">
                      <i className="ri-shopping-bag-3-line font-24 text-success"></i>
                      <span>Pedidos</span>
                    </a>
                  </div>
                  <div className="col">
                    <a className="dropdown-icon-item" href="#">
                      <i className="ri-store-2-line font-24 text-info"></i>
                      <span>Productos</span>
                    </a>
                  </div>
                </div>
                <div className="row g-0">
                  <div className="col">
                    <a className="dropdown-icon-item" href="#">
                      <i className="ri-group-line font-24 text-warning"></i>
                      <span>Usuarios</span>
                    </a>
                  </div>
                  <div className="col">
                    <a className="dropdown-icon-item" href="#">
                      <i className="ri-bar-chart-2-line font-24 text-danger"></i>
                      <span>Reportes</span>
                    </a>
                  </div>
                  <div className="col">
                    <a className="dropdown-icon-item" href="#">
                      <i className="ri-settings-3-line font-24 text-muted"></i>
                      <span>Config</span>
                    </a>
                  </div>
                </div>
              </div>
            </div>
          </li>

          {/* Dark/Light mode toggle */}
          <li className="d-none d-sm-inline-block">
            <div
              className="nav-link"
              id="light-dark-mode"
              onClick={handleDarkModeToggle}
              style={{ cursor: 'pointer' }}
            >
              <i className="ri-moon-line font-22"></i>
            </div>
          </li>

          {/* Fullscreen Toggle */}
          <li className="d-none d-md-inline-block">
            <a className="nav-link" href="#" onClick={handleFullscreen}>
              <i className="ri-fullscreen-line font-22"></i>
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
                  src={user?.avatarImages?.thumbnail || '/images/users/avatar-1.jpg'}
                  alt="user"
                  width="32"
                  className="rounded-circle"
                />
              </span>
              <span className="d-lg-flex flex-column gap-1 d-none">
                <h5 className="my-0">
                  {user ? `${user.firstName} ${user.lastName}` : 'Usuario'}
                </h5>
                <h6 className="my-0 fw-normal">{user?.role || ''}</h6>
              </span>
            </a>
            <div className="dropdown-menu dropdown-menu-end dropdown-menu-animated profile-dropdown">
              {/* Welcome header */}
              <div className="dropdown-header noti-title">
                <h6 className="text-overflow m-0">Bienvenido!</h6>
              </div>

              {/* My Account */}
              <Link to="/profile" className="dropdown-item">
                <i className="ri-user-smile-line font-16 me-1"></i>
                <span>Mi Cuenta</span>
              </Link>

              {/* Settings */}
              <Link to="/settings" className="dropdown-item">
                <i className="ri-user-settings-line font-16 me-1"></i>
                <span>Configuración</span>
              </Link>

              {/* Support */}
              <a href="#" className="dropdown-item">
                <i className="ri-lifebuoy-line font-16 me-1"></i>
                <span>Soporte</span>
              </a>

              {/* Logout */}
              <a href="#" className="dropdown-item" onClick={handleLogout}>
                <i className="ri-login-circle-line font-16 me-1"></i>
                <span>Cerrar Sesión</span>
              </a>
            </div>
          </li>
        </ul>
      </div>
    </div>
  );
}
