import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Link, MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Layout from './Layout';

// The frame only: what each bar draws has its own tests.
vi.mock('./Topbar', () => ({
  default: ({ onToggleSidebar, isMobileMenuOpen }: { onToggleSidebar: () => void; isMobileMenuOpen: boolean }) => (
    <button type="button" onClick={onToggleSidebar} aria-expanded={isMobileMenuOpen}>Mostrar u ocultar el menú</button>
  ),
}));
vi.mock('./LeftSidebar', () => ({
  default: ({ onClose }: { onClose: () => void }) => <button type="button" onClick={onClose}>Cerrar el menú</button>,
}));
vi.mock('./BottomTabBar', () => ({ default: () => null }));
vi.mock('./GlobalSearch', () => ({ default: () => null }));
vi.mock('./PastDueBanner', () => ({ default: () => null }));
vi.mock('../sales/PendingSalesSync', () => ({ default: () => null }));
vi.mock('../../hooks/useNewVersion', () => ({ useNewVersion: () => false }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: () => ({ user: { role: 'TENANT_ADMIN' } }) }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: () => ({ tenant: { name: 'Café La Esquina', status: 0 }, capabilities: null }) }));

const html = document.documentElement;
const toggle = () => screen.getByRole('button', { name: 'Mostrar u ocultar el menú' });

/** jsdom has no matchMedia: a screen of this width answers the max-width queries of the layout. */
function screenOf(width: number) {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: (query: string) => ({
      matches: width <= Number(/max-width:\s*([\d.]+)px/.exec(query)?.[1] ?? Infinity),
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }),
  });
}

function Page() {
  const { pathname } = useLocation();
  return (
    <>
      <p>Pantalla {pathname}</p>
      <Link to="/otra">Ir a otra pantalla</Link>
    </>
  );
}

function renderLayout() {
  return render(
    <MemoryRouter initialEntries={['/inicio']}>
      <Routes>
        <Route element={<Layout />}>
          <Route path="*" element={<Page />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  window.localStorage.clear();
});

afterEach(() => {
  html.removeAttribute('data-ta-sidenav');
});

describe('Layout', () => {
  it('shows the whole menu on a desktop; the button condenses it and the device remembers it', async () => {
    screenOf(1440);
    const { unmount } = renderLayout();
    expect(html).toHaveAttribute('data-ta-sidenav', 'default');

    await userEvent.click(toggle());
    expect(html).toHaveAttribute('data-ta-sidenav', 'condensed');
    expect(html).not.toHaveClass('ta-sidebar-open');
    expect(window.localStorage.getItem('tienditapp.sidenavSize')).toBe('condensed');

    unmount();
    renderLayout();
    expect(html).toHaveAttribute('data-ta-sidenav', 'condensed');
  });

  it('starts condensed on a tablet, so the page keeps its room', () => {
    screenOf(1024);
    renderLayout();
    expect(html).toHaveAttribute('data-ta-sidenav', 'condensed');
  });

  it('hides the menu on a phone and slides it over the page, whatever size the device remembers', async () => {
    window.localStorage.setItem('tienditapp.sidenavSize', 'default');
    screenOf(390);
    const { container } = renderLayout();
    expect(html).toHaveAttribute('data-ta-sidenav', 'full');
    expect(html).not.toHaveClass('ta-sidebar-open');
    expect(container.querySelector('.offcanvas-backdrop')).toBeNull();

    await userEvent.click(toggle());
    expect(html).toHaveClass('ta-sidebar-open');
    expect(toggle()).toHaveAttribute('aria-expanded', 'true');
    // The page under it does not scroll while it is open.
    expect(document.body.style.overflow).toBe('hidden');
    expect(container.querySelector('.offcanvas-backdrop')).not.toBeNull();
  });

  it('closes the phone menu with Escape, the backdrop, its close button, the menu button or a new screen', async () => {
    screenOf(390);
    const { container } = renderLayout();
    const isOpen = () => html.classList.contains('ta-sidebar-open');

    await userEvent.click(toggle());
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(isOpen()).toBe(false);
    expect(document.body.style.overflow).toBe('');

    await userEvent.click(toggle());
    await userEvent.click(container.querySelector('.offcanvas-backdrop')!);
    expect(isOpen()).toBe(false);

    await userEvent.click(toggle());
    await userEvent.click(screen.getByRole('button', { name: 'Cerrar el menú' }));
    expect(isOpen()).toBe(false);

    await userEvent.click(toggle());
    await userEvent.click(toggle());
    expect(isOpen()).toBe(false);

    await userEvent.click(toggle());
    await userEvent.click(screen.getByRole('link', { name: 'Ir a otra pantalla' }));
    expect(screen.getByText('Pantalla /otra')).toBeInTheDocument();
    expect(isOpen()).toBe(false);
  });

  it('leaves <html> and the page as they were when it goes away', async () => {
    screenOf(390);
    const { unmount } = renderLayout();
    await userEvent.click(toggle());

    unmount();
    expect(html).not.toHaveClass('ta-sidebar-open');
    expect(document.body.style.overflow).toBe('');
  });
});
