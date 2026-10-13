import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import SectionTabs from './SectionTabs';

// An owner of a business with every module (63): Configuración has all its pages.
vi.mock('../../hooks/useAuth', () => ({ useAuth: () => ({ user: { role: 'TENANT_ADMIN' } }) }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: () => ({ capabilities: 63 }) }));

// jsdom lays nothing out: here each tab is 150 px wide, side by side in a row that shows 400 px.
const TAB = 150;
const SHOWN = 400;
let shown = SHOWN;
let scrolled = 0;
const scrollTo = vi.fn();
const originalScrollTo = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollTo');

const isRow = (element: Element) => element.classList.contains('section-tabs');

beforeEach(() => {
  scrolled = 0;
  shown = SHOWN;
  scrollTo.mockReset();
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(function (this: HTMLElement) {
    return isRow(this) ? shown : 0;
  });
  vi.spyOn(Element.prototype, 'scrollWidth', 'get').mockImplementation(function (this: Element) {
    return isRow(this) ? this.children.length * TAB : 0;
  });
  vi.spyOn(Element.prototype, 'scrollLeft', 'get').mockImplementation(() => scrolled);
  scrollTo.mockImplementation(({ left }: ScrollToOptions) => {
    scrolled = left ?? scrolled;
  });
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    const index = this.parentElement && isRow(this.parentElement) ? [...this.parentElement.children].indexOf(this) : -1;
    const left = index < 0 ? 0 : index * TAB - scrolled;
    return { left, right: left + (index < 0 ? shown : TAB), top: 0, bottom: 44, width: TAB, height: 44, x: left, y: 0 } as DOMRect;
  });
  Object.defineProperty(Element.prototype, 'scrollTo', { configurable: true, writable: true, value: scrollTo });
});

afterEach(() => {
  vi.restoreAllMocks();
  if (originalScrollTo) Object.defineProperty(Element.prototype, 'scrollTo', originalScrollTo);
  else delete (Element.prototype as Partial<Element>).scrollTo;
  Reflect.deleteProperty(window, 'matchMedia');
});

/** jsdom has no matchMedia: the person asks, or not, for less motion. */
function reducedMotion(reduce: boolean) {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: (query: string) => ({ matches: reduce && query.includes('reduce'), media: query }),
  });
}

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <SectionTabs />
    </MemoryRouter>,
  );
  return screen.getByRole('navigation');
}

describe('SectionTabs', () => {
  it('shows the open page tab when the page loads, even at the end of a long row', () => {
    const row = renderAt('/setup');
    const tabs = within(row).getAllByRole('link');

    const last = tabs[tabs.length - 1];
    expect(last).toHaveAccessibleName('Primeros pasos');
    expect(last).toHaveAttribute('aria-current', 'page');
    // Straight to the end of the row, where the last tab shows whole.
    expect(scrollTo).toHaveBeenCalledExactlyOnceWith({ left: tabs.length * TAB - SHOWN, behavior: 'instant' });
    // At the end only the start edge hides tabs.
    expect(row).toHaveClass('fade-start');
    expect(row).not.toHaveClass('fade-end');
    expect(screen.getByTitle('Ver las pestañas anteriores')).toBeInTheDocument();
    expect(screen.queryByTitle('Ver las pestañas siguientes')).not.toBeInTheDocument();
  });

  it('slides a page with the button on the edge that hides tabs', async () => {
    reducedMotion(false);
    const row = renderAt('/settings/business');

    // Its tab, the first, already shows: the row stays at the start.
    expect(scrollTo).not.toHaveBeenCalled();
    expect(row).toHaveClass('fade-end');
    await userEvent.click(screen.getByTitle('Ver las pestañas siguientes'));

    // The third tab (300 to 450 px) was cut at 400: the next page starts with it.
    expect(scrollTo).toHaveBeenCalledWith({ left: 2 * TAB, behavior: 'smooth' });
  });

  it('slides without motion for whoever asks for less', async () => {
    reducedMotion(true);
    renderAt('/settings/business');

    await userEvent.click(screen.getByTitle('Ver las pestañas siguientes'));

    expect(scrollTo).toHaveBeenCalledWith({ left: 2 * TAB, behavior: 'instant' });
  });

  it('keeps the buttons out of the keyboard and of screen readers, which reach every tab', () => {
    renderAt('/settings/business');

    expect(screen.getByTitle('Ver las pestañas siguientes')).toHaveAttribute('tabindex', '-1');
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('neither fades nor shows buttons when every tab fits', () => {
    // Wider than every page of Configuración side by side.
    shown = 20 * TAB;
    const row = renderAt('/settings/business');

    expect(row).not.toHaveClass('fade-start');
    expect(row).not.toHaveClass('fade-end');
    expect(screen.queryByTitle(/Ver las pestañas/)).not.toBeInTheDocument();
  });
});
