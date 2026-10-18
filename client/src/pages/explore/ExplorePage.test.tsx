import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi } from '../../api/accountApi';
import { publicDirectoryApi, type DirectoryBusiness, type DirectoryProduct, type PublicDirectory } from '../../api/publicDirectoryApi';
import { BusinessCategory } from '../../api/generated/enums';
import { ToastContext, type ToastApi } from '../../components/feedback/feedbackContexts';
import { AuthContext, type AuthContextType } from '../../contexts/AuthContextDefinition';
import { useCustomerAccount } from '../../hooks/useCustomerAccount';
import type { UserProfileDto } from '../../types';
import { saveCart } from '../../utils/savedCart';
import ExplorePage from './ExplorePage';

vi.mock('../../api/publicDirectoryApi', () => ({
  publicDirectoryApi: { getDirectory: vi.fn(), getProfile: vi.fn(), updateProfile: vi.fn() },
}));
vi.mock('../../api/accountApi', () => ({
  accountApi: { getFavorites: vi.fn(), saveFavorite: vi.fn(), removeFavorite: vi.fn() },
}));
vi.mock('../../hooks/useCustomerAccount', () => ({ useCustomerAccount: vi.fn() }));

const api = vi.mocked(publicDirectoryApi);
const account = vi.mocked(accountApi);

/** Signed in with a customer account: it keeps favorites (Fase 24F). */
function signInAsCustomer() {
  vi.mocked(useCustomerAccount).mockReturnValue({ customer: { id: 'u1', firstName: 'Mariana' } as UserProfileDto, signedIn: true });
}

function product(overrides: Partial<DirectoryProduct>): DirectoryProduct {
  return {
    id: 'p1',
    name: 'Orden de pastor',
    price: 85,
    businessName: 'Taquería El Güero',
    businessSlug: 'el-guero',
    theme: 0,
    acceptsOrders: true,
    addsTax: false,
    ...overrides,
  };
}

function business(overrides: Partial<DirectoryBusiness>): DirectoryBusiness {
  return {
    name: 'Taquería El Güero',
    slug: 'el-guero',
    category: BusinessCategory.Food,
    theme: 0,
    deliveryOnly: false,
    deliveryAvailable: false,
    pricesIncludeTax: true,
    booksOnline: false,
    acceptsOrders: false,
    ordersPaused: false,
    closed: false,
    ...overrides,
  };
}

function directory(
  items: DirectoryBusiness[],
  options: { total?: number; hasNextPage?: boolean; counts?: Partial<Record<BusinessCategory, number>>; products?: PublicDirectory['products'] } = {},
): PublicDirectory {
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
    products: options.products ?? [],
  };
}

function Address() {
  const location = useLocation();
  return <output data-testid="address">{`${location.pathname}${location.search}`}</output>;
}

/** A promise the test settles when it wants: a change of a heart still on its way. */
function later<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((ok, fail) => {
    resolve = ok;
    reject = fail;
  });
  return { promise, resolve, reject };
}

function renderPage(path = '/explorar', { auth, toast }: { auth?: Partial<AuthContextType>; toast?: ToastApi } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const page = (
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/explorar" element={<><ExplorePage /><Address /></>} />
        <Route path="/explorar/:categoria" element={<><ExplorePage /><Address /></>} />
      </Routes>
    </MemoryRouter>
  );
  return render(
    <QueryClientProvider client={queryClient}>
      <ToastContext.Provider value={toast ?? null}>
        {auth ? <AuthContext.Provider value={auth as AuthContextType}>{page}</AuthContext.Provider> : page}
      </ToastContext.Provider>
    </QueryClientProvider>,
  );
}

describe('ExplorePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // A visitor unless a test signs in.
    vi.mocked(useCustomerAccount).mockReturnValue({ customer: null, signedIn: false });
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
    expect(within(await screen.findByRole('link', { name: /^Taquería El Güero/ })).getByText('Envío a domicilio')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Escribe tu código postal' }));
    // The focus goes to the field, and back to the button once it is written.
    expect(screen.getByLabelText('Código postal')).toHaveFocus();
    await userEvent.type(screen.getByLabelText('Código postal'), '72a000');
    await userEvent.click(screen.getByRole('button', { name: 'Listo' }));
    expect(screen.getByRole('button', { name: 'Cambiar' })).toHaveFocus();

    expect(await within(screen.getByRole('link', { name: /^Taquería El Güero/ })).findByText('Envío $30.00')).toBeInTheDocument();
    expect(within(screen.getByRole('link', { name: /^Café Luna/ })).getByText('No llega a tu código postal')).toBeInTheDocument();
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

    const taqueria = await screen.findByRole('link', { name: /^Taquería El Güero/ });
    expect(taqueria).toHaveAttribute('href', '/m/el-guero');
    expect(within(taqueria).getByText('Pide en línea')).toBeInTheDocument();
    expect(within(taqueria).getByText('Envío a domicilio')).toBeInTheDocument();
    expect(within(taqueria).getByText('Comida · Puebla')).toBeInTheDocument();
    expect(within(screen.getByRole('link', { name: /^Café Luna/ })).getByText('Cerrado · abre mañana a las 9:00')).toBeInTheDocument();
    expect(within(screen.getByRole('link', { name: /^Ferretería Don Beto/ })).getByText('Ferretería y hogar · Solo a domicilio')).toBeInTheDocument();
    expect(screen.getByText('3 negocios')).toBeInTheDocument();
    expect(api.getDirectory).toHaveBeenCalledWith({ category: undefined, q: undefined, pageNumber: 1, pageSize: 24 }, expect.anything());
  });

  it('says which businesses book appointments online (Fase 24D)', async () => {
    api.getDirectory.mockResolvedValue(directory([
      business({ name: 'Barbería Luis', slug: 'barberia-luis', category: BusinessCategory.Beauty, booksOnline: true, statusLabel: 'Abierto' }),
      business({ name: 'Café Luna', slug: 'cafe-luna' }),
    ]));

    renderPage();

    const barbershop = await screen.findByRole('link', { name: /^Barbería Luis/ });
    expect(within(barbershop).getByText('Agenda en línea')).toBeInTheDocument();
    expect(within(barbershop).getByText('Abierto')).toBeInTheDocument();
    expect(within(screen.getByRole('link', { name: /^Café Luna/ })).queryByText('Agenda en línea')).not.toBeInTheDocument();
  });

  it('shows only the categories with businesses, and the page of one asks for it', async () => {
    api.getDirectory.mockResolvedValue(directory([business({})]));

    renderPage('/explorar/comida');

    expect(await screen.findByRole('link', { name: /^Taquería El Güero/ })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Comida' })).toBeInTheDocument();
    const chips = within(screen.getByRole('navigation', { name: 'Categorías' }));
    expect(chips.getAllByRole('link').map((link) => link.textContent)).toEqual(['Todos', 'Comida2', 'Ferretería y hogar1']);
    expect(chips.getByRole('link', { name: /Comida/ })).toHaveAttribute('aria-current', 'page');
    expect(api.getDirectory).toHaveBeenCalledWith(expect.objectContaining({ category: BusinessCategory.Food }), expect.anything());
    // The title the server writes for the page (PublicPages.StaticHeadFor).
    expect(document.title).toBe('Comida · Explorar · TienditApp');
  });

  it('searches as the customer types and keeps the search in the address', async () => {
    api.getDirectory.mockResolvedValue(directory([]));
    const user = userEvent.setup();
    renderPage();

    await user.type(await screen.findByRole('searchbox', { name: 'Buscar negocios y productos' }), 'tacos');

    expect(await screen.findByText('No encontramos «tacos»')).toBeInTheDocument();
    expect(screen.getByTestId('address')).toHaveTextContent('/explorar?q=tacos');
    expect(api.getDirectory).toHaveBeenLastCalledWith(expect.objectContaining({ q: 'tacos' }), expect.anything());

    await user.click(screen.getByRole('button', { name: 'Borrar la búsqueda' }));
    expect(screen.getByRole('searchbox', { name: 'Buscar negocios y productos' })).toHaveValue('');
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

    expect(await screen.findByRole('link', { name: /^Café Luna/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /^Taquería El Güero/ })).toBeInTheDocument();
    expect(api.getDirectory).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 }), expect.anything());
    expect(screen.queryByRole('button', { name: /Ver más/ })).not.toBeInTheDocument();
  });

  it('sends an unknown category to all of them', async () => {
    api.getDirectory.mockResolvedValue(directory([business({})]));

    renderPage('/explorar/no-existe');

    expect(await screen.findByTestId('address')).toHaveTextContent(/^\/explorar$/);
  });
  it('finds the products of the menus, and each opens on its sheet in its store (Fase 24F)', async () => {
    api.getDirectory.mockResolvedValue(directory([], {
      products: [
        product({ compareAtPrice: 100 }),
        product({ id: 'p2', name: 'Gringa de pastor', price: 70, businessName: 'Café Luna', businessSlug: 'cafe-luna', acceptsOrders: false, addsTax: true }),
      ],
    }));

    renderPage('/explorar?q=pastor');

    const products = within(await screen.findByRole('region', { name: 'Productos' }));
    const orden = products.getByRole('link', { name: /^Orden de pastor/ });
    expect(orden).toHaveAttribute('href', '/m/el-guero?producto=p1');
    expect(within(orden).getByText(/Pide en línea/)).toBeInTheDocument();
    expect(within(orden).getByText('$85.00')).toBeInTheDocument();
    expect(within(orden).getByText('$100.00')).toBeInTheDocument();
    const gringa = products.getByRole('link', { name: /^Gringa de pastor/ });
    expect(gringa).toHaveAttribute('href', '/m/cafe-luna?producto=p2');
    expect(within(gringa).queryByText(/Pide en línea/)).not.toBeInTheDocument();
    expect(within(gringa).getByText('más IVA')).toBeInTheDocument();
    // No business by that name, but products: not the page that says nothing was found.
    expect(screen.queryByText(/No encontramos/)).not.toBeInTheDocument();
    expect(api.getDirectory).toHaveBeenCalledWith(expect.objectContaining({ q: 'pastor' }), expect.anything());
  });

  it('puts the cover of each business across its card', async () => {
    api.getDirectory.mockResolvedValue(directory([business({ coverImageUrl: '/uploads/covers/guero/portada_md.webp' })]));

    const { container } = renderPage();

    await screen.findByRole('link', { name: /^Taquería El Güero/ });
    expect(container.querySelector('.dc-cover-photo')).toHaveAttribute('src', '/uploads/covers/guero/portada_md.webp');
  });

  it('invites a visitor to sign in to keep a business among their favorites', async () => {
    api.getDirectory.mockResolvedValue(directory([business({})]));

    renderPage();

    expect(await screen.findByRole('link', { name: 'Entra para guardar Taquería El Güero en tus favoritos' })).toHaveAttribute('href', '/login');
    expect(account.getFavorites).not.toHaveBeenCalled();
  });

  it('shows the favorites of a customer first, and keeps or takes out a business with its heart at once', async () => {
    signInAsCustomer();
    account.getFavorites.mockResolvedValue([
      { slug: 'el-guero', name: 'Taquería El Güero', savedAt: '2026-10-18T08:00:00Z', card: business({}) },
      // Out of Explorar now: the API writes its card as null.
      { slug: 'oculto', name: 'Café Oculto', savedAt: '2026-10-17T08:00:00Z', card: null },
    ]);
    const saving = later<void>();
    account.saveFavorite.mockReturnValue(saving.promise as never);
    account.removeFavorite.mockResolvedValue(undefined as never);
    api.getDirectory.mockResolvedValue(directory([business({}), business({ name: 'Café Luna', slug: 'cafe-luna' })]));
    const user = userEvent.setup();

    renderPage();

    const region = await screen.findByRole('region', { name: 'Tus favoritos' });
    const favorites = within(region);
    expect(favorites.getByRole('link', { name: /^Taquería El Güero/ })).toBeInTheDocument();
    // One out of Explorar keeps its name and opens its menu.
    expect(favorites.getByRole('link', { name: /Café Oculto/ })).toHaveAttribute('href', '/m/oculto');
    expect(favorites.getByText('No aparece en Explorar ahora')).toBeInTheDocument();

    // Kept at once, while the server answers; the heart keeps its name and says it is pressed.
    const luna = await screen.findByRole('button', { name: 'Guardar Café Luna en tus favoritos', pressed: false });
    await user.click(luna);
    expect(account.saveFavorite).toHaveBeenCalledWith('cafe-luna');
    expect(luna).toHaveAttribute('aria-pressed', 'true');
    expect(luna).toHaveAttribute('aria-disabled', 'true');
    account.getFavorites.mockResolvedValue([
      { slug: 'cafe-luna', name: 'Café Luna', savedAt: '2026-10-18T09:00:00Z', card: business({ name: 'Café Luna', slug: 'cafe-luna' }) },
      { slug: 'el-guero', name: 'Taquería El Güero', savedAt: '2026-10-18T08:00:00Z', card: business({}) },
    ]);
    await act(async () => saving.resolve());
    await waitFor(() => expect(favorites.getByRole('link', { name: /^Café Luna/ })).toBeInTheDocument());

    // Taken out from «Tus favoritos»: it leaves the row and the focus goes to its title.
    const guero = favorites.getByRole('button', { name: 'Guardar Taquería El Güero en tus favoritos', pressed: true });
    account.getFavorites.mockResolvedValue([
      { slug: 'cafe-luna', name: 'Café Luna', savedAt: '2026-10-18T09:00:00Z', card: business({ name: 'Café Luna', slug: 'cafe-luna' }) },
    ]);
    await user.click(guero);
    expect(account.removeFavorite).toHaveBeenCalledWith('el-guero');
    await waitFor(() => expect(favorites.queryByRole('link', { name: /^Taquería El Güero/ })).not.toBeInTheDocument());
    expect(favorites.getByRole('heading', { name: 'Tus favoritos' })).toHaveFocus();
    // Its heart in «Tus favoritos» and in the list say the same.
    expect(screen.getAllByRole('button', { name: 'Guardar Café Luna en tus favoritos' }).map((heart) => heart.getAttribute('aria-pressed')))
      .toEqual(['true', 'true']);
  });

  it('changes two hearts at once, and one the server refuses goes back with its reason', async () => {
    signInAsCustomer();
    account.getFavorites.mockResolvedValue([]);
    const first = later<void>();
    account.saveFavorite.mockImplementation((slug: string) => (slug === 'el-guero' ? first.promise : Promise.reject(new Error('Sin conexión'))) as never);
    api.getDirectory.mockResolvedValue(directory([business({}), business({ name: 'Café Luna', slug: 'cafe-luna' })]));
    const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() } as unknown as ToastApi;
    const user = userEvent.setup();

    renderPage('/explorar', { toast });

    await user.click(await screen.findByRole('button', { name: 'Guardar Taquería El Güero en tus favoritos', pressed: false }));
    // The second while the first is on its way: it is not ignored.
    await user.click(screen.getByRole('button', { name: 'Guardar Café Luna en tus favoritos' }));
    expect(account.saveFavorite).toHaveBeenCalledWith('cafe-luna');
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('No se pudo guardar el negocio.'));
    expect(screen.getByRole('button', { name: 'Guardar Café Luna en tus favoritos' })).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('button', { name: 'Guardar Taquería El Güero en tus favoritos' })).toHaveAttribute('aria-pressed', 'true');
    await act(async () => first.resolve());
  });

  it('shows no heart while the session comes back', async () => {
    api.getDirectory.mockResolvedValue(directory([business({})]));

    renderPage('/explorar', { auth: { isLoading: true, isAuthenticated: false, user: null } });

    await screen.findByRole('link', { name: /^Taquería El Güero/ });
    expect(screen.queryByRole('link', { name: /favoritos/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /favoritos/ })).not.toBeInTheDocument();
  });

  it('shows no favorites while searching or in a category', async () => {
    signInAsCustomer();
    account.getFavorites.mockResolvedValue([{ slug: 'el-guero', name: 'Taquería El Güero', savedAt: '2026-10-18T08:00:00Z', card: business({}) }]);
    api.getDirectory.mockResolvedValue(directory([business({})]));

    renderPage('/explorar/comida');

    await screen.findByRole('link', { name: /^Taquería El Güero/ });
    expect(screen.queryByRole('region', { name: 'Tus favoritos' })).not.toBeInTheDocument();
  });
});
