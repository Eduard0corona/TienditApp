import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
import TabRow from '../common/TabRow';
import { getSectionTabs, isNavItemActive } from './navigation';

/**
 * The pages of the current section (Catálogo, Compras, Clientes, Reportes, Configuración) the person can open, as
 * tabs. When they do not fit, the row slides (TabRow): the edge that hides tabs fades and, with a mouse, has a button.
 */
export default function SectionTabs() {
  const { pathname } = useLocation();
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const section = getSectionTabs(pathname, getAccess(user, capabilities), capabilities);
  if (!section) return null;

  const pages = section.pages.map((page) => page.path).join();
  const activePath = section.pages.find((page) => isNavItemActive(page, pathname))?.path;
  return (
    <TabRow label={section.label} contentKey={pages} activeKey={activePath}>
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
    </TabRow>
  );
}
