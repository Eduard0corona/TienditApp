import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { publicDirectoryApi, type DirectoryBusiness, type PublicDirectory } from '../../api/publicDirectoryApi';
import { BusinessCategory } from '../../api/generated/enums';
import { saveCart } from '../../utils/savedCart';
import ExplorePage from './ExplorePage';

vi.mock('../../api/publicDirectoryApi', () => ({
  publicDirectoryApi: { getDirectory: vi.fn(), getProfile: vi.fn(), updateProfile: vi.fn() },
}));

const api = vi.mocked(publicDirectoryApi);

function business(overrides: Partial<DirectoryBusiness>): DirectoryBusiness {
  return {
    name: 'Taquería El Güero',
    slug: 'el-guero',
    category: BusinessCategory.Food,
    theme: 0,
    deliveryOnly: false,
    deliveryAvailable: false,
    pricesIncludeTax: true,
    acceptsOrders: false,
    ordersPaused: false,
    closed: false,
    ...overrides,
  };
}

function directory(items: DirectoryBusiness[], options: { total?: number; hasNextPage?: boolean; counts?: Partial<Record<BusinessCategory, number>> } = {}): PublicDirectory {
  const counts = options.counts ?? { [BusinessCategory.Food]: 2, [BusinessCategory.Hardware]: 1 };
  return {
    categories: Object.values(BusinessCategory).map((category) => ({ category, count: counts[category] ?? 0 })),
    businesses: {
      items,
      pageNumber: 1,
      totalPages: options.hasNextPage ? 2 : 1,
      totalCount: options.total ?? items.length,
      hasPreviousPage: false,
      hasNextPage: options.hasNextPage ?? false,
    },
  };
}

function Address() {
  const location = useLocation();
  return <output data-testid="address">{`${location.pathname}${location.search}`}</output>;
}

function renderPage(path = '/explorar') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/explorar" element={<><ExplorePage /><Address /></>} />
          <Route path="/explorar/:categoria" element={<><ExplorePage /><Address /></>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ExplorePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // The postal code and the carts stay on the device (Fase 24B): each test starts without them.
    window.localStorage.clear();
  });

  it('asks the postal code once, remembers it, and each card says whether it delivers there', async () => {
    api.getDirectory.mockImplementation(async (search) => directory([
      business({
        acceptsOrders: true,
        deliveryAvailable: true,
        deliversToPostalCode: search.postalCode ? true : null,
        deliveryFee: search.postalCode ? 30 : null,
      }),
      business({
        name: 'Café Luna',
        slug: 'cafe-luna',
        acceptsOrders: true,
        deliveryAvailable: true,
        pricesIncludeTax: false,
        deliversToPostalCode: search.postalCode ? false : null,
      }),
    ]));

    const { unmount } = renderPage();
    expect(within(await screen.findByRole('link', { name: /Taquería El Güero/ })).getByText('Envío a domicilio')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Escribe tu código postal' }));
    // The focus goes to the field, and back to the button once it is written.
    expect(screen.getByLabelText('Código postal')).toHaveFocus();
    await userEvent.type(screen.getByLabelText('Código postal'), '72a000');
    await userEvent.click(screen.getByRole('button', { name: 'Listo' }));
    expect(screen.getByRole('button', { name: 'Cambiar' })).toHaveFocus();

    expect(await within(screen.getByRole('link', { name: /Taquería El Güero/ })).findByText('Envío $30.00')).toBeInTheDocument();
    expect(within(screen.getByRole('link', { name: /Café Luna/ })).getByText('No llega a tu código postal')).toBeInTheDocument();
    expect(screen.getByText('72000')).toBeInTheDocument();
    expect(api.getDirectory).toHaveBeenLastCalledWith(expect.objectContaining({ postalCode: '72000' }), expect.anything());

    // Next visit, the same postal code without asking; it can be taken away.
    unmount();
    renderPage();
    expect(await screen.findByText('72000')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Cambiar' }));
    await userEvent.click(screen.getByRole('button', { name: 'Quitar' }));
    expect(screen.getByRole('button', { name: 'Escribe tu código postal' })).toBeInTheDocument();
    expect(window.localStorage.getItem('tienditapp.postalCode')).toBeNull();
  });

  it('reminds a cart left without sending in a store', async () => {
    saveCart('el-guero', { businessName: 'Taquería El Güero', lines: [{ productId: 'p1', optionIds: [], quantity: 3 }] });
    api.getDirectory.mockResolvedValue(directory([business({})]));

    renderPage();

    expect(await screen.findByText(/Tienes un pedido sin enviar en/)).toHaveTextContent('Tienes un pedido sin enviar en Taquería El Güero (3 productos).');
    expect(screen.getByRole('link', { name: 'Seguir con el pedido' })).toHaveAttribute('href', '/m/el-guero');
  });

  it('lists each business with what its menu answers, and opens its menu', async () => {
    api.getDirectory.mockResolvedValue(directory([
      business({ acceptsOrders: true, deliveryAvailable: true, description: 'Tacos al pastor desde 1998', city: 'Puebla' }),
      business({ name: 'Café Luna', slug: 'cafe-luna', ordersPaused: true, closed: true, statusLabel: 'Cerrado', statusDetail: 'abre mañana a las 9:00' }),
      business({ name: 'Ferretería Don Beto', slug: 'don-beto', category: BusinessCategory.Hardware, deliveryOnly: true }),
    ]));

    renderPage();

    const taqueria = await screen.findByRole('link', { name: /Taquería El Güero/ });
    expect(taqueria).toHaveAttribute('href', '/m/el-guero');
    expect(within(taqueria).getByText('Pide en línea')).toBeInTheDocument();
    expect(within(taqueria).getByText('Envío a domicilio')).toBeInTheDocument();
    expect(within(taqueria).getByText('Comida · Puebla')).toBeInTheDocument();
    expect(within(screen.getByRole('link', { name: /Café Luna/ })).getByText('Cerrado · abre mañana a las 9:00')).toBeInTheDocument();
    expect(within(screen.getByRole('link', { name: /Ferretería Don Beto/ })).getByText('Ferretería y hogar · Solo a domicilio')).toBeInTheDocument();
    expect(screen.getByText('3 negocios')).toBeInTheDocument();
    expect(api.getDirectory).toHaveBeenCalledWith({ category: undefined, q: undefined, pageNumber: 1, pageSize: 24 }, expect.anything());
  });

  it('shows only the categories with businesses, and the page of one asks for it', async () => {
    api.getDirectory.mockResolvedValue(directory([business({})]));

    renderPage('/explorar/comida');

    expect(await screen.findByRole('link', { name: /Taquería El Güero/ })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Comida' })).toBeInTheDocument();
    const chips = within(screen.getByRole('navigation', { name: 'Categorías' }));
    expect(chips.getAllByRole('link').map((link) => link.textContent)).toEqual(['Todos', 'Comida2', 'Ferretería y hogar1']);
    expect(chips.getByRole('link', { name: /Comida/ })).toHaveAttribute('aria-current', 'page');
    expect(api.getDirectory).toHaveBeenCalledWith(expect.objectContaining({ category: BusinessCategory.Food }), expect.anything());
  });

  it('searches as the customer types and keeps the search in the address', async () => {
    api.getDirectory.mockResolvedValue(directory([]));
    const user = userEvent.setup();
    renderPage();

    await user.type(await screen.findByRole('searchbox', { name: 'Buscar negocios' }), 'tacos');

    expect(await screen.findByText('No encontramos «tacos»')).toBeInTheDocument();
    expect(screen.getByTestId('address')).toHaveTextContent('/explorar?q=tacos');
    expect(api.getDirectory).toHaveBeenLastCalledWith(expect.objectContaining({ q: 'tacos' }), expect.anything());

    await user.click(screen.getByRole('button', { name: 'Borrar la búsqueda' }));
    expect(screen.getByRole('searchbox', { name: 'Buscar negocios' })).toHaveValue('');
    await act(() => new Promise((resolve) => setTimeout(resolve, 350)));
    expect(screen.getByTestId('address')).toHaveTextContent(/^\/explorar$/);
  });

  it('says when there are no businesses yet, and when a category has none', async () => {
    api.getDirectory.mockResolvedValueOnce(directory([], { counts: {} }));
    const { unmount } = renderPage();
    expect(await screen.findByText('Pronto verás aquí los negocios de tu ciudad')).toBeInTheDocument();
    unmount();

    api.getDirectory.mockResolvedValueOnce(directory([]));
    renderPage('/explorar/autos');
    expect(await screen.findByText('Aún no hay negocios de autos')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver todos los negocios' })).toHaveAttribute('href', '/explorar');
  });

  it('loads the next businesses on request', async () => {
    // 25 businesses: 24 on the first page, one on the second (only one of each is shown here).
    api.getDirectory
      .mockResolvedValueOnce(directory([business({})], { total: 25, hasNextPage: true }))
      .mockResolvedValueOnce(directory([business({ name: 'Café Luna', slug: 'cafe-luna' })], { total: 25 }));
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole('button', { name: 'Ver más negocios (1)' }));

    expect(await screen.findByRole('link', { name: /Café Luna/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Taquería El Güero/ })).toBeInTheDocument();
    expect(api.getDirectory).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 }), expect.anything());
    expect(screen.queryByRole('button', { name: /Ver más/ })).not.toBeInTheDocument();
  });

  it('sends an unknown category to all of them', async () => {
    api.getDirectory.mockResolvedValue(directory([business({})]));

    renderPage('/explorar/no-existe');

    expect(await screen.findByTestId('address')).toHaveTextContent(/^\/explorar$/);
  });
});
