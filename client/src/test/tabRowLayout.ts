import { vi } from 'vitest';

/** How wide each tab is and how much of the row shows, in pixels. */
export const TAB = 150;
export const SHOWN = 400;

/**
 * jsdom lays nothing out: this puts the tabs of a row of tabs (`.section-tabs`, TabRow) side by side, each TAB wide, in
 * a row that shows SHOWN (or what `setShown` says), and makes scrolling the row move it. Call it before each test and
 * `restore` after.
 */
export function layOutTabRow() {
  let shown = SHOWN;
  let scrolled = 0;
  const scrollTo = vi.fn(({ left }: ScrollToOptions) => {
    scrolled = left ?? scrolled;
  });
  const originalScrollTo = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollTo');
  const isRow = (element: Element) => element.classList.contains('section-tabs');

  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(function (this: HTMLElement) {
    return isRow(this) ? shown : 0;
  });
  vi.spyOn(Element.prototype, 'scrollWidth', 'get').mockImplementation(function (this: Element) {
    return isRow(this) ? this.children.length * TAB : 0;
  });
  vi.spyOn(Element.prototype, 'scrollLeft', 'get').mockImplementation(() => scrolled);
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    const index = this.parentElement && isRow(this.parentElement) ? [...this.parentElement.children].indexOf(this) : -1;
    const left = index < 0 ? 0 : index * TAB - scrolled;
    return { left, right: left + (index < 0 ? shown : TAB), top: 0, bottom: 44, width: TAB, height: 44, x: left, y: 0 } as DOMRect;
  });
  Object.defineProperty(Element.prototype, 'scrollTo', { configurable: true, writable: true, value: scrollTo });

  return {
    scrollTo,
    setShown: (width: number) => {
      shown = width;
    },
    restore: () => {
      vi.restoreAllMocks();
      if (originalScrollTo) Object.defineProperty(Element.prototype, 'scrollTo', originalScrollTo);
      else delete (Element.prototype as Partial<Element>).scrollTo;
      Reflect.deleteProperty(window, 'matchMedia');
    },
  };
}

/** jsdom has no matchMedia: the person asks, or not, for less motion. */
export function reducedMotion(reduce: boolean) {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: (query: string) => ({ matches: reduce && query.includes('reduce'), media: query }),
  });
}
