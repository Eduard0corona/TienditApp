import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi } from '../../api/accountApi';
import { publicMenuApi, type PublicMenu } from '../../api/publicMenuApi';
import { AuthContext, type AuthContextType } from '../../contexts/AuthContextDefinition';
import { BusinessTheme, DayOfWeek, SpecialDayKind } from '../../types';
import { getBusinessTheme } from '../../utils/businessThemes';
import { readCart, saveCart } from '../../utils/savedCart';
import { applyBusinessTheme } from '../../utils/theme';
import PublicMenuPage from './PublicMenuPage';

vi.mock('../../api/publicMenuApi', () => ({
  publicMenuApi: {
    getMenu: vi.fn(),
    getDeliveryQuote: vi.fn(),
    placeOrder: vi.fn(),
  },
}));

vi.mock('../../api/accountApi', () => ({
  MAX_ACCOUNT_ADDRESSES: 10,
  accountApi: { getAddresses: vi.fn(), saveAddress: vi.fn(), updateAddress: vi.fn(), deleteAddress: vi.fn() },
}));

const api = vi.mocked(publicMenuApi);
const account = vi.mocked(accountApi);

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

// Christmas Day as this API words it (todayDate present): closed by a special day, open again on Saturday 26.
const christmasDay = {
  intervals: mondayToSaturday,
  today: DayOfWeek.Friday,
  isOpen: false,
  opensAt: '09:00',
  opensInDays: 1,
  todayDate: '2026-12-25',
  hasWeeklyHours: true,
  statusLabel: 'Cerrado',
  statusDetail: 'por Navidad · abre mañana a las 9:00',
  closedNotice: 'No recibimos pedidos en línea por Navidad: vuelve mañana a las 9:00. Mientras, puedes ver el menú.',
  upcomingNotice: null,
  closedBySpecialDay: true,
  todayHours: { date: '2026-12-25', closed: true, special: true, openings: [], reason: 'Navidad' },
  opensOn: '2026-12-26',
  specialDays: [
    { from: '2026-12-31', to: '2026-12-31', kind: SpecialDayKind.SpecialHours, openings: [{ opens: '09:00', closes: '18:00' }], reason: 'Fin de año' },
  ],
};

function renderPage(path = '/m/cafe-luna', auth?: Partial<AuthContextType>, from?: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  // `from`: the page of the app the customer came from (Explorar), one step back in the history.
  const page = (
    <MemoryRouter initialEntries={from ? [from, path] : [path]} initialIndex={from ? 1 : 0}>
      <Routes>
        <Route path="/m/:slug" element={<PublicMenuPage />} />
      </Routes>
    </MemoryRouter>
  );
  return render(
    <QueryClientProvider client={queryClient}>
      {auth ? <AuthContext.Provider value={auth as AuthContextType}>{page}</AuthContext.Provider> : page}
    </QueryClientProvider>,
  );
}

/** Signed in with a customer account (Fase 24B), or with one of a business's team. */
const signedInAs = (role: 'Customer' | 'TenantAdmin'): Partial<AuthContextType> => ({
  isAuthenticated: true,
  isLoading: false,
  user: {
    id: 'u1', email: 'mariana@demo.com', firstName: 'Mariana', lastName: 'Ortiz', fullName: 'Mariana Ortiz',
    phoneNumber: '+5212221234567', phoneNumberConfirmed: false, emailConfirmed: true, role, permissions: [],
  } as unknown as AuthContextType['user'],
});

describe('PublicMenuPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    // The cart stays on the device (Fase 24B): each test starts without one.
    window.localStorage.clear();
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
    expect(container.querySelector('.pm-store .photo-initial')).toHaveTextContent('C');
    expect(container.querySelector('.pm-item .photo-initial')).toBeNull();
    // Without a cover, the colors of the business across the top.
    expect(container.querySelector('.pm-cover')).not.toHaveClass('has-photo');
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
    expect(container.querySelector('img.pm-logo')).toHaveAttribute('src', 'https://cdn/luna_md.webp');
    const [latte, capuchino] = Array.from(container.querySelectorAll('.pm-item'));
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
    // The bar keeps its background; only the row of tabs inside fades.
    expect(categories).not.toHaveClass('fade-end');
    expect(within(categories).getByRole('button', { name: 'Bebidas' }).parentElement).toHaveClass('fade-end');
  });

  it('says a kitchen only delivers, with no address and no way to get there', async () => {
    // An address next to the flag (an API that sent both) is not shown either.
    api.getMenu.mockResolvedValue(menu({ deliveryOnly: true, address: 'Privada Los Pinos 8, Puebla' }));

    renderPage();

    expect(await screen.findByText('Solo a domicilio')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Información' }));
    const info = screen.getByRole('dialog', { name: 'Café Luna' });
    expect(within(info).getByText('Solo a domicilio: no tenemos local para visitar.')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Cómo llegar/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/Los Pinos/)).not.toBeInTheDocument();
    // The order is the same: the customer still orders online.
    expect(screen.getByRole('button', { name: 'Agregar Latte' })).toBeInTheDocument();
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

  it('leads to the booking page of a business that books appointments online, but not from a table', async () => {
    api.getMenu.mockResolvedValue(menu({ acceptsOrders: false, booksOnline: true }));

    const { unmount } = renderPage();

    const booking = await screen.findByRole('link', { name: 'Agenda tu cita' });
    expect(booking).toHaveAttribute('href', '/m/cafe-luna/agendar');
    // Without orders, booking is what the page is for.
    expect(booking).toHaveClass('btn-primary');
    unmount();

    renderPage('/m/cafe-luna?mesa=Mesa%204');
    await screen.findByText('Latte');
    expect(screen.queryByRole('link', { name: 'Agenda tu cita' })).not.toBeInTheDocument();
  });

  it('shows the menu without taking orders while they are paused (no drawer open)', async () => {
    api.getMenu.mockResolvedValue(menu({ acceptsOrders: false, ordersPaused: true }));

    renderPage();

    expect(await screen.findByText('Por ahora no estamos recibiendo pedidos en línea. Puedes ver el menú y volver más tarde.'))
      .toBeInTheDocument();
    expect(screen.getByText('Latte')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Agregar Latte' })).not.toBeInTheDocument();
  });

  it('shows the words the API writes about its hours and why it is closed', async () => {
    api.getMenu.mockResolvedValue(menu({ acceptsOrders: false, ordersPaused: true, openingHours: christmasDay }));

    renderPage();

    // Not "abre mañana a las 9:00" worked out here: the reason comes from the API.
    expect(await screen.findByText('Cerrado', { selector: '.ta-chip' })).toBeInTheDocument();
    expect(screen.getByText('por Navidad · abre mañana a las 9:00')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(
      'No recibimos pedidos en línea por Navidad: vuelve mañana a las 9:00. Mientras, puedes ver el menú.',
    );
    expect(screen.queryByText(/Fuera de horario/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Agregar Latte' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByText('Hoy: Cerrado · Navidad'));
    expect(within(screen.getByRole('list', { name: 'Horario de la semana' })).getAllByRole('listitem')).toHaveLength(7);
    expect(within(screen.getByRole('list', { name: 'Días especiales' })).getByRole('listitem'))
      .toHaveTextContent('Jue 31 dic · 9:00 a 18:00 · Fin de año');
  });

  it('takes no order at a table on a special day the business closes', async () => {
    api.getMenu.mockResolvedValue(menu({ acceptsOrders: false, ordersPaused: true, openingHours: christmasDay }));

    renderPage('/m/cafe-luna?mesa=Mesa%204');

    expect(await screen.findByText('Pedido para Mesa 4')).toBeInTheDocument();
    expect(api.getMenu).toHaveBeenCalledWith('cafe-luna', 'Mesa 4');
    expect(screen.getByRole('status')).toHaveTextContent('No recibimos pedidos en línea por Navidad');
    expect(screen.queryByRole('button', { name: 'Agregar Latte' })).not.toBeInTheDocument();
  });

  it('tells the customer at a table that they can still order outside the hours', async () => {
    api.getMenu.mockResolvedValue(menu({
      openingHours: {
        ...christmasDay,
        today: DayOfWeek.Thursday,
        todayDate: '2026-12-24',
        statusDetail: 'tu mesa sí puede pedir',
        closedNotice: null,
        closedBySpecialDay: false,
        todayHours: { date: '2026-12-24', closed: false, special: true, openings: [{ opens: '09:00', closes: '18:00' }], reason: 'Nochebuena' },
      },
    }));

    renderPage('/m/cafe-luna?mesa=Mesa%204');

    expect(await screen.findByText('Cerrado', { selector: '.ta-chip' })).toBeInTheDocument();
    expect(screen.getByText('tu mesa sí puede pedir')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Agregar Latte' })).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('shows today\'s special hours and the next special day', async () => {
    api.getMenu.mockResolvedValue(menu({
      openingHours: {
        intervals: mondayToSaturday,
        today: DayOfWeek.Thursday,
        isOpen: true,
        closesAt: '18:00',
        closesInDays: 0,
        todayDate: '2026-12-24',
        hasWeeklyHours: true,
        statusLabel: 'Abierto',
        statusDetail: 'cierra a las 18:00 por Nochebuena',
        upcomingNotice: 'El viernes 25 cerramos por Navidad.',
        closedBySpecialDay: false,
        todayHours: { date: '2026-12-24', closed: false, special: true, openings: [{ opens: '09:00', closes: '18:00' }], reason: 'Nochebuena' },
        closesOn: '2026-12-24',
        specialDays: [{ from: '2026-12-25', to: '2026-12-25', kind: SpecialDayKind.Closed, openings: [], reason: 'Navidad' }],
      },
    }));

    renderPage();

    expect(await screen.findByText('Abierto', { selector: '.ta-chip' })).toBeInTheDocument();
    expect(screen.getByText('cierra a las 18:00 por Nochebuena')).toBeInTheDocument();
    expect(screen.getByText('El viernes 25 cerramos por Navidad.')).toBeInTheDocument();
    await userEvent.click(screen.getByText('Hoy: 9:00 a 18:00 · Nochebuena'));
    expect(within(screen.getByRole('list', { name: 'Días especiales' })).getByRole('listitem'))
      .toHaveTextContent('Vie 25 dic · Cerrado · Navidad');
    expect(screen.getByRole('button', { name: 'Agregar Latte' })).toBeInTheDocument();
  });

  it('shows only the special days of a business without weekly hours, with no status', async () => {
    api.getMenu.mockResolvedValue(menu({
      openingHours: {
        intervals: [],
        today: DayOfWeek.Monday,
        isOpen: true,
        closesAt: null,
        todayDate: '2026-12-14',
        hasWeeklyHours: false,
        statusLabel: null,
        statusDetail: null,
        closedBySpecialDay: false,
        specialDays: [{ from: '2026-12-21', to: '2027-01-04', kind: SpecialDayKind.Closed, openings: [], reason: 'Vacaciones' }],
      },
    }));

    const { container } = renderPage();

    expect(await screen.findByText('Pide en línea')).toBeInTheDocument();
    // "Abierto las 24 horas" would only mean that no special day closes it today.
    expect(container.querySelector('.ta-chip')).toBeNull();
    expect(screen.queryByText(/^Hoy:/)).not.toBeInTheDocument();
    await userEvent.click(screen.getByText('Días especiales'));
    expect(screen.queryByRole('list', { name: 'Horario de la semana' })).not.toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Días especiales' })).getByRole('listitem'))
      .toHaveTextContent('Lun 21 dic al lun 4 ene · Cerrado · Vacaciones');
    expect(screen.getByRole('button', { name: 'Agregar Latte' })).toBeInTheDocument();
  });

  it('says today in a plain line when there is no week and no special day ahead to open', async () => {
    api.getMenu.mockResolvedValue(menu({
      acceptsOrders: false,
      ordersPaused: true,
      openingHours: {
        intervals: [],
        today: DayOfWeek.Monday,
        isOpen: false,
        todayDate: '2026-10-12',
        hasWeeklyHours: false,
        statusLabel: 'Cerrado',
        statusDetail: 'por Inventario',
        closedNotice: 'No recibimos pedidos en línea por Inventario. Mientras, puedes ver el menú.',
        closedBySpecialDay: true,
        todayHours: { date: '2026-10-12', closed: true, special: true, openings: [], reason: 'Inventario' },
        specialDays: [],
      },
    }));

    const { container } = renderPage();

    // Today in one line, and the store's information has neither a week nor special days to list.
    await userEvent.click(await screen.findByRole('button', { name: 'Hoy: Cerrado · Inventario' }));
    const info = screen.getByRole('dialog', { name: 'Café Luna' });
    expect(within(info).getByText('Cerrado', { selector: '.ta-chip' })).toBeInTheDocument();
    expect(within(info).queryByRole('list')).not.toBeInTheDocument();
    expect(container.querySelector('details')).toBeNull();
  });

  describe('con cuenta y carrito guardado (Fase 24B)', () => {
    const receipt = (overrides = {}) => ({
      orderNumber: 'ORD-9', total: 110, deliveryFee: null, confirmed: false, businessName: 'Café Luna', contactPhone: null,
      linkedToAccount: false, trackingToken: null, ...overrides,
    });

    it('keeps the cart on the device, and brings back only what the menu has now', async () => {
      saveCart('cafe-luna', {
        businessName: 'Café Luna',
        lines: [{ productId: 'p1', optionIds: [], quantity: 2 }, { productId: 'ya-no', optionIds: [], quantity: 1 }],
        reorderOf: 3,
        skipped: 1,
      });
      api.getMenu.mockResolvedValue(menu());

      renderPage();

      expect(await screen.findByRole('button', { name: /^Ver pedido · 2/ })).toHaveTextContent('$110.00');
      expect(screen.getByRole('status', { name: '' })).toHaveTextContent(
        'Agregamos lo que sigue disponible; 1 producto ya no está en el menú de ahora; 1 producto se pide por peso: agrégalo con su cantidad.',
      );
      // What is in the cart now replaces what came back, without the notice of «Pedir otra vez».
      await userEvent.click(screen.getByRole('button', { name: /^Agregar Latte/ }));
      expect(readCart('cafe-luna')).toEqual(expect.objectContaining({ lines: [{ productId: 'p1', optionIds: [], quantity: 3 }] }));
      expect(readCart('cafe-luna')?.reorderOf).toBeUndefined();
      expect(readCart('cafe-luna')?.skipped).toBeUndefined();
    });

    it('sends the order with a key, the same one when it is sent again, and forgets the cart once it is placed', async () => {
      api.getMenu.mockResolvedValue(menu());
      api.placeOrder.mockRejectedValueOnce(new Error('Network Error'))
        .mockResolvedValue(receipt({ trackingToken: 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE' }) as never);

      renderPage();
      await userEvent.click(await screen.findByRole('button', { name: /^Agregar Latte/ }));
      await userEvent.click(screen.getByRole('button', { name: /^Ver pedido/ }));
      const cart = screen.getByRole('dialog', { name: 'Tu pedido' });
      await userEvent.type(within(cart).getByLabelText('Tu nombre'), 'Ana');
      await userEvent.type(within(cart).getByLabelText('Teléfono (WhatsApp)'), '5512345678');
      await userEvent.click(within(cart).getByRole('button', { name: /^Hacer pedido/ }));
      expect(await within(cart).findByText(/No se pudo enviar el pedido/)).toBeInTheDocument();
      await userEvent.click(within(cart).getByRole('button', { name: /^Hacer pedido/ }));

      expect(await screen.findByText('¡Pedido enviado!')).toBeInTheDocument();
      const [first, second] = api.placeOrder.mock.calls.map(([, body]) => body.idempotencyKey);
      expect(first).toMatch(/^[0-9a-f-]{36}$/);
      expect(second).toBe(first);
      expect(readCart('cafe-luna')).toBeNull();
      // A visitor is invited to create an account; nothing is said about «Mis pedidos».
      expect(screen.getByRole('link', { name: 'Crea tu cuenta' })).toHaveAttribute('href', '/register?cuenta=cliente');
      expect(screen.queryByRole('link', { name: 'Ver mis pedidos' })).not.toBeInTheDocument();
      // The link to follow it, in the fragment: it never reaches a server log (Fase 24C).
      expect(screen.getByRole('link', { name: 'Seguir tu pedido' }))
        .toHaveAttribute('href', '/seguimiento#AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE');
      expect(screen.getByText('Guarda el enlace de seguimiento para ver cómo va tu pedido.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Copiar enlace de seguimiento' })).toBeInTheDocument();
    });

    it('writes the account\'s name and phone, offers its last address and keeps a new one when asked', async () => {
      api.getMenu.mockResolvedValue(menu({ deliveryAvailable: true }));
      api.getDeliveryQuote.mockImplementation(async (_slug, postalCode) => ({
        postalCode, covered: true, fee: 0, minimumOrder: 0, freeDeliveryFrom: null,
      }));
      account.getAddresses.mockResolvedValue([
        { id: 'a1', label: 'Casa', postalCode: '72000', addressLine: 'Av. Juárez 120', references: 'Portón verde', lastUsedAt: '2026-10-12T19:00:00Z' },
        { id: 'a2', label: 'Oficina', postalCode: '72160', addressLine: 'Calle 5 de Mayo 8', references: null, lastUsedAt: null },
      ]);
      api.placeOrder.mockResolvedValue(receipt({ linkedToAccount: true, trackingToken: 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE' }) as never);

      renderPage('/m/cafe-luna', signedInAs('Customer'));
      await userEvent.click(await screen.findByRole('button', { name: /^Agregar Latte/ }));
      await userEvent.click(screen.getByRole('button', { name: /^Ver pedido/ }));
      const cart = screen.getByRole('dialog', { name: 'Tu pedido' });

      expect(within(cart).getByLabelText('Tu nombre')).toHaveValue('Mariana Ortiz');
      expect(within(cart).getByLabelText('Teléfono (WhatsApp)')).toHaveValue('2221234567');
      await userEvent.click(within(cart).getByRole('radio', { name: 'A domicilio' }));
      expect(await within(cart).findByRole('radio', { name: /^Casa/ })).toHaveAttribute('aria-checked', 'true');
      // A saved address needs nothing typed, and is never saved twice.
      expect(within(cart).queryByLabelText('Dirección')).not.toBeInTheDocument();
      expect(await within(cart).findByText('Envío gratis')).toBeInTheDocument();

      // Another one, kept with a name.
      await userEvent.click(within(cart).getByRole('radio', { name: 'Otra dirección' }));
      await userEvent.type(within(cart).getByLabelText('Código postal'), '72100');
      await userEvent.type(within(cart).getByLabelText('Dirección'), 'Calle Nueva 1');
      expect(within(cart).getByLabelText('Guardar esta dirección en mi cuenta')).toBeChecked();
      await userEvent.type(within(cart).getByLabelText('Nombre de la dirección'), 'Casa de mi mamá');
      await userEvent.click(await within(cart).findByRole('button', { name: /^Hacer pedido/ }));

      expect(api.placeOrder).toHaveBeenCalledWith('cafe-luna', expect.objectContaining({
        customerName: 'Mariana Ortiz',
        delivery: { postalCode: '72100', addressLine: 'Calle Nueva 1', references: null, paysWith: null },
        saveAddress: true,
        addressLabel: 'Casa de mi mamá',
      }));
      expect(await screen.findByRole('link', { name: 'Ver mis pedidos' })).toHaveAttribute('href', '/mis-pedidos');
      expect(screen.queryByRole('link', { name: 'Crea tu cuenta' })).not.toBeInTheDocument();
      // The account finds it in Mis pedidos: it follows it, with nothing to keep.
      expect(screen.getByRole('link', { name: 'Seguir tu pedido' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Copiar enlace de seguimiento' })).not.toBeInTheDocument();
    });

    it('lets a business\'s team order as a guest, without its name or addresses', async () => {
      api.getMenu.mockResolvedValue(menu({ deliveryAvailable: true }));

      renderPage('/m/cafe-luna', signedInAs('TenantAdmin'));
      await userEvent.click(await screen.findByRole('button', { name: /^Agregar Latte/ }));
      await userEvent.click(screen.getByRole('button', { name: /^Ver pedido/ }));
      const cart = screen.getByRole('dialog', { name: 'Tu pedido' });
      await userEvent.click(within(cart).getByRole('radio', { name: 'A domicilio' }));

      expect(within(cart).getByLabelText('Tu nombre')).toHaveValue('');
      expect(account.getAddresses).not.toHaveBeenCalled();
      expect(within(cart).queryByLabelText('Guardar esta dirección en mi cuenta')).not.toBeInTheDocument();
    });
  });

  describe('a domicilio', () => {
    const quoted = (postalCode: string, covered = true) => ({
      postalCode,
      covered,
      fee: covered ? 35 : null,
      minimumOrder: covered ? 100 : null,
      freeDeliveryFrom: covered ? 400 : null,
    });

    async function openCart(quantity = 2) {
      for (let i = 0; i < quantity; i++) {
        await userEvent.click(await screen.findByRole('button', { name: /^Agregar Latte/ }));
      }
      await userEvent.click(screen.getByRole('button', { name: /^Ver pedido/ }));
      return screen.getByRole('dialog', { name: 'Tu pedido' });
    }

    it('takes the order to the address with what the business charges to its postal code', async () => {
      api.getMenu.mockResolvedValue(menu({ deliveryAvailable: true }));
      api.getDeliveryQuote.mockImplementation(async (_slug, postalCode) => quoted(postalCode));
      api.placeOrder.mockResolvedValue({
        orderNumber: 'ORD-7', total: 145, deliveryFee: 35, confirmed: false, businessName: 'Café Luna', contactPhone: null,
      } as never);

      renderPage();
      // The store offers both, as the apps do: picked up until the customer switches.
      const how = await screen.findByRole('radiogroup', { name: '¿Cómo lo quieres?' });
      expect(within(how).getByRole('radio', { name: 'Para recoger' })).toHaveAttribute('aria-checked', 'true');
      const cart = await openCart();
      const send = within(cart).getByRole('button', { name: /^Hacer pedido/ });

      // Picked up by default: the products alone.
      expect(within(cart).getByRole('radio', { name: 'Para recoger' })).toHaveAttribute('aria-checked', 'true');
      expect(send).toHaveTextContent('$110.00');
      await userEvent.click(within(cart).getByRole('radio', { name: 'A domicilio' }));
      expect(send).toBeDisabled();
      await userEvent.type(within(cart).getByLabelText('Código postal'), '72000');
      expect(await within(cart).findByText('Envío $35.00 · gratis desde $400.00 · pedido mínimo $100.00')).toBeInTheDocument();
      expect(api.getDeliveryQuote).toHaveBeenCalledWith('cafe-luna', '72000');
      expect(within(cart).getByText('Envío a domicilio').nextSibling).toHaveTextContent('$35.00');
      expect(send).toHaveTextContent('$145.00');

      await userEvent.type(within(cart).getByLabelText('Tu nombre'), 'Ana');
      await userEvent.type(within(cart).getByLabelText('Teléfono (WhatsApp)'), '5512345678');
      await userEvent.type(within(cart).getByLabelText('Dirección'), 'Av. Reforma 123, Centro');
      await userEvent.click(send);

      expect(api.placeOrder).toHaveBeenCalledWith('cafe-luna', expect.objectContaining({
        expectedTotal: 145,
        tableLabel: null,
        delivery: { postalCode: '72000', addressLine: 'Av. Reforma 123, Centro', references: null, paysWith: null },
      }));
      expect((await screen.findByText('Envío')).nextSibling).toHaveTextContent('$35.00');
    });

    it('asks what the customer pays with, for the courier\'s change, and waits for a bill that covers the total', async () => {
      api.getMenu.mockResolvedValue(menu({ deliveryAvailable: true, deliveryOnly: true }));
      api.getDeliveryQuote.mockImplementation(async (_slug, postalCode) => quoted(postalCode));
      api.placeOrder.mockResolvedValue({
        orderNumber: 'ORD-8', total: 145, deliveryFee: 35, confirmed: false, businessName: 'Café Luna', contactPhone: null,
      } as never);

      renderPage();
      const cart = await openCart();
      const send = within(cart).getByRole('button', { name: /^Hacer pedido/ });
      await userEvent.type(within(cart).getByLabelText('Código postal'), '72000');
      await within(cart).findByText('Envío $35.00 · gratis desde $400.00 · pedido mínimo $100.00');
      await userEvent.type(within(cart).getByLabelText('Tu nombre'), 'Ana');
      await userEvent.type(within(cart).getByLabelText('Teléfono (WhatsApp)'), '5512345678');
      await userEvent.type(within(cart).getByLabelText('Dirección'), 'Av. Reforma 123, Centro');
      const bill = within(cart).getByLabelText('¿Con cuánto pagas? (opcional)');

      await userEvent.type(bill, '100');
      expect(within(cart).getByText('Con $100.00 no alcanza: hay que cobrar $145.00.')).toBeInTheDocument();
      expect(bill).toHaveAttribute('aria-invalid', 'true');
      expect(send).toBeDisabled();

      await userEvent.clear(bill);
      await userEvent.type(bill, '200');
      expect(within(cart).getByText(/Tu cambio:/)).toHaveTextContent('$55.00');
      expect(bill).toHaveAttribute('aria-invalid', 'false');
      await userEvent.click(send);

      expect(api.placeOrder).toHaveBeenCalledWith('cafe-luna', expect.objectContaining({
        expectedTotal: 145,
        delivery: { postalCode: '72000', addressLine: 'Av. Reforma 123, Centro', references: null, paysWith: 200 },
      }));
    });

    it('says where it does not deliver, or the minimum, and sends nothing', async () => {
      api.getMenu.mockResolvedValue(menu({ deliveryAvailable: true, deliveryOnly: true }));
      api.getDeliveryQuote.mockImplementation(async (_slug, postalCode) => quoted(postalCode, postalCode === '72000'));

      renderPage();
      const cart = await openCart(1);

      // A kitchen without premises only delivers: no choice to make.
      expect(within(cart).queryByRole('radio')).not.toBeInTheDocument();
      expect(within(cart).getByText('Te lo llevamos a domicilio.')).toBeInTheDocument();
      const postalCode = within(cart).getByLabelText('Código postal');
      await userEvent.type(postalCode, '72999');
      expect(await within(cart).findByText('Por ahora no llegamos al 72999.')).toBeInTheDocument();
      expect(within(cart).getByRole('button', { name: /^Hacer pedido/ })).toBeDisabled();

      await userEvent.clear(postalCode);
      await userEvent.type(postalCode, '72000');
      expect(await within(cart).findByText('El pedido mínimo para el 72000 es de $100.00.')).toBeInTheDocument();
      expect(within(cart).getByRole('button', { name: /^Hacer pedido/ })).toBeDisabled();
      expect(api.placeOrder).not.toHaveBeenCalled();
    });

    it('adds the IVA of the fee for a business whose prices do not include it, and the rows add up', async () => {
      api.getMenu.mockResolvedValue(menu({ deliveryAvailable: true, pricesIncludeTax: false }));
      api.getDeliveryQuote.mockImplementation(async (_slug, postalCode) => quoted(postalCode));

      renderPage();
      const cart = await openCart();
      await userEvent.click(within(cart).getByRole('radio', { name: 'A domicilio' }));
      await userEvent.type(within(cart).getByLabelText('Código postal'), '72000');

      expect(await within(cart).findByText('Envío $35.00 más IVA · gratis desde $400.00 · pedido mínimo $100.00')).toBeInTheDocument();
      // $110 + 16 % = $127.60 in products and $35 + 16 % = $40.60 of delivery: $168.20.
      expect(within(cart).getByText('Productos').nextSibling).toHaveTextContent('$127.60');
      expect(within(cart).getByText('Envío a domicilio').nextSibling).toHaveTextContent('$40.60');
      expect(within(cart).getByRole('button', { name: /^Hacer pedido/ })).toHaveTextContent('$168.20');
    });

    it('offers no delivery at a table', async () => {
      api.getMenu.mockResolvedValue(menu({ deliveryAvailable: true }));

      renderPage('/m/cafe-luna?mesa=Mesa%204');
      const cart = await openCart(1);

      expect(within(cart).queryByLabelText('Código postal')).not.toBeInTheDocument();
      expect(within(cart).queryByRole('radio', { name: 'A domicilio' })).not.toBeInTheDocument();
      expect(screen.queryByText(/También a domicilio/)).not.toBeInTheDocument();
    });
  });

  describe('con la forma de las apps de entrega (Fase 24E)', () => {
    const size = {
      id: 'g1', name: 'Tamaño', minSelections: 1, maxSelections: 1,
      options: [
        { id: 'o1', name: 'Chico', priceDelta: 0, isAvailable: true },
        { id: 'o2', name: 'Grande', priceDelta: 12, isAvailable: true },
      ],
    };
    const extras = {
      id: 'g2', name: 'Extras', minSelections: 0, maxSelections: 2,
      options: [{ id: 'o3', name: 'Shot extra', priceDelta: 10, isAvailable: true }],
    };
    const storeMenu = (overrides: Partial<PublicMenu> = {}) => menu({
      description: 'Café de especialidad y pan dulce',
      category: 0,
      categories: [
        {
          id: 'c1',
          name: 'Bebidas calientes',
          products: [
            {
              id: 'p1', name: 'Latte', description: 'Espresso con leche vaporizada', price: 55, compareAtPrice: 65,
              isTaxExempt: false, isFeatured: true, availableNow: true, allergens: 'Lácteos', optionGroups: [size, extras],
              imageUrl: 'https://cdn/latte_md.webp', imageLargeUrl: 'https://cdn/latte_lg.webp',
            },
            { id: 'p2', name: 'Café americano', description: 'Café de Chiapas', price: 35, isTaxExempt: false, isFeatured: false, availableNow: true, optionGroups: [] },
          ],
        },
        {
          id: 'c2',
          name: 'Panadería',
          products: [{ id: 'p3', name: 'Concha de vainilla', price: 18, isTaxExempt: false, isFeatured: false, availableNow: true, optionGroups: [] }],
        },
      ],
      ...overrides,
    });

    it('opens with the cover, the store line and its featured products, and the arrow back only from the app', async () => {
      api.getMenu.mockResolvedValue(storeMenu({
        coverImageUrl: 'https://cdn/portada_lg.webp',
        coverImageWideUrl: 'https://cdn/portada_original.webp',
      }));

      const { container, unmount } = renderPage();

      expect(await screen.findByText('Comida · Café de especialidad y pan dulce')).toBeInTheDocument();
      const cover = container.querySelector('.pm-cover-photo');
      expect(cover).toHaveAttribute('src', 'https://cdn/portada_lg.webp');
      expect(cover).toHaveAttribute('srcset', 'https://cdn/portada_lg.webp 1024w, https://cdn/portada_original.webp 2048w');
      const featured = screen.getByRole('region', { name: 'Destacados' });
      expect(within(featured).getByRole('button', { name: 'Latte' })).toBeInTheDocument();
      expect(within(featured).queryByRole('button', { name: 'Café americano' })).not.toBeInTheDocument();
      // −15 %: from $65.00 to $55.00, with the price before crossed out.
      expect(within(featured).getByText('−15%')).toBeInTheDocument();
      // Opened from a QR or a link: nothing leads to other businesses.
      expect(screen.queryByRole('button', { name: 'Regresar' })).not.toBeInTheDocument();
      unmount();

      renderPage('/m/cafe-luna', undefined, '/explorar');
      expect(await screen.findByRole('button', { name: 'Regresar' })).toBeInTheDocument();
    });

    it('finds products by any word of their name or description, without accents', async () => {
      api.getMenu.mockResolvedValue(storeMenu());

      renderPage();
      const search = await screen.findByRole('searchbox', { name: 'Buscar en Café Luna' });

      await userEvent.type(search, 'cafe chiapas');
      expect(screen.getByRole('heading', { name: '1 producto' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Café americano' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Concha de vainilla' })).not.toBeInTheDocument();
      // While searching the tabs and the featured products step aside.
      expect(screen.queryByRole('navigation', { name: 'Categorías' })).not.toBeInTheDocument();
      expect(screen.queryByRole('region', { name: 'Destacados' })).not.toBeInTheDocument();

      await userEvent.clear(search);
      await userEvent.type(search, 'tacos');
      expect(screen.getByRole('heading', { name: 'No encontramos «tacos» en el menú' })).toBeInTheDocument();
    });

    it('takes a tab to its section and marks the first one at the top of the page', async () => {
      const scrollIntoView = vi.fn();
      Element.prototype.scrollIntoView = scrollIntoView;
      api.getMenu.mockResolvedValue(storeMenu());

      renderPage();
      const tabs = await screen.findByRole('navigation', { name: 'Categorías' });

      expect(within(tabs).getByRole('button', { name: 'Bebidas calientes' })).toHaveAttribute('aria-current', 'true');
      await userEvent.click(within(tabs).getByRole('button', { name: 'Panadería' }));
      expect(scrollIntoView).toHaveBeenCalledWith(expect.objectContaining({ block: 'start' }));
      expect(scrollIntoView.mock.contexts[0]).toBe(document.getElementById('pm-cat-c2'));
      expect(screen.getByRole('region', { name: 'Panadería' })).toContainElement(screen.getByRole('button', { name: 'Concha de vainilla' }));
    });

    it('opens the sheet of a product with its photo, allergens and options, and adds how many with instructions', async () => {
      api.getMenu.mockResolvedValue(storeMenu());
      api.placeOrder.mockResolvedValue({
        orderNumber: 'ORD-3', total: 154, deliveryFee: null, confirmed: false, businessName: 'Café Luna', contactPhone: null,
      } as never);

      renderPage();
      const drinks = await screen.findByRole('region', { name: 'Bebidas calientes' });
      await userEvent.click(within(drinks).getByRole('button', { name: 'Latte' }));
      const sheet = screen.getByRole('dialog', { name: 'Latte' });

      expect(sheet.querySelector('.pm-product-photo')).toHaveAttribute('src', 'https://cdn/latte_lg.webp');
      expect(within(sheet).getByText('Espresso con leche vaporizada')).toBeInTheDocument();
      expect(within(sheet).getByText('Alérgenos: Lácteos')).toBeInTheDocument();
      expect(within(sheet).getByText('Obligatorio')).toBeInTheDocument();
      expect(within(sheet).getByText('Elige 1')).toBeInTheDocument();
      expect(within(sheet).getByText('Elige hasta 2')).toBeInTheDocument();
      // One size is already chosen, as the apps do.
      expect(within(sheet).getByRole('radio', { name: /Chico/ })).toBeChecked();

      await userEvent.click(within(sheet).getByRole('radio', { name: /Grande/ }));
      await userEvent.type(within(sheet).getByLabelText('Instrucciones especiales'), '  sin azúcar  ');
      await userEvent.click(within(sheet).getByRole('button', { name: 'Uno más' }));
      await userEvent.click(within(sheet).getByRole('button', { name: /^Agregar 2/ }));

      // Two of $67.00: the size adds $12.00.
      expect(screen.getByRole('button', { name: /^Ver pedido · 2/ })).toHaveTextContent('$134.00');
      expect(screen.getAllByRole('button', { name: 'Agregar Latte' })[0]).toHaveTextContent('2');
      expect(readCart('cafe-luna')?.lines).toEqual([{ productId: 'p1', optionIds: ['o2'], quantity: 2, notes: 'sin azúcar' }]);

      await userEvent.click(screen.getByRole('button', { name: /^Ver pedido/ }));
      const cart = screen.getByRole('dialog', { name: 'Tu pedido' });
      expect(within(cart).getByText('«sin azúcar»')).toBeInTheDocument();
      await userEvent.type(within(cart).getByLabelText('Tu nombre'), 'Ana');
      await userEvent.type(within(cart).getByLabelText('Teléfono (WhatsApp)'), '5512345678');
      await userEvent.click(within(cart).getByRole('button', { name: /^Hacer pedido/ }));

      expect(api.placeOrder).toHaveBeenCalledWith('cafe-luna', expect.objectContaining({
        items: [{ productId: 'p1', quantity: 2, optionIds: ['o2'], notes: 'sin azúcar' }],
      }));
    });

    it('adds a product without options at once with its «+», and only shows a product while the menu takes no orders', async () => {
      api.getMenu.mockResolvedValueOnce(storeMenu());

      const { unmount } = renderPage();
      await userEvent.click(await screen.findByRole('button', { name: 'Agregar Café americano' }));
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: /^Ver pedido · 1/ })).toHaveTextContent('$35.00');
      unmount();

      window.localStorage.clear();
      api.getMenu.mockResolvedValueOnce(storeMenu({ acceptsOrders: false }));
      renderPage();
      await userEvent.click(await screen.findByRole('button', { name: 'Café americano' }));
      const sheet = screen.getByRole('dialog', { name: 'Café americano' });
      expect(within(sheet).queryByRole('button', { name: /^Agregar/ })).not.toBeInTheDocument();
      expect(within(sheet).queryByLabelText('Instrucciones especiales')).not.toBeInTheDocument();
    });
  });
});
