import { useCallback, useEffect, useLayoutEffect, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import LeftSidebar from './LeftSidebar';
import Topbar from './Topbar';
import Footer from './Footer';

type DesktopSidenavSize = 'default' | 'condensed';

// Same breakpoints the Hyper theme uses: below 768 px the menu slides over the content, up to 1140 px
// it starts condensed (icons only) so tablets keep room for the page.
const MOBILE_QUERY = '(max-width: 767.98px)';
const TABLET_QUERY = '(max-width: 1140px)';
const SIDENAV_SIZE_STORAGE_KEY = 'tienditapp.sidenavSize';

function readStoredSize(): DesktopSidenavSize | null {
  try {
    const stored = window.localStorage.getItem(SIDENAV_SIZE_STORAGE_KEY);
    return stored === 'default' || stored === 'condensed' ? stored : null;
  } catch {
    return null;
  }
}

function writeStoredSize(size: DesktopSidenavSize) {
  try {
    window.localStorage.setItem(SIDENAV_SIZE_STORAGE_KEY, size);
  } catch {
    // Storage can be blocked (private mode); the menu still works for this visit.
  }
}

export default function Layout() {
  const { pathname } = useLocation();
  const [isMobile, setIsMobile] = useState(() => window.matchMedia(MOBILE_QUERY).matches);
  const [desktopSize, setDesktopSize] = useState<DesktopSidenavSize>(
    () => readStoredSize() ?? (window.matchMedia(TABLET_QUERY).matches ? 'condensed' : 'default'),
  );
  // The mobile menu remembers the page where it was opened: navigating anywhere closes it.
  const [mobileMenuOpenedAt, setMobileMenuOpenedAt] = useState<string | null>(null);
  const isMobileMenuOpen = isMobile && mobileMenuOpenedAt === pathname;

  useEffect(() => {
    const media = window.matchMedia(MOBILE_QUERY);
    const handleChange = (event: MediaQueryListEvent) => {
      setIsMobile(event.matches);
      setMobileMenuOpenedAt(null);
    };
    media.addEventListener('change', handleChange);
    return () => media.removeEventListener('change', handleChange);
  }, []);

  // Layout effect: the size must be on <html> before the first paint, or phones flash the open menu.
  useLayoutEffect(() => {
    const html = document.documentElement;
    html.setAttribute('data-sidenav-size', isMobile ? 'full' : desktopSize);
    html.classList.toggle('sidebar-enable', isMobileMenuOpen);
    document.body.style.overflow = isMobileMenuOpen ? 'hidden' : '';

    return () => {
      html.classList.remove('sidebar-enable');
      document.body.style.overflow = '';
    };
  }, [isMobile, desktopSize, isMobileMenuOpen]);

  useEffect(() => {
    if (!isMobileMenuOpen) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setMobileMenuOpenedAt(null);
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isMobileMenuOpen]);

  const toggleSidebar = useCallback(() => {
    if (isMobile) {
      setMobileMenuOpenedAt((openedAt) => (openedAt === pathname ? null : pathname));
      return;
    }

    const nextSize = desktopSize === 'condensed' ? 'default' : 'condensed';
    setDesktopSize(nextSize);
    writeStoredSize(nextSize);
  }, [desktopSize, isMobile, pathname]);

  const closeMobileMenu = useCallback(() => setMobileMenuOpenedAt(null), []);

  return (
    <div className="wrapper">
      <Topbar onToggleSidebar={toggleSidebar} isMobileMenuOpen={isMobileMenuOpen} />
      <LeftSidebar onClose={closeMobileMenu} />
      {isMobileMenuOpen && <div className="offcanvas-backdrop fade show" onClick={closeMobileMenu} aria-hidden="true" />}
      <div className="content-page">
        <div className="content">
          <div className="container-fluid">
            <Outlet />
          </div>
        </div>
        <Footer />
      </div>
    </div>
  );
}
