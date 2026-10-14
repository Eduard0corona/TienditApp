import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { TenantCapability } from '../../types';
import DeliveryZones from './DeliveryZones';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));
// The section tabs read the signed-in user; they are not what this test is about.
vi.mock('../../components/layout/SectionTabs', () => ({ default: () => null }));
const confirm = vi.fn();
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm }) }));
const tenant = { pricesIncludeTax: true };
// The modules of the business: with the agenda it also goes to its customers' addresses (Fase 22E).
const modules = { capabilities: 0 };
vi.mock('../../hooks/useTenant', () => ({ useTenant: () => ({ tenant, capabilities: modules.capabilities }) }));

type Zone = Schemas['DeliveryZoneDto'];

const CENTRO: Zone = {
  id: 'z1', name: 'Centro', fee: 35, minimumOrder: 150, freeDeliveryFrom: 400, isActive: true, revision: 3,
  postalCodes: ['72000', '72005', '72010', '72020', '72030', '72040'],
};
const NORTE: Zone = { id: 'z2', name: 'Norte', fee: 0, minimumOrder: 0, freeDeliveryFrom: null, isActive: false, revision: 1, postalCodes: ['72100'] };

function serve(zones: Zone[], save: () => Promise<unknown> = () => Promise.resolve(CENTRO)) {
  vi.mocked(apiRequest).mockImplementation(((method: string, path: string) => {
    if (method === 'get' && path === '/api/v1/delivery-zones') return Promise.resolve(zones);
    if (method === 'get' && path === '/api/v1/delivery-zones/home-visits') return Promise.resolve({ travelMinutes: 30 });
    if (method === 'post' || method === 'put' || method === 'delete') return save();
    return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as unknown as typeof apiRequest);
}

function writes() {
  return vi.mocked(apiRequest).mock.calls.filter(([method]) => method !== 'get');
}

function apiError(status: number, code: string, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/settings/delivery-zones']}>
        <DeliveryZones />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DeliveryZones', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    confirm.mockResolvedValue(true);
    tenant.pricesIncludeTax = true;
    modules.capabilities = 0;
  });

  it('shows the travel time between visits only to a business with the agenda', async () => {
    serve([CENTRO]);
    const { unmount } = renderPage();
    await screen.findByText('Centro');
    expect(screen.queryByRole('heading', { name: 'Visitas a domicilio' })).not.toBeInTheDocument();
    unmount();

    modules.capabilities = TenantCapability.Service | TenantCapability.Appointments;
    renderPage();
    expect(await screen.findByRole('heading', { name: 'Visitas a domicilio' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText('Traslado entre citas (minutos)')).toHaveValue(30));
  });

  it('says the amounts go like the prices of the business: with IVA included, or before IVA', async () => {
    serve([CENTRO]);
    const { unmount } = renderPage();
    expect(await screen.findByText(/Los montos son lo que paga tu cliente, con IVA incluido\./)).toBeInTheDocument();
    unmount();

    tenant.pricesIncludeTax = false;
    renderPage();
    expect(await screen.findByText(/El envío va como tus precios, antes de IVA: el pedido le suma el IVA\./)).toBeInTheDocument();
  });

  it('lists each zone with what it charges and its first postal codes', async () => {
    serve([CENTRO, NORTE]);
    renderPage();

    const centro = (await screen.findByText('Centro')).closest('tr')!;
    expect(within(centro).getByText('6 códigos postales')).toBeInTheDocument();
    expect(within(centro).getByText('72000, 72005, 72010, 72020 y 2 más')).toBeInTheDocument();
    expect(within(centro).getByText('$35.00')).toBeInTheDocument();
    expect(within(centro).getByText('$150.00')).toBeInTheDocument();
    expect(within(centro).getByText('$400.00')).toBeInTheDocument();
    expect(within(centro).getByText('Activa')).toBeInTheDocument();
    const norte = screen.getByText('Norte').closest('tr')!;
    expect(within(norte).getByText('Gratis')).toBeInTheDocument();
    expect(within(norte).getByText('Sin mínimo')).toBeInTheDocument();
    expect(within(norte).getByText('Inactiva')).toBeInTheDocument();
    expect(within(norte).getByRole('button', { name: 'Activar Norte' })).toBeInTheDocument();
  });

  it('says what the automatic replies would answer for a postal code', async () => {
    serve([CENTRO, NORTE]);
    renderPage();
    const probe = await screen.findByLabelText('Probar un código postal');

    await userEvent.type(probe, '72005');
    expect(screen.getByRole('status')).toHaveTextContent(
      'Sí llegas al 72005 con «Centro»: envío $35.00 · pedido mínimo $150.00 · envío gratis desde $400.00.');
    await userEvent.clear(probe);
    await userEvent.type(probe, '72100');
    expect(screen.getByRole('status')).toHaveTextContent('El 72100 está en «Norte», que está desactivada: no llegas por ahora.');
    await userEvent.clear(probe);
    await userEvent.type(probe, '7a5000');
    expect(probe).toHaveValue('75000');
    expect(screen.getByRole('status')).toHaveTextContent('Ninguna zona llega al 75000.');
    expect(screen.queryByText(/Ninguna zona está activa/)).not.toBeInTheDocument();
  });

  it('says that the automatic replies do not talk about delivering while no zone is active', async () => {
    serve([NORTE]);
    renderPage();

    expect(await screen.findByText('Ninguna zona está activa: mientras tanto, tus respuestas automáticas no hablan de entregas.'))
      .toBeInTheDocument();
  });

  it('adds a zone with its postal codes once each and the amounts it charges', async () => {
    serve([]);
    renderPage();
    expect(await screen.findByText(/Todavía no tienes zonas de entrega/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Probar un código postal')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /Nueva zona/ }));
    const dialog = screen.getByRole('dialog', { name: 'Nueva zona de entrega' });
    await userEvent.type(within(dialog).getByLabelText('Nombre'), ' Centro ');
    await userEvent.type(within(dialog).getByLabelText('Costo de envío'), '35');
    await userEvent.type(within(dialog).getByLabelText('Pedido mínimo (opcional)'), '150.50');
    await userEvent.type(within(dialog).getByLabelText('Códigos postales'), '72005, 72000\n72005');
    expect(within(dialog).getByText(/2 códigos postales\./)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Agregar zona' }));

    await waitFor(() => expect(writes()).toHaveLength(1));
    expect(writes()[0]).toEqual(['post', '/api/v1/delivery-zones', {
      body: {
        name: 'Centro', fee: 35, minimumOrder: 150.5, freeDeliveryFrom: null, postalCodes: ['72000', '72005'],
        isActive: true, revision: 0,
      },
    }]);
    expect(toast.success).toHaveBeenCalledWith('Zona agregada.');
  });

  it('says what is wrong before saving, and the answer of the server when it refuses', async () => {
    serve([CENTRO], () => Promise.reject(apiError(400, 'bad_request', 'El código postal 72005 ya está en la zona «Centro». Quítalo de ahí primero.')));
    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: /Nueva zona/ }));
    const dialog = screen.getByRole('dialog', { name: 'Nueva zona de entrega' });
    await userEvent.type(within(dialog).getByLabelText('Nombre'), 'Norte');
    await userEvent.type(within(dialog).getByLabelText('Costo de envío'), '0');
    await userEvent.type(within(dialog).getByLabelText('Envío gratis desde (opcional)'), '400');
    await userEvent.type(within(dialog).getByLabelText('Códigos postales'), '72005 7200');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Agregar zona' }));
    expect(within(dialog).getByText('El envío ya es gratis: deja vacío «Envío gratis desde».')).toBeInTheDocument();
    await userEvent.clear(within(dialog).getByLabelText('Envío gratis desde (opcional)'));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Agregar zona' }));
    expect(within(dialog).getByText('«7200» no es un código postal: lleva 5 dígitos.')).toBeInTheDocument();
    expect(writes()).toHaveLength(0);

    await userEvent.clear(within(dialog).getByLabelText('Códigos postales'));
    await userEvent.type(within(dialog).getByLabelText('Códigos postales'), '72005');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Agregar zona' }));
    expect(await within(dialog).findByText('El código postal 72005 ya está en la zona «Centro». Quítalo de ahí primero.')).toBeInTheDocument();
  });

  it('saves a zone with the revision it was read with, and closes it to read it again when someone saved it first', async () => {
    serve([CENTRO], () => Promise.reject(apiError(409, 'concurrency_conflict', 'Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.')));
    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Editar Centro' }));
    const dialog = screen.getByRole('dialog', { name: 'Editar Centro' });
    expect(within(dialog).getByLabelText('Costo de envío')).toHaveValue('35.00');
    expect(within(dialog).getByLabelText('Códigos postales')).toHaveValue('72000, 72005, 72010, 72020, 72030, 72040');

    await userEvent.clear(within(dialog).getByLabelText('Costo de envío'));
    await userEvent.type(within(dialog).getByLabelText('Costo de envío'), '40');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    // Saving it again would be refused the same way: it opens again from the list, read again.
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(toast.error).toHaveBeenCalledWith('Alguien más cambió «Centro» al mismo tiempo. Ábrela otra vez para ver lo que quedó guardado.');
    expect(writes()[0]).toEqual(['put', '/api/v1/delivery-zones/{id}', {
      path: { id: 'z1' },
      body: expect.objectContaining({ fee: 40, freeDeliveryFrom: 400, revision: 3 }),
    }]);
    await waitFor(() => expect(vi.mocked(apiRequest).mock.calls.filter(([method]) => method === 'get')).toHaveLength(2));
  });

  it('closes a zone someone else deleted', async () => {
    serve([CENTRO], () => Promise.reject(apiError(404, 'not_found', 'No se encontró la zona.')));
    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Editar Centro' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(toast.error).toHaveBeenCalledWith('Alguien más eliminó «Centro».');
  });

  it('refuses the name of another zone before saving, without telling capitals apart', async () => {
    serve([CENTRO, NORTE]);
    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Editar Norte' }));
    const dialog = screen.getByRole('dialog', { name: 'Editar Norte' });
    await userEvent.clear(within(dialog).getByLabelText('Nombre'));
    await userEvent.type(within(dialog).getByLabelText('Nombre'), 'centro ');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    expect(within(dialog).getByText('Ya tienes una zona llamada «centro». Ponle otro nombre.')).toBeInTheDocument();
    expect(writes()).toHaveLength(0);
  });

  it('keeps the actions of every zone off while a change is being saved', async () => {
    serve([CENTRO, NORTE], () => new Promise(() => {}));
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Activar Norte' }));

    expect(screen.getByRole('button', { name: 'Activar Norte' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Editar Centro' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Eliminar Centro' })).toBeDisabled();
    expect(writes()).toHaveLength(1);
  });

  it('turns a zone off and deletes one only once confirmed, with their revisions', async () => {
    serve([CENTRO, NORTE], () => Promise.resolve(undefined));
    renderPage();

    confirm.mockResolvedValueOnce(false);
    await userEvent.click(await screen.findByRole('button', { name: 'Desactivar Centro' }));
    expect(writes()).toHaveLength(0);

    await userEvent.click(screen.getByRole('button', { name: 'Desactivar Centro' }));
    await waitFor(() => expect(writes()).toHaveLength(1));
    expect(writes()[0][2]).toEqual(expect.objectContaining({ path: { id: 'z1' }, body: expect.objectContaining({ isActive: false, revision: 3 }) }));

    await userEvent.click(screen.getByRole('button', { name: 'Eliminar Norte' }));
    await waitFor(() => expect(writes()).toHaveLength(2));
    expect(writes()[1]).toEqual(['delete', '/api/v1/delivery-zones/{id}', { path: { id: 'z2' }, query: { revision: 1 } }]);
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ title: 'Eliminar Norte', confirmLabel: 'Eliminar' }));
  });
});
