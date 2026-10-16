import { useEffect } from 'react';
import { Link, NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { BusinessTheme } from '../../types';
import { getAccess } from '../../utils/access';
import { previewBusinessTheme } from '../../utils/theme';
import BrandLogo from '../common/BrandLogo';
import ThemeToggle from './ThemeToggle';
import './CustomerLayout.css';

const navClass = ({ isActive }: { isActive: boolean }) => `cl-link${isActive ? ' active' : ''}`;

/**
 * The frame of the customer's screens (Explorar, Mis pedidos): TienditApp's own, without the business menu. Whoever has
 * no session enters or creates a customer account from here; the team of a business goes back to it.
 */
export default function CustomerLayout() {
  const { user, isAuthenticated, isLoading, logout } = useAuth();
  const customer = isAuthenticated && getAccess(user, null).myOrders;

  // TienditApp's colors, not those of the last business opened on this device.
  useEffect(() => {
    previewBusinessTheme(BusinessTheme.Nopal);
    return () => previewBusinessTheme(undefined);
  }, []);

  return (
    <div className="cl-shell">
      <a className="visually-hidden-focusable cl-skip" href="#contenido">Saltar al contenido</a>
      <header className="cl-header">
        <div className="cl-header-inner">
          <Link to="/explorar" className="cl-brand" aria-label="TienditApp, ir a Explorar">
            <BrandLogo />
          </Link>
          <nav className="cl-nav" aria-label="Principal">
            <NavLink to="/explorar" className={navClass}>
              <i className="ri-store-2-line" aria-hidden="true"></i>Explorar
            </NavLink>
            {customer && (
              <NavLink to="/mis-pedidos" className={navClass}>
                <i className="ri-shopping-bag-3-line" aria-hidden="true"></i>Mis pedidos
              </NavLink>
            )}
            {isAuthenticated && !customer && (
              <Link to="/" className="cl-link">
                <i className="ri-dashboard-line" aria-hidden="true"></i>Mi negocio
              </Link>
            )}
          </nav>
          <div className="cl-actions">
            <ThemeToggle />
            {!isLoading && !isAuthenticated && (
              <>
                <Link to="/login" className="btn btn-sm btn-light">Entrar</Link>
                <Link to="/register?cuenta=cliente" className="btn btn-sm btn-primary cl-signup">Crear cuenta</Link>
              </>
            )}
            {customer && (
              <>
                <Link to="/profile" className="btn btn-sm btn-light" aria-label="Mi perfil">
                  <i className="ri-user-line" aria-hidden="true"></i>
                </Link>
                <button type="button" className="btn btn-sm btn-light" onClick={() => void logout()}>Salir</button>
              </>
            )}
          </div>
        </div>
      </header>
      <main id="contenido" className="cl-main">
        <Outlet />
      </main>
      <footer className="cl-footer">
        <span>¿Tienes un negocio?</span>{' '}
        <Link to="/register?cuenta=negocio">Recibe pedidos en línea con TienditApp</Link>
      </footer>
    </div>
  );
}
