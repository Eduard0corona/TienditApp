import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess, getLandingPath } from '../../utils/access';

interface Tab {
  label: string;
  path: string;
  icon: string;
  primary?: boolean;
}

interface BottomTabBarProps {
  /** "Más" opens the full menu. */
  onOpenMenu: () => void;
  isMenuOpen: boolean;
}

/**
 * Phones: the screens of the day under the thumb, with Vender in the middle. Everything else is in
 * "Más" (the full menu).
 */
export default function BottomTabBar({ onOpenMenu, isMenuOpen }: BottomTabBarProps) {
  const { pathname } = useLocation();
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const access = getAccess(user, capabilities);

  const home: Tab = { label: 'Inicio', path: access.dashboard ? '/dashboard' : getLandingPath(user, capabilities), icon: 'ri-home-4-line' };
  const center: Tab | null = access.sell
    ? { label: 'Vender', path: '/sell', icon: 'ri-shopping-basket-2-line', primary: true }
    : access.kitchen
      ? { label: 'Cocina', path: '/kitchen', icon: 'ri-restaurant-2-line', primary: true }
      : access.serviceBoard
        ? { label: 'Taller', path: '/services/board', icon: 'ri-tools-line', primary: true }
        : null;
  const tabs = [
    home,
    access.orders ? { label: 'Pedidos', path: '/orders', icon: 'ri-file-list-3-line' } : null,
    center,
    access.chat ? { label: 'Mensajes', path: '/whatsapp/chat', icon: 'ri-chat-3-line' } : null,
  ].filter((tab): tab is Tab => tab !== null && !(tab !== center && center?.path === tab.path));

  const isActive = (path: string) => pathname === path || pathname.startsWith(`${path}/`);

  return (
    <nav className="bottom-tab-bar d-md-none" aria-label="Accesos rápidos">
      {tabs.map((tab) => (
        <Link
          key={tab.path}
          to={tab.path}
          className={`bottom-tab ${tab.primary ? 'bottom-tab-primary' : ''} ${isActive(tab.path) && !isMenuOpen ? 'active' : ''}`}
          aria-current={isActive(tab.path) ? 'page' : undefined}
        >
          <i className={tab.icon} aria-hidden="true"></i>
          <span>{tab.label}</span>
        </Link>
      ))}
      <button
        type="button"
        className={`bottom-tab ${isMenuOpen ? 'active' : ''}`}
        onClick={onOpenMenu}
        aria-expanded={isMenuOpen}
      >
        <i className="ri-menu-line" aria-hidden="true"></i>
        <span>Más</span>
      </button>
    </nav>
  );
}
