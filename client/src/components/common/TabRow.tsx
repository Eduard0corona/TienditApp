import { useLayoutEffect, useRef, type ReactNode } from 'react';
import { useScrollFade } from '../../hooks/useScrollFade';
import { pageScroll, revealScroll, type RowItem, type RowPadding } from '../../utils/scrollRow';

/** The open tab: the link of the page that shows (aria-current) or the part of the screen that shows (aria-selected). */
const OPEN_TAB = '[aria-current="page"], [aria-selected="true"]';

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

/** A slide the person sees, unless they ask for less motion. */
function slideBehavior(): ScrollBehavior {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth';
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

interface TabRowProps {
  /** What the tabs are, for a screen reader: the section (Configuración) or the screen whose parts they open. */
  label: string;
  /**
   * 'tablist' when the tabs open parts of one screen (Inventario's buttons, role tab with aria-selected). Without it
   * the row is a navigation and its tabs open pages (SectionTabs' links, aria-current on the open one).
   */
  role?: 'tablist';
  /** Changes when the tabs do (another section, a page the person can now open): the row is measured again. */
  contentKey: string;
  /** The open tab, which the row keeps in view. */
  activeKey: string | undefined;
  children: ReactNode;
}

/**
 * A row of tabs (`.section-tab`) under a line, as a menu section's pages (SectionTabs) or the parts of one screen
 * (Inventario). When they do not fit, the row slides: the edge that hides tabs fades and, with a mouse, has a button.
 * The frame draws the line, so it runs whole under the faded edges, and holds the buttons, outside the row: a tablist
 * holds only its tabs.
 */
export default function TabRow({ label, role, contentKey, activeKey, children }: TabRowProps) {
  const { ref, element: row, fade, fadeClass } = useScrollFade<HTMLElement>(contentKey);
  // The row and open tab last brought into view: the same row with another tab open is a tab opened in place.
  const shown = useRef<{ row: HTMLElement; activeKey: string | undefined } | null>(null);

  // The open tab shows as the row draws, clear of the faded edges, even at the end of a long row: at once, before
  // paint, and again when the fonts are in, as they change the width of each tab (jsdom has no fonts). A tab opened
  // in place (a part of Inventario cut by an edge) slides into view the way the buttons slide the row.
  useLayoutEffect(() => {
    const active = row?.querySelector(OPEN_TAB);
    if (!row || !active) return undefined;
    const openedInPlace = shown.current?.row === row && shown.current.activeKey !== activeKey;
    shown.current = { row, activeKey };
    let mounted = true;
    const reveal = (slid: boolean) => {
      const left = revealScroll(rowItem(row, active), row.scrollLeft, row.clientWidth, row.scrollWidth, rowPadding(row));
      if (mounted && left !== row.scrollLeft) row.scrollTo({ left, behavior: slid ? slideBehavior() : 'instant' });
    };
    reveal(openedInPlace);
    if (!openedInPlace) document.fonts?.ready.then(() => reveal(false));
    return () => {
      mounted = false;
    };
  }, [row, activeKey, contentKey]);

  const slide = (direction: 1 | -1) => {
    if (!row) return;
    const items = [...row.children].map((tab) => rowItem(row, tab));
    const left = pageScroll(direction, items, row.scrollLeft, row.clientWidth, row.scrollWidth, rowPadding(row));
    row.scrollTo({ left, behavior: slideBehavior() });
  };

  const rowClass = `section-tabs ta-scroll-fade ${fadeClass}`;
  return (
    <div className={`section-tabs-frame${fade.start || fade.end ? ' slides' : ''}`}>
      {role === 'tablist' ? (
        <div ref={ref} className={rowClass} role="tablist" aria-label={label}>
          {children}
        </div>
      ) : (
        <nav ref={ref} className={rowClass} aria-label={label}>
          {children}
        </nav>
      )}
      {fade.start && <SlideButton direction={-1} onSlide={slide} />}
      {fade.end && <SlideButton direction={1} onSlide={slide} />}
    </div>
  );
}
