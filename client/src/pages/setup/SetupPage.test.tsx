import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Link, MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { tenantApi } from '../../api/tenantApi';
import { useTenant } from '../../hooks/useTenant';
import { TenantCapability, type StarterCatalogDto, type TenantSetupDto } from '../../types';
import SetupPage from './SetupPage';

vi.mock('../../api/tenantApi', () => ({
  tenantApi: { getSetup: vi.fn(), updateMyTenant: vi.fn(), updateSalesPolicy: vi.fn(), installStarterCatalog: vi.fn(), completeSetup: vi.fn() },
}));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const api = vi.mocked(tenantApi);

const CAFE: StarterCatalogDto = {
  id: 'cafe', name: 'Cafetería', description: 'Bebidas calientes y pan.', businessType: 0, productCount: 24,
  categories: ['Café', 'Panadería'], isRecommended: true,
};
const TACOS: StarterCatalogDto = {
  id: 'tacos', name: 'Taquería', description: 'Tacos y bebidas.', businessType: 0, productCount: 18, categories: ['Tacos', 'Bebidas'],
  isRecommended: false,
};
const ABARROTES: StarterCatalogDto = {
  id: 'abarrotes', name: 'Abarrotes', description: 'Básicos de la tienda.', businessType: 1, productCount: 40,
  categories: ['Despensa', 'Bebidas'], isRecommended: false,
};

const SETUP: TenantSetupDto = {
  businessType: 0, capabilities: TenantCapability.Food, pricesIncludeTax: true, defaultTaxRate: 16, completedAt: null, hasProducts: false,
  steps: [
    { key: 'catalog', done: false }, { key: 'cash', done: true }, { key: 'firstSale', done: false }, { key: 'team', done: false },
    { key: 'logo', done: true },
  ],
  starterCatalogs: [ABARROTES, TACOS, CAFE],
};

const TENANT = {
  name: 'Café Luna', contactEmail: 'hola@cafeluna.mx', contactPhone: '5512345678', rfc: 'CLU200101AB1', legalName: 'Café Luna SA',
  industry: 'Cafetería', taxRegime: '601', street: 'Av. Juárez', exteriorNumber: '12', interiorNumber: null, neighborhood: 'Centro',
  zipCode: '72000', city: 'Puebla', state: 'Puebla', pricesIncludeTax: true, defaultTaxRate: 16, autoCloseCounterSales: true,
};

function Elsewhere() {
  return <p>En {useLocation().pathname}</p>;
}

function renderSetup(setup: TenantSetupDto = SETUP) {
  if (!api.getSetup.getMockImplementation()) api.getSetup.mockResolvedValue({ data: setup } as never);
  const refreshTenant = vi.fn().mockResolvedValue(undefined);
  vi.mocked(useTenant).mockReturnValue({ tenant: TENANT, refreshTenant } as unknown as ReturnType<typeof useTenant>);
  window.scrollTo = vi.fn();
  render(
    <MemoryRouter initialEntries={['/setup']}>
      {/* Somewhere else to go, like the menu. */}
      <Link to="/fuera">Salir</Link>
      <Routes>
        <Route path="/setup" element={<SetupPage />} />
        <Route path="*" element={<Elsewhere />} />
      </Routes>
    </MemoryRouter>,
  );
  return { refreshTenant };
}

const step = () => screen.getByRole('list', { name: 'Pasos' }).querySelector('[aria-current="step"]')?.textContent;

describe('SetupPage', () => {
  beforeEach(() => vi.resetAllMocks());

  it('starts with the modules of the business, and goes on without saving them when nothing changed', async () => {
    const user = userEvent.setup();
    renderSetup();

    expect(await screen.findByRole('heading', { name: 'Prepara Café Luna' })).toBeInTheDocument();
    expect(screen.getByText(/^Tu giro es/)).toHaveTextContent('Tu giro es Alimentos.');
    expect(step()).toBe('1Tu negocio');
    expect(screen.getByRole('checkbox', { name: /^Alimentos/ })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: /^Venta de productos/ })).not.toBeChecked();

    await user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByRole('heading', { name: 'Precios e IVA' })).toBeInTheDocument();
    expect(api.updateMyTenant).not.toHaveBeenCalled();
  });

  it('saves the modules chosen with the rest of the business as it is, and reads both again', async () => {
    const user = userEvent.setup();
    api.updateMyTenant.mockResolvedValue({ data: {} } as never);
    const { refreshTenant } = renderSetup();

    await user.click(await screen.findByRole('checkbox', { name: /^Pantalla de cocina/ }));
    await user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByRole('heading', { name: 'Precios e IVA' })).toBeInTheDocument();
    expect(api.updateMyTenant).toHaveBeenCalledWith({
      name: 'Café Luna', contactEmail: 'hola@cafeluna.mx', contactPhone: '5512345678', capabilities: TenantCapability.Food | TenantCapability.Kitchen,
      rfc: 'CLU200101AB1', legalName: 'Café Luna SA', industry: 'Cafetería', taxRegime: '601', street: 'Av. Juárez', exteriorNumber: '12',
      interiorNumber: null, neighborhood: 'Centro', zipCode: '72000', city: 'Puebla', state: 'Puebla',
    });
    expect(refreshTenant).toHaveBeenCalled();
    expect(api.getSetup).toHaveBeenCalledTimes(2);
  });

  it('asks for something to sell, and says why the modules could not be saved', async () => {
    const user = userEvent.setup();
    api.updateMyTenant.mockRejectedValue({ isAxiosError: true, response: { status: 400, data: { detail: 'Tu plan no incluye cocina.' } } });
    renderSetup();

    await user.click(await screen.findByRole('checkbox', { name: /^Alimentos/ }));
    await user.click(screen.getByRole('button', { name: 'Continuar' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Elige al menos venta de productos, alimentos o servicios.');
    expect(api.updateMyTenant).not.toHaveBeenCalled();

    await user.click(screen.getByRole('checkbox', { name: /^Alimentos/ }));
    await user.click(screen.getByRole('checkbox', { name: /^Pantalla de cocina/ }));
    await user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Tu plan no incluye cocina.');
    expect(step()).toBe('1Tu negocio');
  });

  it('saves whether the prices include IVA and its rate', async () => {
    const user = userEvent.setup();
    api.updateSalesPolicy.mockResolvedValue({ data: {} } as never);
    const { refreshTenant } = renderSetup();
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));

    await user.click(await screen.findByRole('radio', { name: /No, el IVA se suma/ }));
    await user.selectOptions(screen.getByLabelText('Tasa general de IVA'), '8');
    await user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByRole('heading', { name: 'Empieza con un catálogo de ejemplo' })).toBeInTheDocument();
    expect(api.updateSalesPolicy).toHaveBeenCalledWith({ pricesIncludeTax: false, defaultTaxRate: 8, autoCloseCounterSales: true });
    expect(refreshTenant).toHaveBeenCalled();
  });

  it('says why the IVA could not be saved, and stays', async () => {
    const user = userEvent.setup();
    api.updateSalesPolicy.mockRejectedValue({ isAxiosError: true, response: { status: 500, data: {} } });
    renderSetup();
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));

    await user.click(await screen.findByRole('button', { name: 'Continuar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo guardar la política de IVA.');
    expect(screen.getByRole('heading', { name: 'Precios e IVA' })).toBeInTheDocument();
  });

  describe('the sample catalog', () => {
    async function toCatalog(user: ReturnType<typeof userEvent.setup>) {
      api.updateSalesPolicy.mockResolvedValue({ data: {} } as never);
      await user.click(await screen.findByRole('button', { name: 'Continuar' }));
      await user.click(await screen.findByRole('button', { name: 'Continuar' }));
      await screen.findByRole('heading', { name: 'Empieza con un catálogo de ejemplo' });
    }

    it('offers the one for the giro first, then the others of its type, and those of other giros apart', async () => {
      const user = userEvent.setup();
      renderSetup();
      await toCatalog(user);

      const offered = screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent);
      expect(offered).toEqual(['Cafetería', 'Taquería', 'Abarrotes']);
      expect(screen.getByText('Abarrotes').closest('details')).toHaveTextContent('Plantillas de otros giros');
      expect(screen.getByText('Cafetería').closest('details')).toBeNull();
      expect(screen.getByText('24 productos en Café, Panadería.')).toBeInTheDocument();
    });

    it('installs one, and nothing else can be done meanwhile', async () => {
      const user = userEvent.setup();
      let installed: (value: unknown) => void = () => undefined;
      api.installStarterCatalog.mockReturnValue(new Promise((resolve) => { installed = resolve; }) as never);
      renderSetup();
      await toCatalog(user);

      const cafe = screen.getByRole('heading', { name: 'Cafetería' }).closest('.card') as HTMLElement;
      await user.click(within(cafe).getByRole('button', { name: 'Usar esta plantilla' }));

      expect(within(cafe).getByRole('button', { name: 'Instalando…' })).toBeDisabled();
      for (const name of ['Usar esta plantilla', 'Atrás', 'Prefiero capturar mis productos', 'Omitir por ahora']) {
        for (const button of screen.getAllByRole('button', { name })) expect(button).toBeDisabled();
      }

      installed({ data: { templateId: 'cafe', templateName: 'Cafetería', productsCreated: 24 } });
      expect(await screen.findByRole('heading', { name: '¡Tu negocio está listo para vender!' })).toBeInTheDocument();
      expect(api.installStarterCatalog).toHaveBeenCalledWith('cafe');
      expect(toast.success).toHaveBeenCalledWith('Listo: 24 productos de ejemplo de Cafetería. Edita los precios cuando quieras.');
      expect(api.getSetup).toHaveBeenCalledTimes(2);
    });

    it('says why it could not be installed, and offers the templates again', async () => {
      const user = userEvent.setup();
      api.installStarterCatalog.mockRejectedValue({ isAxiosError: true, response: { status: 409, data: { detail: 'Ya tienes productos.' } } });
      renderSetup();
      await toCatalog(user);

      const scrolled = vi.mocked(window.scrollTo).mock.calls.length;
      await user.click(screen.getAllByRole('button', { name: 'Usar esta plantilla' })[0]);

      expect(await screen.findByRole('alert')).toHaveTextContent('Ya tienes productos.');
      // Said above the steps: the page goes up to it.
      expect(window.scrollTo).toHaveBeenCalledTimes(scrolled + 1);
      expect(window.scrollTo).toHaveBeenLastCalledWith({ top: 0, behavior: 'smooth' });
      for (const button of screen.getAllByRole('button', { name: 'Usar esta plantilla' })) expect(button).toBeEnabled();
    });

    it('skips it for whoever captures their own products', async () => {
      const user = userEvent.setup();
      renderSetup();
      await toCatalog(user);

      await user.click(screen.getByRole('button', { name: 'Prefiero capturar mis productos' }));

      expect(screen.getByRole('heading', { name: '¡Tu negocio está listo para vender!' })).toBeInTheDocument();
      expect(api.installStarterCatalog).not.toHaveBeenCalled();
    });

    it('offers none to a business that already has products', async () => {
      const user = userEvent.setup();
      api.updateSalesPolicy.mockResolvedValue({ data: {} } as never);
      renderSetup({ ...SETUP, hasProducts: true });
      await user.click(await screen.findByRole('button', { name: 'Continuar' }));
      await user.click(await screen.findByRole('button', { name: 'Continuar' }));

      expect(await screen.findByRole('heading', { name: 'Tu catálogo ya tiene productos' })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Ver productos' })).toHaveAttribute('href', '/products');
      expect(screen.queryByRole('button', { name: 'Usar esta plantilla' })).not.toBeInTheDocument();
    });
  });

  describe('the last step', () => {
    async function toLast(user: ReturnType<typeof userEvent.setup>) {
      api.updateSalesPolicy.mockResolvedValue({ data: {} } as never);
      await user.click(await screen.findByRole('button', { name: 'Continuar' }));
      await user.click(await screen.findByRole('button', { name: 'Continuar' }));
      await user.click(await screen.findByRole('button', { name: 'Prefiero capturar mis productos' }));
    }

    it('lists what is still to do, and finishing goes where the person chose', async () => {
      const user = userEvent.setup();
      api.completeSetup.mockResolvedValue({ data: {} } as never);
      const { refreshTenant } = renderSetup();
      await toLast(user);

      const list = screen.getByRole('heading', { name: '¡Tu negocio está listo para vender!' }).parentElement as HTMLElement;
      // Done: the drawer opened and the logo. To do: products, the first sale and the team.
      expect(within(list).getByRole('link', { name: 'Agregar' })).toHaveAttribute('href', '/products/new');
      expect(within(list).queryByRole('button', { name: 'Abrir caja' })).not.toBeInTheDocument();
      expect(within(list).queryByRole('button', { name: 'Subir logotipo' })).not.toBeInTheDocument();
      expect(within(list).getByRole('button', { name: 'Vender' })).toBeInTheDocument();
      await user.click(within(list).getByRole('button', { name: 'Invitar' }));

      expect(await screen.findByText('En /employees')).toBeInTheDocument();
      expect(api.completeSetup).toHaveBeenCalledTimes(1);
      expect(refreshTenant).toHaveBeenCalled();
    });

    it('says why it could not finish, and stays', async () => {
      const user = userEvent.setup();
      api.completeSetup.mockRejectedValue({ isAxiosError: true, response: { status: 500, data: {} } });
      renderSetup();
      await toLast(user);

      await user.click(screen.getByRole('button', { name: 'Hacer mi primera venta' }));

      expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo terminar el arranque guiado.');
      expect(screen.getByRole('button', { name: 'Hacer mi primera venta' })).toBeEnabled();
      expect(screen.queryByText(/^En \//)).not.toBeInTheDocument();
    });
  });

  it('keeps the IVA chosen and not saved when the person goes back and changes the modules', async () => {
    const user = userEvent.setup();
    api.updateMyTenant.mockResolvedValue({ data: {} } as never);
    renderSetup();
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await user.selectOptions(await screen.findByLabelText('Tasa general de IVA'), '8');

    await user.click(screen.getByRole('button', { name: 'Atrás' }));
    await user.click(screen.getByRole('checkbox', { name: /^Pantalla de cocina/ }));
    await user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByLabelText('Tasa general de IVA')).toHaveValue('8');
    expect(api.getSetup).toHaveBeenCalledTimes(2);
  });

  it('offers no template while it is being skipped', async () => {
    const user = userEvent.setup();
    api.updateSalesPolicy.mockResolvedValue({ data: {} } as never);
    api.completeSetup.mockReturnValue(new Promise(() => undefined) as never);
    renderSetup();
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await screen.findByRole('heading', { name: 'Empieza con un catálogo de ejemplo' });

    await user.click(screen.getByRole('button', { name: 'Omitir por ahora' }));

    for (const name of ['Usar esta plantilla', 'Atrás', 'Prefiero capturar mis productos']) {
      for (const button of screen.getAllByRole('button', { name })) expect(button).toBeDisabled();
    }
  });

  it('stays where the person went when finishing answers after they left', async () => {
    const user = userEvent.setup();
    let completed: (value: unknown) => void = () => undefined;
    api.updateSalesPolicy.mockResolvedValue({ data: {} } as never);
    api.completeSetup.mockReturnValue(new Promise((resolve) => { completed = resolve; }) as never);
    const { refreshTenant } = renderSetup();
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await user.click(await screen.findByRole('button', { name: 'Prefiero capturar mis productos' }));

    await user.click(screen.getByRole('button', { name: 'Hacer mi primera venta' }));
    await user.click(screen.getByRole('link', { name: 'Agregar' }));
    expect(screen.getByText('En /products/new')).toBeInTheDocument();
    completed({ data: {} });

    await waitFor(() => expect(refreshTenant).toHaveBeenCalled());
    expect(screen.getByText('En /products/new')).toBeInTheDocument();
  });

  it('says a template was installed after the person left, without moving the page they went to', async () => {
    const user = userEvent.setup();
    let installed: (value: unknown) => void = () => undefined;
    api.updateSalesPolicy.mockResolvedValue({ data: {} } as never);
    api.installStarterCatalog.mockReturnValue(new Promise((resolve) => { installed = resolve; }) as never);
    renderSetup();
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await user.click((await screen.findAllByRole('button', { name: 'Usar esta plantilla' }))[0]);

    await user.click(screen.getByRole('link', { name: 'Salir' }));
    const scrolled = vi.mocked(window.scrollTo).mock.calls.length;
    installed({ data: { templateId: 'cafe', templateName: 'Cafetería', productsCreated: 24 } });

    await waitFor(() => expect(toast.success).toHaveBeenCalled());
    expect(screen.getByText('En /fuera')).toBeInTheDocument();
    expect(window.scrollTo).toHaveBeenCalledTimes(scrolled);
    expect(api.getSetup).toHaveBeenCalledTimes(1);
  });

  it('does not move the page the person went to when the modules are saved after they left', async () => {
    const user = userEvent.setup();
    let modulesSaved: (value: unknown) => void = () => undefined;
    api.updateMyTenant.mockReturnValue(new Promise((resolve) => { modulesSaved = resolve; }) as never);
    renderSetup();
    await user.click(await screen.findByRole('checkbox', { name: /^Pantalla de cocina/ }));
    await user.click(screen.getByRole('button', { name: 'Continuar' }));
    await user.click(screen.getByRole('link', { name: 'Salir' }));
    const scrolled = vi.mocked(window.scrollTo).mock.calls.length;
    await act(async () => modulesSaved({ data: {} }));
    await waitFor(() => expect(api.getSetup).toHaveBeenCalledTimes(2));
    expect(screen.getByText('En /fuera')).toBeInTheDocument();
    expect(window.scrollTo).toHaveBeenCalledTimes(scrolled);
  });

  it('does not move the page the person went to when the IVA is saved after they left', async () => {
    const user = userEvent.setup();
    let policySaved: (value: unknown) => void = () => undefined;
    api.updateSalesPolicy.mockReturnValue(new Promise((resolve) => { policySaved = resolve; }) as never);
    const { refreshTenant } = renderSetup();
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await user.click(screen.getByRole('link', { name: 'Salir' }));
    const scrolled = vi.mocked(window.scrollTo).mock.calls.length;
    await act(async () => policySaved({ data: {} }));
    await waitFor(() => expect(refreshTenant).toHaveBeenCalled());
    expect(screen.getByText('En /fuera')).toBeInTheDocument();
    expect(window.scrollTo).toHaveBeenCalledTimes(scrolled);
  });

  it('says nothing and moves nothing when a template is refused after the person left', async () => {
    const user = userEvent.setup();
    let refuse: (reason: unknown) => void = () => undefined;
    api.updateSalesPolicy.mockResolvedValue({ data: {} } as never);
    api.installStarterCatalog.mockReturnValue(new Promise((_, reject) => { refuse = reject; }) as never);
    renderSetup();
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await user.click((await screen.findAllByRole('button', { name: 'Usar esta plantilla' }))[0]);

    await user.click(screen.getByRole('link', { name: 'Salir' }));
    const scrolled = vi.mocked(window.scrollTo).mock.calls.length;
    await act(async () => refuse({ isAxiosError: true, response: { status: 409, data: { detail: 'Ya tienes productos.' } } }));

    expect(screen.getByText('En /fuera')).toBeInTheDocument();
    expect(window.scrollTo).toHaveBeenCalledTimes(scrolled);
  });

  it('goes on when the setup cannot be read again after installing a template, and says so in the last step', async () => {
    const user = userEvent.setup();
    api.updateSalesPolicy.mockResolvedValue({ data: {} } as never);
    api.installStarterCatalog.mockResolvedValue({ data: { templateId: 'cafe', templateName: 'Cafetería', productsCreated: 24 } } as never);
    api.getSetup.mockResolvedValueOnce({ data: SETUP } as never).mockRejectedValue(new Error('Network Error'));
    renderSetup();
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));

    await user.click((await screen.findAllByRole('button', { name: 'Usar esta plantilla' }))[0]);

    expect(await screen.findByRole('heading', { name: '¡Tu negocio está listo para vender!' })).toBeInTheDocument();
    // What is still to do may be out of date: the page says it could not read it again, and keeps going.
    expect(screen.getByRole('alert')).toHaveTextContent('No se pudo cargar el arranque guiado.');
    expect(screen.queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument();
  });

  it('goes on when the setup cannot be read again after saving the modules, and says so in the next step', async () => {
    const user = userEvent.setup();
    api.updateMyTenant.mockResolvedValue({ data: {} } as never);
    api.getSetup.mockResolvedValueOnce({ data: SETUP } as never)
      .mockRejectedValue({ isAxiosError: true, response: { status: 400, data: { detail: 'El arranque no está disponible.' } } });
    renderSetup();

    await user.click(await screen.findByRole('checkbox', { name: /^Pantalla de cocina/ }));
    await user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByRole('heading', { name: 'Precios e IVA' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('El arranque no está disponible.');
    expect(api.getSetup).toHaveBeenCalledTimes(2);
  });

  it('can be skipped from the start, which the server remembers', async () => {
    const user = userEvent.setup();
    api.completeSetup.mockResolvedValue({ data: {} } as never);
    renderSetup();

    await user.click(await screen.findByRole('button', { name: 'Omitir por ahora' }));

    expect(await screen.findByText('En /')).toBeInTheDocument();
    expect(api.completeSetup).toHaveBeenCalledTimes(1);
  });

  it('says it could not be read, and reads it again', async () => {
    const user = userEvent.setup();
    let read: (value: unknown) => void = () => undefined;
    api.getSetup.mockRejectedValueOnce({ isAxiosError: true, response: { status: 500, data: {} } })
      .mockReturnValueOnce(new Promise((resolve) => { read = resolve; }) as never);
    renderSetup();

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo cargar el arranque guiado.');
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));

    // While it reads, it says so and is not asked again.
    expect(screen.getByRole('status')).toHaveTextContent('Cargando…');
    expect(screen.queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument();
    read({ data: SETUP });
    expect(await screen.findByRole('heading', { name: 'Prepara Café Luna' })).toBeInTheDocument();
    expect(api.getSetup).toHaveBeenCalledTimes(2);
  });
});
