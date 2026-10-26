import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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
  options: {
    total?: number;
    hasNextPage?: boolean;
    totalPages?: number;
    counts?: Partial<Record<BusinessCategory, number>>;
    products?: PublicDirectory['products'];
  } = {},
): PublicDirectory {
  const counts = options.counts ?? { [BusinessCategory.Food]: 2, [BusinessCategory.Hardware]: 1 };
  return {
    categories: Object.values(BusinessCategory).map((category) => ({ category, count: counts[category] ?? 0 })),
    businesses: {
      items,
      pageNumber: 1,
      totalPages: options.totalPages ?? (options.hasNextPage ? 2 : 1),
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

/** The browser's geolocation: answers with that position, or fails with that code (1 is «denied»). */
function mockGeolocation(answer: { latitude: number; longitude: number } | { code: number }) {
  const getCurrentPosition = vi.fn((success: PositionCallback, failure?: PositionErrorCallback | null) => {
    if ('code' in answer) failure?.({ code: answer.code, PERMISSION_DENIED: 1 } as GeolocationPositionError);
    else success({ coords: { latitude: answer.latitude, longitude: answer.longitude } } as GeolocationPosition);
  });
  Object.defineProperty(navigator, 'geolocation', { value: { getCurrentPosition }, configurable: true });
  return getCurrentPosition;
}

/** The browser already allows this site its position: asking does not open its prompt. */
function allowLocation() {
  Object.defineProperty(navigator, 'permissions', {
    value: { query: vi.fn().mockResolvedValue({ state: 'granted' }) },
    configurable: true,
  });
}

describe('ExplorePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // A visitor unless a test signs in.
    vi.mocked(useCustomerAccount).mockReturnValue({ customer: null, signedIn: false });
    // The postal code and the carts stay on the device (Fase 24B): each test starts without them.
    window.localStorage.clear();
  });

  afterEach(() => {
    // jsdom has neither: what a test put on the browser leaves with it.
    Reflect.deleteProperty(navigator, 'geolocation');
    Reflect.deleteProperty(navigator, 'permissions');
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

  it('shows the favorites of a customer first and not again in the list, and keeps or takes out a business with its heart at once', async () => {
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
    // A favorite is not in the list below again, nor in its count.
    const list = within(screen.getByRole('region', { name: 'Negocios' }));
    expect(await list.findByText('1 negocio')).toBeInTheDocument();
    expect(list.queryByRole('link', { name: /^Taquería El Güero/ })).not.toBeInTheDocument();

    // Kept at once, while the server answers; the heart keeps its name and says it is pressed.
    const luna = list.getByRole('button', { name: 'Guardar Café Luna en tus favoritos', pressed: false });
    await user.click(luna);
    expect(account.saveFavorite).toHaveBeenCalledWith('cafe-luna');
    expect(luna).toHaveAttribute('aria-pressed', 'true');
    expect(luna).toHaveAttribute('aria-disabled', 'true');
    account.getFavorites.mockResolvedValue([
      { slug: 'cafe-luna', name: 'Café Luna', savedAt: '2026-10-18T09:00:00Z', card: business({ name: 'Café Luna', slug: 'cafe-luna' }) },
      { slug: 'el-guero', name: 'Taquería El Güero', savedAt: '2026-10-18T08:00:00Z', card: business({}) },
    ]);
    await act(async () => saving.resolve());
    // Once the server has it, it goes from the list to «Tus favoritos». Every business is there now: the list leaves (it
    // does not say there are none) and the focus goes to the title of «Tus favoritos».
    await waitFor(() => expect(favorites.getByRole('link', { name: /^Café Luna/ })).toBeInTheDocument());
    expect(screen.queryByRole('region', { name: 'Negocios' })).not.toBeInTheDocument();
    expect(screen.queryByText(/Pronto verás aquí/)).not.toBeInTheDocument();
    expect(favorites.getByRole('heading', { name: 'Tus favoritos' })).toHaveFocus();

    // Taken out from «Tus favoritos»: it leaves the row, the focus goes to its title, and it is back in the list.
    const guero = favorites.getByRole('button', { name: 'Guardar Taquería El Güero en tus favoritos', pressed: true });
    account.getFavorites.mockResolvedValue([
      { slug: 'cafe-luna', name: 'Café Luna', savedAt: '2026-10-18T09:00:00Z', card: business({ name: 'Café Luna', slug: 'cafe-luna' }) },
    ]);
    await user.click(guero);
    expect(account.removeFavorite).toHaveBeenCalledWith('el-guero');
    await waitFor(() => expect(favorites.queryByRole('link', { name: /^Taquería El Güero/ })).not.toBeInTheDocument());
    expect(favorites.getByRole('heading', { name: 'Tus favoritos' })).toHaveFocus();
    const back = within(await screen.findByRole('region', { name: 'Negocios' }));
    expect(back.getByRole('link', { name: /^Taquería El Güero/ })).toBeInTheDocument();
    expect(back.getByText('1 negocio')).toBeInTheDocument();
    expect(back.queryByRole('link', { name: /^Café Luna/ })).not.toBeInTheDocument();
    // Its heart in the list says it is not kept any more, once the server answered.
    await waitFor(() => expect(back.getByRole('button', { name: 'Guardar Taquería El Güero en tus favoritos' })).not.toHaveAttribute('aria-disabled'));
    expect(back.getByRole('button', { name: 'Guardar Taquería El Güero en tus favoritos' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('keeps the focus in the list on the business that takes the place of one kept', async () => {
    signInAsCustomer();
    account.getFavorites.mockResolvedValue([]);
    const saving = later<void>();
    account.saveFavorite.mockReturnValue(saving.promise as never);
    api.getDirectory.mockResolvedValue(directory([
      business({}),
      business({ name: 'Café Luna', slug: 'cafe-luna' }),
      business({ name: 'Ferretería Don Pepe', slug: 'don-pepe', category: BusinessCategory.Hardware }),
    ]));
    const user = userEvent.setup();

    renderPage();

    const list = within(await screen.findByRole('region', { name: 'Negocios' }));
    const luna = await list.findByRole('button', { name: 'Guardar Café Luna en tus favoritos', pressed: false });
    // The hearts wait until the favorites arrive.
    await waitFor(() => expect(luna).not.toHaveAttribute('aria-disabled'));
    await user.click(luna);
    account.getFavorites.mockResolvedValue([
      { slug: 'cafe-luna', name: 'Café Luna', savedAt: '2026-10-18T09:00:00Z', card: business({ name: 'Café Luna', slug: 'cafe-luna' }) },
    ]);
    await act(async () => saving.resolve());

    await waitFor(() => expect(list.queryByRole('link', { name: /^Café Luna/ })).not.toBeInTheDocument());
    expect(list.getByRole('button', { name: 'Guardar Ferretería Don Pepe en tus favoritos' })).toHaveFocus();
    expect(list.getByText('2 negocios')).toBeInTheDocument();
  });

  it('counts the list and «Ver más» without the favorites, on whichever page they are', async () => {
    signInAsCustomer();
    const kept = (slug: string, name: string) => ({ slug, name, savedAt: '2026-10-18T09:00:00Z', card: business({ slug, name }) });
    account.getFavorites.mockResolvedValue([kept('el-guero', 'Taquería El Güero'), kept('cafe-luna', 'Café Luna'), kept('oaxaca', 'Tlayudas Oaxaca')]);
    // 50 businesses in three pages (only two of each here), a favorite on each. The second arrives when the test says.
    const pages = { total: 50, totalPages: 3 };
    const second = later<PublicDirectory>();
    api.getDirectory.mockImplementation((search) => {
      if (search.pageNumber === 2) return second.promise;
      return Promise.resolve(search.pageNumber === 3
        ? directory([business({ name: 'Tlayudas Oaxaca', slug: 'oaxaca' }), business({ name: 'Papelería Sol', slug: 'sol' })], pages)
        : directory([business({}), business({ name: 'Tienda Lupita', slug: 'lupita' })], pages));
    });
    const user = userEvent.setup();

    renderPage();

    const list = within(await screen.findByRole('region', { name: 'Negocios' }));
    // 50 without the 3 favorites; past the first page, 26 businesses with 2 of the favorites.
    expect(await list.findByText('47 negocios')).toBeInTheDocument();
    await user.click(await list.findByRole('button', { name: 'Ver más negocios (24)' }));
    // While the second page is on its way, the favorites not found yet may be there: the button stays, with its focus.
    expect(list.getByRole('button', { name: 'Ver más negocios (2)' })).toHaveFocus();
    await act(async () => second.resolve(directory(
      [business({ name: 'Café Luna', slug: 'cafe-luna' }), business({ name: 'Ferretería Don Pepe', slug: 'don-pepe' })],
      pages,
    )));
    // The favorite of the second page is not one of those to come.
    const more = await list.findByRole('button', { name: 'Ver más negocios (1)' });
    expect(more).toHaveFocus();
    await user.click(more);

    expect(await list.findByRole('link', { name: /^Papelería Sol/ })).toBeInTheDocument();
    expect(list.getByRole('link', { name: /^Tienda Lupita/ })).toBeInTheDocument();
    expect(list.getByRole('link', { name: /^Ferretería Don Pepe/ })).toBeInTheDocument();
    for (const name of [/^Taquería El Güero/, /^Café Luna/, /^Tlayudas Oaxaca/]) {
      expect(list.queryByRole('link', { name })).not.toBeInTheDocument();
    }
    expect(list.queryByRole('button', { name: /Ver más/ })).not.toBeInTheDocument();
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

  it('shows no favorites while searching or in a category: there they are in the list', async () => {
    signInAsCustomer();
    account.getFavorites.mockResolvedValue([{ slug: 'el-guero', name: 'Taquería El Güero', savedAt: '2026-10-18T08:00:00Z', card: business({}) }]);
    api.getDirectory.mockResolvedValue(directory([business({})]));

    renderPage('/explorar/comida');

    await screen.findByRole('link', { name: /^Taquería El Güero/ });
    expect(await screen.findByRole('button', { name: 'Guardar Taquería El Güero en tus favoritos', pressed: true })).toBeInTheDocument();
    expect(screen.getByText('1 negocio')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Tus favoritos' })).not.toBeInTheDocument();
  });

  it('shows the nearest first with «Cerca de mí», each card saying how far it is, and keeps it in the address (Fase 24G)', async () => {
    const geolocation = mockGeolocation({ latitude: 19.0435774, longitude: -98.1986262 });
    api.getDirectory.mockImplementation(async (search) => directory(search.latitude == null
      ? [business({ slug: 'lejos', name: 'Taquería Lejos' }), business({ slug: 'cerca', name: 'Café Cerca' })]
      : [
          business({ slug: 'cerca', name: 'Café Cerca', city: 'Puebla', distanceKm: 0.3 }),
          business({ slug: 'lejos', name: 'Taquería Lejos', distanceKm: 12.4 }),
          business({ slug: 'cocina', name: 'Cocina Oculta', deliveryOnly: true }),
        ], {
      products: search.latitude == null ? [] : [product({ businessName: 'Café Cerca', businessSlug: 'cerca', distanceKm: 0.3 })],
    }));
    const user = userEvent.setup();
    renderPage('/explorar?q=caf');

    await user.click(await screen.findByRole('button', { name: 'Cerca de mí', pressed: false }));

    // The position travels to about 110 m, and only to Explorar's own search.
    await waitFor(() => expect(api.getDirectory).toHaveBeenLastCalledWith(
      expect.objectContaining({ q: 'caf', latitude: 19.044, longitude: -98.199 }), expect.anything()));
    expect(screen.getByRole('button', { name: 'Cerca de mí', pressed: true })).toBeInTheDocument();
    expect(screen.getByText('Primero lo más cercano a ti.')).toHaveAttribute('role', 'status');
    const cards = screen.getAllByRole('link', { name: /Café Cerca|Taquería Lejos|Cocina Oculta/ }).filter((link) => link.classList.contains('dc-card'));
    expect(cards.map((card) => card.getAttribute('href'))).toEqual(['/m/cerca', '/m/lejos', '/m/cocina']);
    expect(cards[0]).toHaveTextContent('Comida · Puebla · a 300 m');
    expect(cards[1]).toHaveTextContent('a 12 km');
    expect(cards[2]).toHaveTextContent('Solo a domicilio');
    expect(screen.getByRole('link', { name: /Orden de pastor/ })).toHaveTextContent('Café Cerca · a 300 m');
    expect(screen.getByTestId('address')).toHaveTextContent('/explorar?q=caf&cerca=1');
    // Another category keeps the search and «Cerca de mí».
    const categories = screen.getByRole('navigation', { name: 'Categorías' });
    expect(within(categories).getByRole('link', { name: /Comida/ })).toHaveAttribute('href', '/explorar/comida?q=caf&cerca=1');
    expect(window.localStorage.length).toBe(0);

    // Typing keeps it on: the new search is near the customer too.
    await user.type(screen.getByRole('searchbox'), 'e');
    await waitFor(() => expect(api.getDirectory).toHaveBeenLastCalledWith(
      expect.objectContaining({ q: 'cafe', latitude: 19.044, longitude: -98.199 }), expect.anything()));
    expect(screen.getByTestId('address')).toHaveTextContent('/explorar?q=cafe&cerca=1');

    // Off, Explorar goes back to its own order, without distances, and the address forgets it.
    await user.click(screen.getByRole('button', { name: 'Cerca de mí', pressed: true }));
    expect(await screen.findByRole('button', { name: 'Cerca de mí', pressed: false })).toBeInTheDocument();
    expect(screen.getByTestId('address')).toHaveTextContent(/^\/explorar\?q=cafe$/);
    await waitFor(() => expect(screen.getAllByRole('link', { name: /Café Cerca|Taquería Lejos/ })
      .filter((link) => link.classList.contains('dc-card')).map((card) => card.getAttribute('href'))).toEqual(['/m/lejos', '/m/cerca']));
    expect(screen.queryByText(/a 300 m|a 12 km/)).not.toBeInTheDocument();
    expect(geolocation).toHaveBeenCalledTimes(1);
  });

  it('opened with «Cerca de mí» waits for a tap before asking, and says what to do when the browser does not tell (Fase 24G)', async () => {
    const denied = mockGeolocation({ code: 1 });
    api.getDirectory.mockResolvedValue(directory([business({ slug: 'cerca', name: 'Café Cerca' })]));
    const user = userEvent.setup();
    renderPage('/explorar?cerca=1');

    // A shared link never opens the browser's prompt by itself: the list shows as always meanwhile.
    expect(await screen.findByText('Usa tu ubicación para ver primero lo más cercano.')).toHaveAttribute('role', 'status');
    expect(await screen.findByRole('link', { name: /^Café Cerca/ })).toHaveAttribute('href', '/m/cerca');
    expect(denied).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Usar mi ubicación' }));
    expect(await screen.findByText('Permite tu ubicación en el navegador para ver primero lo más cercano.')).toBeInTheDocument();
    expect(denied).toHaveBeenCalledTimes(1);
    expect(api.getDirectory).not.toHaveBeenCalledWith(expect.objectContaining({ latitude: expect.any(Number) }), expect.anything());

    // Allowed now, «Intentar de nuevo» asks again; the focus waits on the chip.
    mockGeolocation({ latitude: 19.0436, longitude: -98.1986 });
    await user.click(screen.getByRole('button', { name: 'Intentar de nuevo' }));
    await waitFor(() => expect(api.getDirectory).toHaveBeenLastCalledWith(
      expect.objectContaining({ latitude: 19.044, longitude: -98.199 }), expect.anything()));
    expect(screen.queryByRole('button', { name: 'Intentar de nuevo' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cerca de mí', pressed: true })).toHaveFocus();
  });

  it('opened with «Cerca de mí» asks at once when the browser already allows it (Fase 24G)', async () => {
    allowLocation();
    const geolocation = mockGeolocation({ latitude: 19.0436, longitude: -98.1986 });
    api.getDirectory.mockResolvedValue(directory([business({ slug: 'cerca', name: 'Café Cerca', distanceKm: 0.3 })]));
    renderPage('/explorar?cerca=1');

    await waitFor(() => expect(api.getDirectory).toHaveBeenLastCalledWith(
      expect.objectContaining({ latitude: 19.044, longitude: -98.199 }), expect.anything()));
    expect(await screen.findByText('Primero lo más cercano a ti.')).toBeInTheDocument();
    expect(geolocation).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Usar mi ubicación' })).not.toBeInTheDocument();
  });
});
