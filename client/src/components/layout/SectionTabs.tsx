import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
import { getSectionTabs, isNavItemActive } from './navigation';

/** The pages of the current section (Catálogo, Inventario, Clientes, Configuración) the person can open, as tabs. */
export default function SectionTabs() {
  const { pathname } = useLocation();
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const section = getSectionTabs(pathname, getAccess(user, capabilities), capabilities);
  if (!section) return null;

  return (
    <nav className="section-tabs" aria-label={section.label}>
      {section.pages.map((page) => {
        const active = isNavItemActive(page, pathname);
        return (
          <Link
            key={page.path}
            to={page.path}
            className={`section-tab${active ? ' active' : ''}`}
            aria-current={active ? 'page' : undefined}
          >
            <i className={page.icon} aria-hidden="true"></i>
            {page.label}
          </Link>
        );
      })}
    </nav>
  );
}
