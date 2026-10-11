import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
import BrandLogo from '../common/BrandLogo';
import { getAllowedNavGroups, getNavEntryPath, isNavEntryActive } from './navigation';

interface LeftSidebarProps {
  /** Closes the slide-out menu on phones (the close button only shows in that mode). */
  onClose: () => void;
}

export default function LeftSidebar({ onClose }: LeftSidebarProps) {
  const { pathname } = useLocation();
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const groups = getAllowedNavGroups(getAccess(user, capabilities), capabilities);
  // The business logo when it uploaded one; otherwise the TienditApp mark.
  const tenantLogoLg = tenant?.logoImages?.large || tenant?.logoImages?.medium || tenant?.logoUrl || '';
  const tenantLogoSm = tenant?.logoImages?.small || tenant?.logoImages?.thumbnail || tenant?.logoUrl || '';
  const tenantLogoAlt = tenant?.name ? `Logotipo de ${tenant.name}` : 'Logotipo';

  return (
    <div className="ta-sidebar">
      {/* One logo for both modes: the TienditApp mark turns light by itself on a dark page. */}
      <Link to="/" className="ta-sidebar-logo">
        <span className="ta-logo-lg">
          {tenantLogoLg ? <img src={tenantLogoLg} alt={tenantLogoAlt} /> : <BrandLogo />}
        </span>
        <span className="ta-logo-sm">
          {tenantLogoSm ? <img src={tenantLogoSm} alt={tenantLogoAlt} /> : <BrandLogo variant="icon" />}
        </span>
      </Link>

      <button
        type="button"
        className="ta-sidebar-close border-0 bg-transparent p-0"
        onClick={onClose}
        aria-label="Cerrar el menú"
      >
        <i className="ri-close-fill align-middle" aria-hidden="true"></i>
      </button>

      <div className="ta-sidebar-scroll h-100">
        <nav aria-label="Menú principal">
          <ul className="ta-nav">
            {groups.map((group) => (
              <li key={group.title} className="ta-nav-group">
                <div className="ta-nav-title">{group.title}</div>
                <ul className="list-unstyled">
                  {group.entries.map((entry) => {
                    const active = isNavEntryActive(entry, pathname);
                    return (
                      <li key={entry.label} className="ta-nav-item">
                        <Link to={getNavEntryPath(entry)} className={`ta-nav-link ${active ? 'active' : ''}`} aria-current={active ? 'page' : undefined}>
                          <i className={entry.icon} aria-hidden="true"></i>
                          <span>{entry.label}</span>
                        </Link>
                      </li>
                    );
                  })}
                </ul>
              </li>
            ))}
          </ul>
        </nav>

        <div className="clearfix"></div>
      </div>
    </div>
  );
}
