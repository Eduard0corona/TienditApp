import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { publicMenuApi, type PublicMenu } from '../../api/publicMenuApi';
import { BusinessTheme, DayOfWeek } from '../../types';
import { getBusinessTheme } from '../../utils/businessThemes';
import { applyBusinessTheme } from '../../utils/theme';
import PublicMenuPage from './PublicMenuPage';

vi.mock('../../api/publicMenuApi', () => ({
  publicMenuApi: {
    getMenu: vi.fn(),
    placeOrder: vi.fn(),
  },
}));

const api = vi.mocked(publicMenuApi);

beforeAll(() => {
  // jsdom has no matchMedia, and the cart sheet (Offcanvas) asks it for the screen size.
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList,
  });
});

// The page reads only these fields of the menu.
function menu(overrides: Partial<PublicMenu> = {}): PublicMenu {
  return {
    businessName: 'Café Luna',
    slug: 'cafe-luna',
    currency: 'MXN',
    pricesIncludeTax: true,
    defaultTaxRate: 16,
    acceptsOrders: true,
    ordersPaused: false,
    categories: [{
      id: 'c1',
      name: 'Bebidas',
      products: [{ id: 'p1', name: 'Latte', price: 55, isTaxExempt: false, isFeatured: false, availableNow: true, optionGroups: [] }],
    }],
    ...overrides,
  } as PublicMenu;
}

const mondayToSaturday = [
  DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
].map((day) => ({ day, opens: '09:00', closes: '20:00' }));

function renderPage(path = '/m/cafe-luna') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/m/:slug" element={<PublicMenuPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PublicMenuPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('shows the menu in the colors of its business, and gives the browser back its own when leaving', async () => {
    // Someone of another business is signed in on this browser.
    applyBusinessTheme(BusinessTheme.Grafito);
    api.getMenu.mockResolvedValue(menu({ theme: BusinessTheme.Jamaica }));
    const accent = () => document.documentElement.style.getPropertyValue('--tt-accent');

    const { unmount } = renderPage();
    expect(await screen.findByRole('heading', { name: 'Café Luna' })).toBeInTheDocument();
    expect(accent()).toBe(getBusinessTheme(BusinessTheme.Jamaica).light.accent);

    unmount();
    expect(accent()).toBe(getBusinessTheme(BusinessTheme.Grafito).light.accent);
    applyBusinessTheme(null);
  });

  it('takes orders while the business receives them', async () => {
    api.getMenu.mockResolvedValue(menu());

    const { container } = renderPage();

    expect(await screen.findByRole('button', { name: 'Agregar Latte' })).toBeInTheDocument();
    // No logo: the initial of the business. No product has a photo: no placeholders either.
    expect(container.querySelector('.pm-header .photo-initial')).toHaveTextContent('C');
    expect(container.querySelector('.pm-product .photo-initial')).toBeNull();
    expect(api.getMenu).toHaveBeenCalledWith('cafe-luna', undefined);
    expect(screen.queryByText(/no estamos recibiendo pedidos/)).not.toBeInTheDocument();
    // A business without hours or address shows neither.
    expect(screen.queryByText(/^Hoy:/)).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Cómo llegar/ })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Agregar Latte' }));
    await userEvent.click(screen.getByRole('button', { name: /^Ver pedido/ }));
    expect(within(screen.getByRole('dialog', { name: 'Tu pedido' })).getByText('Latte')).toBeInTheDocument();
  });

  it('puts the initial where a product has no photo while others have one', async () => {
    api.getMenu.mockResolvedValue(menu({
      logoUrl: 'https://cdn/luna_md.webp',
      categories: [{
        id: 'c1',
        name: 'Bebidas',
        products: [
          { id: 'p1', name: 'Latte', price: 55, isTaxExempt: false, isFeatured: false, availableNow: true, optionGroups: [], imageUrl: 'https://cdn/latte_md.webp' },
          { id: 'p2', name: 'Capuchino', price: 60, isTaxExempt: false, isFeatured: false, availableNow: true, optionGroups: [] },
        ],
      }],
    }));

    const { container } = renderPage();

    await screen.findByText('Capuchino');
    expect(container.querySelector('.pm-header img')).toHaveAttribute('src', 'https://cdn/luna_md.webp');
    const [latte, capuchino] = Array.from(container.querySelectorAll('.pm-product'));
    expect(latte.querySelector('img')).toHaveAttribute('src', 'https://cdn/latte_md.webp');
    expect(capuchino.querySelector('.photo-initial')).toHaveTextContent('C');
  });

  it('fades the edge of the categories that hides more of them', async () => {
    // jsdom lays nothing out: a row of 900 px seen through 400 px.
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(400);
    vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockReturnValue(900);
    api.getMenu.mockResolvedValue(menu());

    renderPage();

    const categories = await screen.findByRole('navigation', { name: 'Categorías' });
    // The bar keeps its background; only the row of chips inside fades.
    expect(categories).not.toHaveClass('fade-end');
    expect(within(categories).getByRole('button', { name: 'Todo' }).parentElement).toHaveClass('fade-end');
  });

  it('shows whether it is open now, its hours of the week and how to get there', async () => {
    api.getMenu.mockResolvedValue(menu({
      address: 'Av. Juárez 123, Centro, 72000 Puebla, Puebla',
      openingHours: {
        intervals: mondayToSaturday,
        today: DayOfWeek.Saturday,
        isOpen: true,
        closesAt: '20:00',
        closesInDays: 0,
      },
    }));

    renderPage();

    expect(await screen.findByText('Abierto')).toHaveClass('ta-chip');
    expect(screen.getByText('cierra a las 20:00')).toBeInTheDocument();
    await userEvent.click(screen.getByText('Hoy: 9:00 a 20:00'));
    const week = screen.getByRole('list', { name: 'Horario de la semana' });
    expect(within(week).getAllByRole('listitem')).toHaveLength(7);
    expect(within(week).getByText('Domingo').closest('li')).toHaveTextContent('Cerrado');
    expect(within(week).getByText('Sábado').closest('li')).toHaveAttribute('aria-current', 'date');
    expect(screen.getByText('Av. Juárez 123, Centro, 72000 Puebla, Puebla')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Cómo llegar/ })).toHaveAttribute(
      'href',
      `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent('Av. Juárez 123, Centro, 72000 Puebla, Puebla')}`,
    );
    expect(screen.getByRole('button', { name: 'Agregar Latte' })).toBeInTheDocument();
  });

  it('says when it opens while closed and takes no orders for pickup', async () => {
    api.getMenu.mockResolvedValue(menu({
      acceptsOrders: false,
      ordersPaused: true,
      openingHours: {
        intervals: mondayToSaturday,
        today: DayOfWeek.Sunday,
        isOpen: false,
        opensAt: '09:00',
        opensInDays: 1,
      },
    }));

    renderPage();

    expect(await screen.findByText('Cerrado', { selector: '.ta-chip' })).toBeInTheDocument();
    expect(screen.getByText('abre mañana a las 9:00')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(
      'Fuera de horario no recibimos pedidos en línea: vuelve mañana a las 9:00. Mientras, puedes ver el menú.',
    );
    expect(screen.queryByText(/no estamos recibiendo pedidos/)).not.toBeInTheDocument();
    expect(screen.getByText('Latte')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Agregar Latte' })).not.toBeInTheDocument();
  });

  it('takes the order of a customer at a table outside the hours', async () => {
    api.getMenu.mockResolvedValue(menu({
      openingHours: {
        intervals: mondayToSaturday,
        today: DayOfWeek.Sunday,
        isOpen: false,
        opensAt: '09:00',
        opensInDays: 1,
      },
    }));

    renderPage('/m/cafe-luna?mesa=Mesa%204');

    expect(await screen.findByRole('button', { name: 'Agregar Latte' })).toBeInTheDocument();
    // The API decides: a customer seated at one of the business's tables orders at any hour.
    expect(api.getMenu).toHaveBeenCalledWith('cafe-luna', 'Mesa 4');
    expect(screen.getByText('Pedido para Mesa 4')).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('shows the menu without taking orders while they are paused (no drawer open)', async () => {
    api.getMenu.mockResolvedValue(menu({ acceptsOrders: false, ordersPaused: true }));

    renderPage();

    expect(await screen.findByText('Por ahora no estamos recibiendo pedidos en línea. Puedes ver el menú y volver más tarde.'))
      .toBeInTheDocument();
    expect(screen.getByText('Latte')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Agregar Latte' })).not.toBeInTheDocument();
  });
});
