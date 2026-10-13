import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { queryKeys } from '../../api/queries';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OrderChannel, PlatformCommissionBase, TenantCapability, type TenantProfileDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import DeliveryPlatformsCard from './DeliveryPlatformsCard';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

type Platform = Schemas['DeliveryPlatformDto'];
type Method = 'get' | 'put';

const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const confirm = vi.fn();

/** Never saved: off, without a commission and with the fiscal defaults, as the API answers it. */
function unconfigured(channel: OrderChannel): Platform {
  return {
    channel,
    isConfigured: false,
    isActive: false,
    commissionPercent: null,
    commissionBase: PlatformCommissionBase.TotalWithTax,
    commissionVatPercent: 16,
    isrWithholdingPercent: 2.5,
    vatWithholdingPercent: 50,
    revision: 0,
    changedAt: null,
    changedBy: null,
  };
}

// DiDi's method on $1,000 with IVA at 16 % included, 30 % on the total, ISR 2.5 % and half the IVA withheld.
const EXAMPLE = { orderTotal: 1000, orderVatPercent: 16, commissionAndTax: 348, withheld: 90.52, expectedSettlement: 561.48 };

function configured(channel: OrderChannel, patch: Partial<Platform> = {}): Platform {
  return {
    ...unconfigured(channel),
    isConfigured: true,
    isActive: true,
    commissionPercent: 30,
    revision: 3,
    changedAt: '2026-10-11T18:00:00Z',
    changedBy: 'ana@cafelaesquina.mx',
    example: EXAMPLE,
    ...patch,
  };
}

function list(...platforms: Platform[]): Schemas['DeliveryPlatformsDto'] {
  return { platforms };
}

const NONE_SAVED = list(unconfigured(OrderChannel.Rappi), unconfigured(OrderChannel.UberEats), unconfigured(OrderChannel.DidiFood));

/** Answers each method of the API with its function; one without an answer fails the test. */
function serve(responses: Partial<Record<Method, () => Promise<unknown>>>) {
  vi.mocked(apiRequest).mockImplementation(((method: Method) => {
    const respond = responses[method];
    return respond ? respond() : Promise.reject(new Error(`Sin respuesta para ${method}`));
  }) as unknown as typeof apiRequest);
}

function calls(method: Method) {
  return vi.mocked(apiRequest).mock.calls.filter(([called]) => called === method);
}

function apiError(status: number, code: string, detail: string, errors?: Record<string, string[]>): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail, errors } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <DeliveryPlatformsCard />
      </QueryClientProvider>
    </MemoryRouter>,
  );
  return queryClient;
}

/** The row of a platform in the card, by its name. */
async function row(name: string) {
  const rows = await screen.findAllByRole('listitem');
  const found = rows.find((item) => within(item).queryByText(name, { exact: true }));
  if (!found) throw new Error(`No hay fila de ${name}`);
  return found;
}

describe('DeliveryPlatformsCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    // Who changed each platform and when is written in the business's time zone, whatever the machine's.
    setBusinessTimeZone('America/Mexico_City');
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useConfirm).mockReturnValue({ confirm, prompt: vi.fn() });
    // The owner, as in Configuración › Negocio.
    vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'TenantAdmin', permissions: [] } } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({
      tenant: { id: 't1', name: 'Cocina Lupita', slug: 'lupita', timeZone: 'America/Mexico_City' } as TenantProfileDto,
      capabilities: 0,
      isLoading: false,
      refreshTenant: vi.fn(),
    });
  });

  afterEach(() => {
    setBusinessTimeZone(null);
  });

  it('lists the three platforms: the ones never saved to configure, the others with their terms and the example of the server', async () => {
    serve({
      get: () => Promise.resolve(list(
        unconfigured(OrderChannel.Rappi),
        configured(OrderChannel.UberEats),
        configured(OrderChannel.DidiFood, { isActive: false, commissionBase: PlatformCommissionBase.SubTotalWithoutTax, example: undefined }),
      )),
    });
    renderCard();

    expect(screen.getByText('Cargando…')).toBeInTheDocument();
    const rappi = await row('Rappi');
    expect(within(rappi).getByText('Sin configurar')).toBeInTheDocument();
    expect(within(rappi).getByRole('button', { name: 'Configurar Rappi' })).toHaveTextContent('Configurar');
    expect(within(rappi).queryByText(/Comisión/)).not.toBeInTheDocument();

    const uber = await row('Uber Eats');
    expect(within(uber).getByText('Activa')).toHaveClass('ta-chip', 'ta-chip-ready');
    expect(within(uber).getByText('Comisión 30 % sobre el total con IVA + IVA 16 % · Retiene ISR 2.5 % e IVA 50 %')).toBeInTheDocument();
    expect(within(uber).getByText(
      'Con un pedido de $1,000.00 con IVA al 16 % incluido cobrado por Uber Eats: comisión e IVA $348.00, retenciones $90.52, te deposita $561.48.',
    )).toBeInTheDocument();
    expect(within(uber).getByText(/^Cambió ana@cafelaesquina\.mx el 11 oct/)).toBeInTheDocument();
    expect(within(uber).getByRole('button', { name: 'Editar Uber Eats' })).toHaveTextContent('Editar');

    const didi = await row('DiDi Food');
    expect(within(didi).getByText('No activa')).toHaveClass('ta-chip', 'ta-chip-done');
    expect(within(didi).getByText(/Comisión 30 % sobre el subtotal sin IVA/)).toBeInTheDocument();
    // Terms stored outside today's limits come without an example: the row still shows, to be fixed.
    expect(within(didi).queryByText(/Con un pedido de/)).not.toBeInTheDocument();
  });

  it('offers to try again when the platforms cannot be read', async () => {
    serve({ get: () => Promise.reject(apiError(500, 'server_error', 'Ocurrió un error inesperado.')) });
    renderCard();

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron cargar tus plataformas de reparto.');

    serve({ get: () => Promise.resolve(NONE_SAVED) });
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByRole('button', { name: 'Configurar Rappi' })).toBeInTheDocument();
  });

  it('opens the terms in a dialog with the help of each platform and the fiscal and price notices', async () => {
    serve({ get: () => Promise.resolve(NONE_SAVED) });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Configurar Uber Eats' }));
    const dialog = screen.getByRole('dialog', { name: 'Uber Eats' });

    // Configuring a platform is to take its orders: it starts on, with the fiscal defaults and no commission made up.
    expect(within(dialog).getByRole('switch', { name: 'Recibo pedidos de Uber Eats' })).toBeChecked();
    expect(within(dialog).getByLabelText('Comisión (%)')).toHaveValue('');
    expect(within(dialog).getByLabelText('Comisión (%)')).toHaveAccessibleDescription(
      // A percentage never wraps away from its sign.
      'Uber publica 25\u00a0% (Lite), 27\u00a0% (Plus) y 29\u00a0% (Premium) más IVA; súmale marketing o Uber One si los pagas.',
    );
    expect(within(dialog).getByRole('radio', { name: 'El total del pedido con IVA' })).toBeChecked();
    expect(within(dialog).getByRole('radio', { name: 'El subtotal sin IVA' })).not.toBeChecked();
    expect(within(dialog).getByLabelText('IVA de la comisión')).toHaveValue('16');
    expect(within(dialog).getAllByRole('option').map((option) => option.textContent?.replace(/\s/g, ' '))).toEqual(['16 %', '8 %', '0 %']);
    expect(within(dialog).getByRole('heading', { name: 'Retenciones cuando cobra la plataforma' })).toBeInTheDocument();
    expect(within(dialog).getByLabelText('ISR (%)')).toHaveValue('2.5');
    expect(within(dialog).getByLabelText('IVA retenido (% del IVA del pedido)')).toHaveValue('50');
    expect(within(dialog).getByText(/^Las tasas por omisión son las de 2026 \(Ley de Ingresos de la Federación y Ley del IVA\)/))
      .toHaveClass('alert', 'alert-warning');
    // Without the catalog (a business with no products), where the prices are set is only named.
    const prices = within(dialog).getByText(/^Los pedidos de Uber Eats se cobran con los precios que pongas en Catálogo › Precios en apps/);
    expect(prices).toHaveClass('alert', 'alert-light', 'border');
    expect(prices).toHaveTextContent('los productos que no tengan uno, con el de tu menú.');
    expect(within(dialog).queryByRole('link', { name: 'Catálogo › Precios en apps' })).not.toBeInTheDocument();
    // One main action in the dialog.
    expect(within(dialog).getByRole('button', { name: 'Guardar' })).toHaveClass('btn-primary');
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toHaveClass('btn-light');
  });

  it('takes whoever manages the catalog to the prices of the platform from its terms', async () => {
    vi.mocked(useTenant).mockReturnValue({
      tenant: { id: 't1', name: 'Cocina Lupita', slug: 'lupita', timeZone: 'America/Mexico_City' } as TenantProfileDto,
      capabilities: TenantCapability.Food,
      isLoading: false,
      refreshTenant: vi.fn(),
    });
    serve({ get: () => Promise.resolve(NONE_SAVED) });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Configurar Rappi' }));
    const dialog = screen.getByRole('dialog', { name: 'Rappi' });

    expect(within(dialog).getByRole('link', { name: 'Catálogo › Precios en apps' })).toHaveAttribute('href', '/platform-prices');
    expect(within(dialog).getByText(/^Los pedidos de Rappi se cobran con los precios que pongas en/)).toBeInTheDocument();
  });

  it('asks for the commission of the contract before saving', async () => {
    serve({ get: () => Promise.resolve(NONE_SAVED) });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Configurar Rappi' }));
    const dialog = screen.getByRole('dialog', { name: 'Rappi' });
    expect(within(dialog).getByLabelText('Comisión (%)')).toHaveAccessibleDescription(
      'Rappi la fija en tu contrato; búscala en tu relación de venta.',
    );
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(within(dialog).getByRole('alert')).toHaveTextContent('Escribe la comisión de tu contrato.');
    expect(within(dialog).getByLabelText('Comisión (%)')).toHaveAttribute('aria-invalid', 'true');
    expect(calls('put')).toHaveLength(0);
  });

  it('saves a platform for the first time with revision 0, shows the list the API answers and tells Vender', async () => {
    const saved = list(configured(OrderChannel.Rappi, { revision: 1 }), unconfigured(OrderChannel.UberEats), unconfigured(OrderChannel.DidiFood));
    serve({ get: () => Promise.resolve(NONE_SAVED), put: () => Promise.resolve(saved) });
    const queryClient = renderCard();
    // Vender and Pedidos read which platforms are active.
    queryClient.setQueryData(queryKeys.deliveryPlatformChannels, []);

    await userEvent.click(await screen.findByRole('button', { name: 'Configurar Rappi' }));
    const dialog = screen.getByRole('dialog', { name: 'Rappi' });
    await userEvent.type(within(dialog).getByLabelText('Comisión (%)'), '30');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Rappi quedó activa.'));
    expect(calls('put')).toEqual([[
      'put',
      '/api/v1/delivery-platforms/{channel}',
      {
        path: { channel: OrderChannel.Rappi },
        body: {
          revision: 0,
          isActive: true,
          commissionPercent: 30,
          commissionBase: PlatformCommissionBase.TotalWithTax,
          commissionVatPercent: 16,
          isrWithholdingPercent: 2.5,
          vatWithholdingPercent: 50,
        },
      },
    ]]);
    expect(screen.queryByRole('dialog', { name: 'Rappi' })).not.toBeInTheDocument();
    expect(within(await row('Rappi')).getByText('Activa')).toBeInTheDocument();
    // The answer replaced the list: nothing is read again for it.
    expect(calls('get')).toHaveLength(1);
    expect(queryClient.getQueryState(queryKeys.deliveryPlatformChannels)?.isInvalidated).toBe(true);
  });

  it('edits the terms with the revision read and the base and IVA chosen', async () => {
    const uber = configured(OrderChannel.UberEats);
    const saved = list(unconfigured(OrderChannel.Rappi), configured(OrderChannel.UberEats, { revision: 4, commissionPercent: 27.5 }), unconfigured(OrderChannel.DidiFood));
    serve({ get: () => Promise.resolve(list(unconfigured(OrderChannel.Rappi), uber, unconfigured(OrderChannel.DidiFood))), put: () => Promise.resolve(saved) });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Editar Uber Eats' }));
    const dialog = screen.getByRole('dialog', { name: 'Uber Eats' });
    expect(within(dialog).getByLabelText('Comisión (%)')).toHaveValue('30');
    await userEvent.clear(within(dialog).getByLabelText('Comisión (%)'));
    await userEvent.type(within(dialog).getByLabelText('Comisión (%)'), '27,5');
    await userEvent.click(within(dialog).getByRole('radio', { name: 'El subtotal sin IVA' }));
    await userEvent.selectOptions(within(dialog).getByLabelText('IVA de la comisión'), '8');
    await userEvent.clear(within(dialog).getByLabelText('ISR (%)'));
    await userEvent.type(within(dialog).getByLabelText('ISR (%)'), '20');
    await userEvent.clear(within(dialog).getByLabelText('IVA retenido (% del IVA del pedido)'));
    await userEvent.type(within(dialog).getByLabelText('IVA retenido (% del IVA del pedido)'), '100');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Uber Eats guardada.'));
    expect(calls('put')[0][2]).toEqual({
      path: { channel: OrderChannel.UberEats },
      body: {
        revision: 3,
        isActive: true,
        commissionPercent: 27.5,
        commissionBase: PlatformCommissionBase.SubTotalWithoutTax,
        commissionVatPercent: 8,
        isrWithholdingPercent: 20,
        vatWithholdingPercent: 100,
      },
    });
    expect(confirm).not.toHaveBeenCalled();
  });

  it('asks before turning a platform off, and saves nothing when the owner keeps it', async () => {
    const uber = configured(OrderChannel.UberEats);
    const saved = list(unconfigured(OrderChannel.Rappi), configured(OrderChannel.UberEats, { isActive: false, revision: 4 }), unconfigured(OrderChannel.DidiFood));
    serve({ get: () => Promise.resolve(list(unconfigured(OrderChannel.Rappi), uber, unconfigured(OrderChannel.DidiFood))), put: () => Promise.resolve(saved) });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Editar Uber Eats' }));
    const dialog = screen.getByRole('dialog', { name: 'Uber Eats' });
    await userEvent.click(within(dialog).getByRole('switch', { name: 'Recibo pedidos de Uber Eats' }));
    confirm.mockResolvedValueOnce(false);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(confirm).toHaveBeenCalledWith({
      title: '¿Desactivar Uber Eats?',
      message: 'Ya no podrás registrar pedidos de Uber Eats en Vender. Los que ya registraste no cambian.',
      confirmLabel: 'Desactivar',
      cancelLabel: 'Cancelar',
      danger: true,
    });
    expect(calls('put')).toHaveLength(0);
    expect(screen.getByRole('dialog', { name: 'Uber Eats' })).toBeInTheDocument();

    confirm.mockResolvedValueOnce(true);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Uber Eats ya no aparece en Vender.'));
    expect(calls('put')[0][2]).toMatchObject({ body: { revision: 3, isActive: false } });
    expect(within(await row('Uber Eats')).getByText('No activa')).toBeInTheDocument();
  });

  it('saves a platform it leaves off without asking', async () => {
    serve({
      get: () => Promise.resolve(NONE_SAVED),
      put: () => Promise.resolve(list(configured(OrderChannel.Rappi, { isActive: false, revision: 1 }), unconfigured(OrderChannel.UberEats), unconfigured(OrderChannel.DidiFood))),
    });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Configurar Rappi' }));
    const dialog = screen.getByRole('dialog', { name: 'Rappi' });
    await userEvent.click(within(dialog).getByRole('switch', { name: 'Recibo pedidos de Rappi' }));
    await userEvent.type(within(dialog).getByLabelText('Comisión (%)'), '0');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Rappi guardada.'));
    expect(confirm).not.toHaveBeenCalled();
    expect(calls('put')[0][2]).toMatchObject({ body: { revision: 0, isActive: false, commissionPercent: 0 } });
  });

  it('reads the list again and closes the dialog when someone else saved the platform meanwhile', async () => {
    const changed = list(configured(OrderChannel.Rappi, { revision: 2, commissionPercent: 25 }), unconfigured(OrderChannel.UberEats), unconfigured(OrderChannel.DidiFood));
    let reads = 0;
    serve({
      get: () => Promise.resolve(reads++ === 0 ? NONE_SAVED : changed),
      put: () => Promise.reject(apiError(409, 'concurrency_conflict', 'Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.')),
    });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Configurar Rappi' }));
    const dialog = screen.getByRole('dialog', { name: 'Rappi' });
    await userEvent.type(within(dialog).getByLabelText('Comisión (%)'), '30');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(
      'Alguien más cambió Rappi al mismo tiempo. Revisa los datos y guarda otra vez.',
    ));
    expect(screen.queryByRole('dialog', { name: 'Rappi' })).not.toBeInTheDocument();
    expect(await screen.findByText(/Comisión 25 % sobre el total con IVA/)).toBeInTheDocument();
    expect(calls('get')).toHaveLength(2);
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('keeps the dialog open with what the API refused', async () => {
    serve({
      get: () => Promise.resolve(NONE_SAVED),
      put: () => Promise.reject(apiError(400, 'validation_failed', 'La solicitud no es válida.', {
        CommissionPercent: ['La comisión va de 0 % a 60 %, con hasta dos decimales.'],
      })),
    });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Configurar Rappi' }));
    const dialog = screen.getByRole('dialog', { name: 'Rappi' });
    await userEvent.type(within(dialog).getByLabelText('Comisión (%)'), '75');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('La comisión va de 0 % a 60 %, con hasta dos decimales.');
    expect(screen.getByRole('dialog', { name: 'Rappi' })).toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('spots a percentage it cannot read before asking the API, and closes without saving on Cancelar', async () => {
    serve({ get: () => Promise.resolve(NONE_SAVED) });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Configurar DiDi Food' }));
    const dialog = screen.getByRole('dialog', { name: 'DiDi Food' });
    expect(within(dialog).getByLabelText('Comisión (%)')).toHaveAccessibleDescription(
      'DiDi la fija en tu contrato (en su ejemplo es 30\u00a0%); búscala en Finanzas › Detalles de pagos.',
    );
    await userEvent.type(within(dialog).getByLabelText('Comisión (%)'), '30');
    await userEvent.clear(within(dialog).getByLabelText('ISR (%)'));
    await userEvent.type(within(dialog).getByLabelText('ISR (%)'), '-1');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(within(dialog).getByRole('alert')).toHaveTextContent('La retención de ISR va de 0 % a 20 %, con hasta dos decimales.');
    expect(within(dialog).getByLabelText('ISR (%)')).toHaveAttribute('aria-invalid', 'true');
    expect(calls('put')).toHaveLength(0);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    expect(screen.queryByRole('dialog', { name: 'DiDi Food' })).not.toBeInTheDocument();
    expect(calls('put')).toHaveLength(0);
  });
});
