import { useLayoutEffect } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { useScrollFade } from '../../hooks/useScrollFade';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
import { pageScroll, revealScroll, type RowItem, type RowPadding } from '../../utils/scrollRow';
import { getSectionTabs, isNavItemActive } from './navigation';

/** Where a tab starts and ends along the row's content, wherever the row is scrolled. */
function rowItem(row: HTMLElement, tab: Element): RowItem {
  const origin = row.getBoundingClientRect().left + row.clientLeft - row.scrollLeft;
  const { left, right } = tab.getBoundingClientRect();
  return { start: left - origin, end: right - origin };
}

/** The room the faded edges (and the buttons, with a mouse) take: the row's scroll-padding in theme.css. */
function rowPadding(row: HTMLElement): RowPadding {
  const style = getComputedStyle(row);
  return { start: parseFloat(style.scrollPaddingLeft) || 0, end: parseFloat(style.scrollPaddingRight) || 0 };
}

/**
 * With a mouse, a button on each edge that hides tabs slides the row a page that way: otherwise a mouse reaches them
 * only with shift and the wheel. A touch screen slides the row with a finger and shows none (theme.css). The keyboard
 * already reaches every tab, each scrolling into view, and a screen reader lists them all, so the buttons stay out of
 * both, and a click on one leaves the focus where it was. Their title names them for the pointer.
 */
function SlideButton({ direction, onSlide }: { direction: 1 | -1; onSlide: (direction: 1 | -1) => void }) {
  return (
    <button
      type="button"
      className={`section-tabs-slide ${direction === 1 ? 'end' : 'start'}`}
      title={direction === 1 ? 'Ver las pestañas siguientes' : 'Ver las pestañas anteriores'}
      tabIndex={-1}
      aria-hidden="true"
      onMouseDown={(event) => event.preventDefault()}
      onClick={() => onSlide(direction)}
    >
      <i className={direction === 1 ? 'ri-arrow-right-s-line' : 'ri-arrow-left-s-line'}></i>
    </button>
  );
}

/**
 * The pages of the current section (Catálogo, Compras, Clientes, Reportes, Configuración) the person can open, as
 * tabs. When they do not fit, the row slides: the edge that hides tabs fades and, with a mouse, has a button.
 */
export default function SectionTabs() {
  const { pathname } = useLocation();
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const section = getSectionTabs(pathname, getAccess(user, capabilities), capabilities);
  const pages = section?.pages.map((page) => page.path).join() ?? '';
  const activePath = section?.pages.find((page) => isNavItemActive(page, pathname))?.path;
  const { ref, element: row, fade, fadeClass } = useScrollFade<HTMLElement>(pages);

  // The open page's tab shows as the page loads, clear of the faded edges, even at the end of a long row. Again when
  // the fonts are in, as they change the width of each tab (jsdom has no fonts).
  useLayoutEffect(() => {
    const active = row?.querySelector('[aria-current="page"]');
    if (!row || !active) return undefined;
    let mounted = true;
    const reveal = () => {
      const left = revealScroll(rowItem(row, active), row.scrollLeft, row.clientWidth, row.scrollWidth, rowPadding(row));
      if (mounted && left !== row.scrollLeft) row.scrollTo({ left, behavior: 'instant' });
    };
    reveal();
    document.fonts?.ready.then(reveal);
    return () => {
      mounted = false;
    };
  }, [row, activePath, pages]);

  const slide = (direction: 1 | -1) => {
    if (!row) return;
    const items = [...row.children].map((tab) => rowItem(row, tab));
    const left = pageScroll(direction, items, row.scrollLeft, row.clientWidth, row.scrollWidth, rowPadding(row));
    const still = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    row.scrollTo({ left, behavior: still ? 'instant' : 'smooth' });
  };

  if (!section) return null;

  return (
    <div className="section-tabs-frame">
      <nav ref={ref} className={`section-tabs ta-scroll-fade ${fadeClass}`} aria-label={section.label}>
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
      {fade.start && <SlideButton direction={-1} onSlide={slide} />}
      {fade.end && <SlideButton direction={1} onSlide={slide} />}
    </div>
  );
}
