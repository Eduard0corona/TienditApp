import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OrderChannel, TenantCapability, type TenantProfileDto } from '../../types';
import PlatformPrices from './PlatformPrices';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

type Item = Schemas['PlatformPriceItemDto'];
type Channel = Schemas['DeliveryPlatformChannelDto'];

const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const confirm = vi.fn();

/** A burger of $99 («Sencilla» +$0 or «Doble» +$30), at $125 in the app and «Doble» at +$38; a soda of $25 without one. */
function catalog(patch: { burger?: Partial<Item>; soda?: Partial<Item> } = {}): Item[] {
  return [
    {
      productId: 'burger',
      name: 'Hamburguesa',
      categoryId: 'c1',
      categoryName: 'Hamburguesas',
      isAvailable: true,
      menuPrice: 99,
      platformPrice: 125,
      optionGroups: [
        {
          id: 'size',
          name: 'Tamaño',
          options: [
            { id: 'sencilla', name: 'Sencilla', isAvailable: true, menuPriceDelta: 0, platformPriceDelta: null },
            { id: 'doble', name: 'Doble', isAvailable: true, menuPriceDelta: 30, platformPriceDelta: 38 },
          ],
        },
      ],
      ...patch.burger,
    },
    {
      productId: 'soda',
      name: 'Refresco',
      categoryId: 'c2',
      categoryName: 'Bebidas',
      isAvailable: false,
      menuPrice: 25,
      platformPrice: null,
      optionGroups: [],
      ...patch.soda,
    },
  ];
}

function page(items: Item[]) {
  return { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false };
}

interface Api {
  channels: Channel[];
  /** The list of each platform, read again after a save: the next one in its array each time, the last one afterwards. */
  prices: Partial<Record<OrderChannel, Item[][]>>;
  save?: () => Promise<unknown>;
}

function serve(api: Api) {
  const reads: Partial<Record<OrderChannel, number>> = {};
  vi.mocked(apiRequest).mockImplementation(((method: string, path: string, options?: { path?: { channel: OrderChannel } }) => {
    if (method === 'get' && path === '/api/v1/delivery-platforms/channels') return Promise.resolve(api.channels);
    if (method === 'get' && path === '/api/v1/delivery-platforms/{channel}/prices') {
      const channel = options!.path!.channel;
      const lists = api.prices[channel] ?? [[]];
      const read = reads[channel] ?? 0;
      reads[channel] = read + 1;
      return Promise.resolve(page(lists[Math.min(read, lists.length - 1)]));
    }
    if (method === 'put' && path === '/api/v1/delivery-platforms/{channel}/prices' && api.save) return api.save();
    return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as unknown as typeof apiRequest);
}

function saves() {
  return vi.mocked(apiRequest).mock.calls.filter(([method]) => method === 'put');
}

function apiError(status: number, code: string, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <PlatformPrices />
      </QueryClientProvider>
    </MemoryRouter>,
  );
  return queryClient;
}

const UBER_ON: Channel[] = [
  { channel: OrderChannel.Rappi, isActive: false },
  { channel: OrderChannel.UberEats, isActive: true },
];

describe('PlatformPrices', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useConfirm).mockReturnValue({ confirm, prompt: vi.fn() });
    vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'TenantAdmin', permissions: [] } } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({
      tenant: { id: 't1', name: 'Cocina Lupita', slug: 'lupita' } as TenantProfileDto,
      capabilities: TenantCapability.Food,
      isLoading: false,
      refreshTenant: vi.fn(),
    });
  });

  it('lists the products of the first platform that is on, each option under its product, with the menu and the app prices', async () => {
    serve({ channels: UBER_ON, prices: { [OrderChannel.UberEats]: [catalog()] } });
    renderPage();

    const burger = await screen.findByRole('textbox', { name: 'Precio de Hamburguesa en Uber Eats' });
    expect(burger).toHaveValue('125.00');
    expect(screen.getByRole('columnheader', { name: 'En Uber Eats' })).toBeInTheDocument();
    expect(screen.getByLabelText('Plataforma')).toHaveValue(String(OrderChannel.UberEats));
    expect(within(screen.getByLabelText('Plataforma')).getAllByRole('option').map((option) => option.textContent))
      .toEqual(['Rappi (apagada)', 'Uber Eats', 'DiDi Food (apagada)']);
    const rows = screen.getAllByRole('row').slice(1).map((row) => within(row).getAllByRole('cell').map((cell) => cell.textContent));
    // The menu's price has its column, and goes under the name too for a phone, where the column is hidden.
    expect(rows.map((cells) => cells[0])).toEqual([
      'HamburguesaHamburguesasTu menú: $99.00',
      'Sencilla · TamañoTu menú: $0.00',
      'Doble · TamañoTu menú: +$30.00',
      'RefrescoNo disponibleBebidasTu menú: $25.00',
    ]);
    expect(rows.map((cells) => cells[1])).toEqual(['$99.00', '$0.00', '+$30.00', '$25.00']);
    expect(screen.getByRole('textbox', { name: 'Doble (Tamaño) de Hamburguesa en Uber Eats' })).toHaveValue('38.00');
    // Without a price in the app, the box is empty and shows the menu's.
    const soda = screen.getByRole('textbox', { name: 'Precio de Refresco en Uber Eats' });
    expect(soda).toHaveValue('');
    expect(soda).toHaveAttribute('placeholder', '25.00');
    expect(screen.getByRole('textbox', { name: 'Sencilla (Tamaño) de Hamburguesa en Uber Eats' })).toHaveAttribute('placeholder', '0.00');
    expect(screen.queryByText(/está apagada/)).not.toBeInTheDocument();
    expect(vi.mocked(apiRequest)).toHaveBeenCalledWith('get', '/api/v1/delivery-platforms/{channel}/prices', expect.objectContaining({
      path: { channel: OrderChannel.UberEats },
      query: { pageNumber: 1, pageSize: 500 },
    }));
    // One main action, off until something changes.
    expect(screen.getByRole('button', { name: 'Guardar cambios' })).toHaveClass('btn-primary');
    expect(screen.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled();
    expect(screen.getByText('Sin cambios por guardar.')).toBeInTheDocument();
  });

  it('says when the platform is off, with the way to turn it on for whoever may', async () => {
    serve({ channels: [], prices: { [OrderChannel.Rappi]: [catalog()] } });
    renderPage();

    await screen.findByRole('textbox', { name: 'Precio de Hamburguesa en Rappi' });
    const notice = screen.getByText(/^Rappi está apagada: estos precios se usan cuando la actives en/);
    expect(within(notice).getByRole('link', { name: 'Configuración › Negocio › Plataformas de reparto' }))
      .toHaveAttribute('href', '/settings/business');
  });

  it('saves only the prices that changed, and shows the list the API reads again', async () => {
    serve({
      channels: UBER_ON,
      prices: {
        [OrderChannel.UberEats]: [catalog(), catalog({ burger: { platformPrice: null }, soda: { platformPrice: 32 } })],
      },
      save: () => Promise.resolve(undefined),
    });
    renderPage();

    const burger = await screen.findByRole('textbox', { name: 'Precio de Hamburguesa en Uber Eats' });
    await userEvent.clear(burger);
    await userEvent.type(screen.getByRole('textbox', { name: 'Precio de Refresco en Uber Eats' }), '32');
    expect(screen.getByText('2 precios cambiados.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Precios de Uber Eats guardados.'));
    expect(saves()).toHaveLength(1);
    expect(saves()[0][2]).toEqual({
      path: { channel: OrderChannel.UberEats },
      body: {
        products: [
          { productId: 'burger', price: null, options: [] },
          { productId: 'soda', price: 32, options: [] },
        ],
      },
    });
    expect(await screen.findByText('Sin cambios por guardar.')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Precio de Refresco en Uber Eats' })).toHaveValue('32.00');
    expect(screen.getByRole('textbox', { name: 'Precio de Hamburguesa en Uber Eats' })).toHaveValue('');
  });

  it('keeps the save off while a price is one the API would refuse', async () => {
    serve({ channels: UBER_ON, prices: { [OrderChannel.UberEats]: [catalog()] } });
    renderPage();

    const soda = await screen.findByRole('textbox', { name: 'Precio de Refresco en Uber Eats' });
    await userEvent.type(soda, '0');
    await userEvent.type(screen.getByRole('textbox', { name: 'Doble (Tamaño) de Hamburguesa en Uber Eats' }), '.5');

    expect(soda).toHaveAttribute('aria-invalid', 'true');
    expect(soda).toHaveAccessibleDescription('Mayor a $0 y hasta $1,000,000, con dos decimales.');
    expect(screen.getByRole('textbox', { name: 'Doble (Tamaño) de Hamburguesa en Uber Eats' })).toHaveAccessibleDescription('Hasta $100,000, con «-» si resta y dos decimales.');
    expect(screen.getByText('Revisa los precios marcados en rojo.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled();
  });

  it('works out the prices from the menu, and saves nothing until the person saves', async () => {
    serve({ channels: UBER_ON, prices: { [OrderChannel.UberEats]: [catalog({ burger: { platformPrice: null } })] } });
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Calcular precios' }));
    const dialog = screen.getByRole('dialog', { name: 'Calcular precios de Uber Eats' });
    expect(within(dialog).getByText('Tu precio más un porcentaje, para tus 2 productos. Revisa los precios antes de guardar.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Calcular' })).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText('Tu precio más (%)'), '25');
    await userEvent.selectOptions(within(dialog).getByLabelText('Redondear hacia arriba'), 'A $5');
    expect(within(dialog).getByText(/^Por ejemplo: Hamburguesa, de/)).toHaveTextContent('Por ejemplo: Hamburguesa, de $99.00 a $125.00.');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Calcular' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByRole('textbox', { name: 'Precio de Hamburguesa en Uber Eats' })).toHaveValue('125.00');
    expect(screen.getByRole('textbox', { name: 'Precio de Refresco en Uber Eats' })).toHaveValue('35.00');
    // «Doble» already has its price in the app and «Sencilla» adds nothing: both stay.
    expect(screen.getByRole('textbox', { name: 'Doble (Tamaño) de Hamburguesa en Uber Eats' })).toHaveValue('38.00');
    expect(screen.getByRole('textbox', { name: 'Sencilla (Tamaño) de Hamburguesa en Uber Eats' })).toHaveValue('');
    expect(screen.getByText('2 precios cambiados.')).toBeInTheDocument();
    expect(saves()).toHaveLength(0);
  });

  it('asks before changing platform with prices not saved, and the other platform starts with its own', async () => {
    serve({
      channels: UBER_ON,
      prices: { [OrderChannel.UberEats]: [catalog()], [OrderChannel.Rappi]: [catalog({ burger: { platformPrice: 129 } })] },
    });
    renderPage();

    await userEvent.type(await screen.findByRole('textbox', { name: 'Precio de Refresco en Uber Eats' }), '32');
    confirm.mockResolvedValueOnce(false);
    await userEvent.selectOptions(screen.getByLabelText('Plataforma'), 'Rappi (apagada)');

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Cambios sin guardar',
      message: 'El precio que cambiaste en Uber Eats se pierde si cambias de plataforma.',
    }));
    expect(screen.getByLabelText('Plataforma')).toHaveValue(String(OrderChannel.UberEats));
    expect(screen.getByRole('textbox', { name: 'Precio de Refresco en Uber Eats' })).toHaveValue('32');

    confirm.mockResolvedValueOnce(true);
    await userEvent.selectOptions(screen.getByLabelText('Plataforma'), 'Rappi (apagada)');

    expect(await screen.findByRole('textbox', { name: 'Precio de Hamburguesa en Rappi' })).toHaveValue('129.00');
    expect(screen.getByRole('textbox', { name: 'Precio de Refresco en Rappi' })).toHaveValue('');
    expect(screen.getByText('Sin cambios por guardar.')).toBeInTheDocument();
  });

  it('keeps what was typed and says why when the API refuses the save', async () => {
    serve({
      channels: UBER_ON,
      prices: { [OrderChannel.UberEats]: [catalog()] },
      save: () => Promise.reject(apiError(409, 'concurrency_conflict', 'Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.')),
    });
    renderPage();

    await userEvent.type(await screen.findByRole('textbox', { name: 'Precio de Refresco en Uber Eats' }), '32');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText('Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Precio de Refresco en Uber Eats' })).toHaveValue('32');
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('keeps what was typed on its platform when another one is turned on meanwhile', async () => {
    const api: Api = {
      channels: UBER_ON,
      prices: { [OrderChannel.UberEats]: [catalog()], [OrderChannel.Rappi]: [catalog()] },
      save: () => Promise.resolve(undefined),
    };
    serve(api);
    const queryClient = renderPage();

    await userEvent.type(await screen.findByRole('textbox', { name: 'Precio de Refresco en Uber Eats' }), '32');
    // Someone turns Rappi on: it would now be the first platform on, but the prices typed are for Uber Eats.
    api.channels = [{ channel: OrderChannel.Rappi, isActive: true }, { channel: OrderChannel.UberEats, isActive: true }];
    await queryClient.invalidateQueries({ queryKey: ['delivery-platforms', 'channels'] });
    await waitFor(() => expect(within(screen.getByLabelText('Plataforma')).getAllByRole('option').map((option) => option.textContent))
      .toEqual(['Rappi', 'Uber Eats', 'DiDi Food (apagada)']));

    expect(screen.getByLabelText('Plataforma')).toHaveValue(String(OrderChannel.UberEats));
    expect(screen.getByRole('textbox', { name: 'Precio de Refresco en Uber Eats' })).toHaveValue('32');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    await waitFor(() => expect(saves()).toHaveLength(1));
    expect(saves()[0][2]).toEqual(expect.objectContaining({ path: { channel: OrderChannel.UberEats } }));
  });

  it('discards the changes after asking', async () => {
    serve({ channels: UBER_ON, prices: { [OrderChannel.UberEats]: [catalog()] } });
    renderPage();

    await userEvent.type(await screen.findByRole('textbox', { name: 'Precio de Refresco en Uber Eats' }), '32');
    confirm.mockResolvedValueOnce(true);
    await userEvent.click(screen.getByRole('button', { name: 'Descartar' }));

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Descartar cambios', message: 'Se pierde el precio que cambiaste.' }));
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Precio de Refresco en Uber Eats' })).toHaveValue(''));
    expect(screen.getByText('Sin cambios por guardar.')).toBeInTheDocument();
  });
});
