import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { publicMenuApi, type PublicMenu } from '../../api/publicMenuApi';
import { BusinessTheme, DayOfWeek, SpecialDayKind } from '../../types';
import { getBusinessTheme } from '../../utils/businessThemes';
import { applyBusinessTheme } from '../../utils/theme';
import PublicMenuPage from './PublicMenuPage';

vi.mock('../../api/publicMenuApi', () => ({
  publicMenuApi: {
    getMenu: vi.fn(),
    getDeliveryQuote: vi.fn(),
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

  it('says a kitchen only delivers, with no address and no way to get there', async () => {
    // An address next to the flag (an API that sent both) is not shown either.
    api.getMenu.mockResolvedValue(menu({ deliveryOnly: true, address: 'Privada Los Pinos 8, Puebla' }));

    renderPage();

    const info = await screen.findByRole('region', { name: 'Horario y ubicación' });
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

    const today = await screen.findByText('Hoy: Cerrado · Inventario');
    // Nothing to open: no disclosure and no arrow that promises more.
    expect(today.closest('details')).toBeNull();
    expect(container.querySelector('details')).toBeNull();
    expect(container.querySelector('.pm-hours-toggle')).toBeNull();
    expect(screen.getByRole('region', { name: 'Horario y ubicación' })).toContainElement(today);
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
      expect(await screen.findByText(/También a domicilio/)).toBeInTheDocument();
      const cart = await openCart();
      const send = within(cart).getByRole('button', { name: /^Enviar pedido/ });

      // Picked up by default: the products alone.
      expect(within(cart).getByRole('radio', { name: 'Paso por él' })).toHaveAttribute('aria-checked', 'true');
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
        delivery: { postalCode: '72000', addressLine: 'Av. Reforma 123, Centro', references: null },
      }));
      expect(await screen.findByText(/Incluye envío de/)).toHaveTextContent('Incluye envío de $35.00');
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
      expect(within(cart).getByRole('button', { name: /^Enviar pedido/ })).toBeDisabled();

      await userEvent.clear(postalCode);
      await userEvent.type(postalCode, '72000');
      expect(await within(cart).findByText('El pedido mínimo para el 72000 es de $100.00.')).toBeInTheDocument();
      expect(within(cart).getByRole('button', { name: /^Enviar pedido/ })).toBeDisabled();
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
      expect(within(cart).getByRole('button', { name: /^Enviar pedido/ })).toHaveTextContent('$168.20');
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
});
