import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import type { DiningRoom, OpenTabSummary } from '../../api/tablesApi';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { KitchenTicketStatus, TenantCapability, UnitType, type OrderDto } from '../../types';
import TablesPage from './TablesPage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../api/ordersApi', () => ({
  ordersApi: { getById: vi.fn(), cancelTabItem: vi.fn(), markRoundServed: vi.fn(), closeTab: vi.fn(), moveTab: vi.fn() },
}));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));

/** The orders hub: each screen subscribes to its events, and the test sends them. */
const hub = vi.hoisted(() => {
  const handlers = new Map<string, () => void>();
  const on = (name: string) => (callback: () => void) => {
    handlers.set(name, callback);
    return () => {
      handlers.delete(name);
    };
  };
  return {
    handlers,
    api: {
      onOrderCreated: on('created'),
      onOrderStatusChanged: on('status'),
      onOrderCancelled: on('cancelled'),
      onKitchenTicketsChanged: on('kitchen'),
      onConnected: on('connected'),
    },
  };
});
vi.mock('../../hooks/useOrderSignalR', () => ({ useOrderSignalR: () => hub.api }));

const request = vi.mocked(apiRequest);
const prompt = vi.fn();
const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };

/** The clock of every test: 21:00 UTC of a Friday. */
const NOW = new Date('2026-10-09T21:00:00Z');
const minutesAgo = (minutes: number) => new Date(NOW.getTime() - minutes * 60_000).toISOString();

/**
 * Mesa 2 has a tab open for an hour and a quarter (and 40 seconds): $380 with $200 already paid, one round ready and one in
 * the kitchen.
 */
const MESA_2_TAB: OpenTabSummary = {
  orderId: 'o2', orderNumber: 'ORD-000002', customerName: 'Público en general', tableNumber: 'Mesa 2', total: 380, balance: 180,
  openedAt: minutesAgo(75 + 40 / 60), rounds: 2, roundsInKitchen: 1, roundsReady: 1,
};

/**
 * A tab at the bar with no table, opened 59 minutes and 40 seconds ago: $95, of which $50 are paid. As Vender opens it, its
 * name is in the table number and the customer is the walk-in one.
 */
const BAR_TAB: OpenTabSummary = {
  orderId: 'o9', orderNumber: 'ORD-000009', customerName: 'Público en general', tableNumber: 'Barra - Juan', total: 95, balance: 45,
  openedAt: minutesAgo(59 + 40 / 60), rounds: 1, roundsInKitchen: 0, roundsReady: 0,
};

const MESA_1 = 'Mesa 1, libre, 4 lugares, abrir cuenta';
const MESA_2 = 'Mesa 2, ocupada, $180.00 por cobrar de $380.00, abierta hace 1 h 15 min, 2 rondas, 1 lista, 1 en cocina';
const BAR = 'Barra - Juan, $45.00 por cobrar de $95.00, abierta hace 59 min, 1 ronda';

const ROOM: DiningRoom = {
  tables: [
    { id: 't1', name: 'Mesa 1', area: 'Terraza', seats: 4, sortOrder: 1, isActive: true },
    { id: 't2', name: 'Mesa 2', area: 'Terraza', seats: null, sortOrder: 2, isActive: true, openTab: MESA_2_TAB },
    // Without an area, the table is in the Salón.
    { id: 't3', name: 'Mesa 3', area: '  ', seats: 2, sortOrder: 3, isActive: true },
  ],
  tabsWithoutTable: [BAR_TAB],
};

function order(overrides: Partial<OrderDto> = {}): OrderDto {
  return {
    id: 'o2', orderNumber: 'ORD-000002', tableNumber: 'Mesa 2', diningTableId: 't2', customerName: 'Público en general',
    createdByBusiness: true, isTab: true, isOpenTab: true, total: 380, paidAmount: 200, balance: 180,
    items: [{
      id: 'i1', productId: 'p1', productName: 'Tacos al pastor', quantity: 4, unitPrice: 95, totalPrice: 380, chargedPrice: 380,
      taxRate: 16, isTaxExempt: false, unit: UnitType.Unit, round: 1,
    }],
    kitchenTickets: [{ id: 'k1', round: 1, status: KitchenTicketStatus.Ready, sentAt: minutesAgo(60) }],
    itemCancellations: [],
    ...overrides,
  } as OrderDto;
}

function Elsewhere() {
  const location = useLocation();
  return <p>En {location.pathname}{location.search}</p>;
}

/** The tables and every reading of them answer with what the test says, in turn. */
function room(...replies: Array<() => unknown>) {
  let next = 0;
  request.mockImplementation(((_method: string, path: string) => {
    if (path !== '/api/v1/tables') return Promise.reject(new Error(`Sin respuesta para ${path}`));
    const reply = replies[Math.min(next++, replies.length - 1)];
    return Promise.resolve().then(reply);
  }) as never);
}

const tableReads = () => request.mock.calls.filter(([, path]) => path === '/api/v1/tables').length;

function renderTables(permissions: string[] = ['ManageOrders', 'ChargeOrders', 'ManageCatalog']) {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'Employee', permissions } } as unknown as ReturnType<typeof useAuth>);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const { unmount } = render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/tables']}>
        <Routes>
          <Route path="/tables" element={<TablesPage />} />
          <Route path="*" element={<Elsewhere />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return { client, unmount };
}

describe('TablesPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    hub.handlers.clear();
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    vi.setSystemTime(NOW);
    vi.mocked(useTenant).mockReturnValue({
      tenant: { id: 'tn1', name: 'Fonda Lupita' }, capabilities: TenantCapability.Food | TenantCapability.Kitchen,
    } as unknown as ReturnType<typeof useTenant>);
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useConfirm).mockReturnValue({ confirm: vi.fn(), prompt } as unknown as ReturnType<typeof useConfirm>);
    vi.mocked(ordersApi.getById).mockImplementation(((id: string) => Promise.resolve({
      data: id === 'o9'
        ? order({ id: 'o9', orderNumber: 'ORD-000009', tableNumber: 'Barra - Juan', diningTableId: undefined, total: 95, paidAmount: 50, balance: 45 })
        : order(),
    })) as never);
    room(() => ROOM);
  });

  afterEach(() => vi.useRealTimers());

  it('shows each table by area with its open tab, and how many are busy, all of it in the name of each card', async () => {
    renderTables();

    expect(await screen.findByText('1 de 3 ocupadas')).toBeInTheDocument();
    const terrace = screen.getByRole('region', { name: 'Terraza' });
    expect(within(terrace).getByRole('heading', { name: 'Terraza' })).toBeInTheDocument();
    expect(within(terrace).getByRole('button', { name: MESA_1 })).toHaveTextContent('Mesa 1Libre · 4 lugares');
    // The total with how long it has been open, its rounds and where they are; its name says what is left to charge too.
    expect(within(terrace).getByRole('button', { name: MESA_2 })).toHaveTextContent('Mesa 2$380.001 h 15 min · 2 rondas1 lista1 en cocina');
    const hall = screen.getByRole('region', { name: 'Salón' });
    expect(within(hall).getByRole('button', { name: 'Mesa 3, libre, 2 lugares, abrir cuenta' })).toHaveTextContent('Mesa 3Libre · 2 lugares');

    const tabs = screen.getByRole('region', { name: 'Cuentas sin mesa' });
    expect(within(tabs).getByRole('button', { name: BAR })).toHaveTextContent('Barra - Juan$95.0059 min');
  });

  it('names no area when every table is in the same one, and counts the rounds ready of each tab', async () => {
    room(() => ({
      tables: [ROOM.tables[0], { ...ROOM.tables[1], openTab: undefined, seats: null }],
      // A tab opened by a device a few seconds ahead counts no time yet.
      tabsWithoutTable: [{ ...BAR_TAB, customerName: 'Ana', tableNumber: null, openedAt: minutesAgo(-0.2), rounds: 3, roundsReady: 2, roundsInKitchen: 1 }],
    }));
    renderTables();

    expect(await screen.findByText('0 de 2 ocupadas')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Terraza' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Mesa 2, libre, abrir cuenta' })).toHaveTextContent(/^Mesa 2Libre$/);
    expect(screen.getByRole('button', { name: 'Ana, $45.00 por cobrar de $95.00, abierta hace 0 min, 3 rondas, 2 listas, 1 en cocina' }))
      .toHaveTextContent('Ana$95.000 min2 listas1 en cocina');
  });

  it('opens a tab in Vender from a free table', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderTables();

    await user.click(await screen.findByRole('button', { name: MESA_1 }));
    expect(screen.getByText('En /sell?table=t1&label=Mesa%201')).toBeInTheDocument();
  });

  it('opens a tab without a table in the name of someone, up to 50 letters, and nothing when the name is not given', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    prompt.mockResolvedValueOnce(null).mockResolvedValueOnce(`Barra - Ana & Luis ${'x'.repeat(40)}`);
    renderTables();
    await screen.findByText('1 de 3 ocupadas');

    await user.click(screen.getByRole('button', { name: 'Cuenta sin mesa' }));
    expect(prompt).toHaveBeenCalledWith(expect.objectContaining({ title: 'Cuenta sin mesa', required: true, maxLength: 50 }));
    expect(screen.getByText('1 de 3 ocupadas')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Cuenta sin mesa' }));
    const label = encodeURIComponent(`Barra - Ana & Luis ${'x'.repeat(31)}`);
    expect(await screen.findByText(`En /sell?label=${label}`)).toBeInTheDocument();
  });

  it('shows the bill of a busy table, with the free tables to move it to, and of a tab without a table', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderTables();

    await user.click(await screen.findByRole('button', { name: MESA_2 }));
    const bill = await screen.findByRole('dialog', { name: 'Mesa 2 · ORD-000002' });
    expect(ordersApi.getById).toHaveBeenCalledWith('o2');
    await user.click(within(bill).getByRole('button', { name: 'Cambiar de mesa' }));
    // Only the free ones, from every area.
    expect(within(bill).getByRole('button', { name: 'Mesa 1' })).toBeInTheDocument();
    expect(within(bill).getByRole('button', { name: 'Mesa 3' })).toBeInTheDocument();
    expect(within(bill).queryByRole('button', { name: 'Mesa 2' })).not.toBeInTheDocument();
    await user.click(within(bill).getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: BAR }));
    expect(await screen.findByRole('dialog', { name: 'Barra - Juan · ORD-000009' })).toBeInTheDocument();
    expect(ordersApi.getById).toHaveBeenLastCalledWith('o9');
  });

  it('reads the tables again with each order or kitchen event, and when the connection comes back', async () => {
    renderTables();
    await screen.findByText('1 de 3 ocupadas');
    expect(tableReads()).toBe(1);

    for (const [event, reads] of [['created', 2], ['status', 3], ['cancelled', 4], ['kitchen', 5], ['connected', 6]] as const) {
      await act(async () => hub.handlers.get(event)?.());
      await waitFor(() => expect(tableReads()).toBe(reads));
    }
  });

  it('reads the tab open in its panel again with each event too', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderTables();
    await user.click(await screen.findByRole('button', { name: MESA_2 }));
    await screen.findByRole('dialog', { name: 'Mesa 2 · ORD-000002' });
    expect(ordersApi.getById).toHaveBeenCalledTimes(1);

    await act(async () => hub.handlers.get('kitchen')?.());
    await waitFor(() => expect(ordersApi.getById).toHaveBeenCalledTimes(2));
  });

  it('reads the tables every half minute, in case the connection dropped, and stops when the page closes', async () => {
    const { unmount } = renderTables();
    await screen.findByText('1 de 3 ocupadas');

    await act(async () => vi.advanceTimersByTime(30_000));
    await waitFor(() => expect(tableReads()).toBe(2));

    unmount();
    // No subscription to the hub and no clock left behind.
    expect(hub.handlers.size).toBe(0);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('counts how long each tab has been open, every half minute, in hours from the hour', async () => {
    renderTables();
    const bar = await screen.findByRole('button', { name: BAR });
    expect(bar).toHaveTextContent(/59 min$/);

    // Until the half minute nothing changes, though the hour has passed (59 min 40 s plus 29 s).
    await act(async () => vi.advanceTimersByTime(29_000));
    expect(bar).toHaveTextContent(/59 min$/);
    await act(async () => vi.advanceTimersByTime(2_000));
    expect(bar).toHaveTextContent(/\$95\.001 h 0 min$/);
    expect(bar).toHaveAccessibleName('Barra - Juan, $45.00 por cobrar de $95.00, abierta hace 1 h 0 min, 1 ronda');
    expect(screen.getByRole('button', { name: /^Mesa 2, ocupada, / })).toHaveTextContent('1 h 16 min');
  });

  it('counts the time from the last reading too, when it comes before the clock', async () => {
    renderTables();
    const bar = await screen.findByRole('button', { name: BAR });

    // 25 seconds later an order event reads the tables again: the time is the one of that reading.
    vi.setSystemTime(new Date(NOW.getTime() + 25_000));
    await act(async () => hub.handlers.get('status')?.());
    await waitFor(() => expect(tableReads()).toBe(2));
    await waitFor(() => expect(bar).toHaveTextContent(/1 h 0 min$/));
  });

  it('says the tables are being read', async () => {
    room(() => new Promise(() => undefined));
    renderTables();

    expect(await screen.findByRole('status')).toHaveTextContent('Cargando las mesas…');
    expect(screen.queryByText(/Agrega tus mesas/)).not.toBeInTheDocument();
  });

  it('says why the tables could not be read, without inviting to add them, and reads them again', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    room(
      () => Promise.reject({ isAxiosError: true, response: { status: 400, data: { error: 'El módulo de mesas no está activo.' } } }),
      () => ROOM,
    );
    renderTables();

    expect(await screen.findByRole('alert')).toHaveTextContent('El módulo de mesas no está activo.');
    expect(screen.queryByText(/Agrega tus mesas/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Agregar mesas' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByText('1 de 3 ocupadas')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('');
  });

  it('keeps the tables when reading them again fails, and its retry waits for the answer', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    let answer: (value: unknown) => void = () => undefined;
    room(
      () => ROOM,
      () => Promise.reject(new Error('Network Error')),
      () => new Promise((resolve) => { answer = resolve; }),
    );
    renderTables();
    await screen.findByText('1 de 3 ocupadas');

    await act(async () => hub.handlers.get('status')?.());
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron cargar las mesas.');
    expect(screen.getByRole('button', { name: MESA_1 })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeDisabled();
    await act(async () => answer(ROOM));
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  });

  it('lets whoever manages the catalog configure the tables, and invites them to add the first', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    room(() => ({ tables: [], tabsWithoutTable: [] }));
    renderTables();

    expect(await screen.findByText('Agrega tus mesas para abrir cuentas por mesa y mandar cada ronda a la cocina.')).toBeInTheDocument();
    // No count of busy tables without tables.
    expect(screen.queryByText(/ocupadas/)).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Agregar mesas' }));
    const dialog = await screen.findByRole('dialog', { name: 'Configurar mesas' });
    await user.click(within(dialog).getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Configurar mesas' }));
    expect(await screen.findByRole('dialog', { name: 'Configurar mesas' })).toBeInTheDocument();
  });

  it('does not offer to configure the tables to whoever does not manage the catalog, nor asks them to add them', async () => {
    room(() => ({ tables: [], tabsWithoutTable: [] }));
    renderTables(['ManageOrders']);

    expect(await screen.findByText('Todavía no hay mesas. Quien administra el catálogo puede agregarlas.')).toBeInTheDocument();
    expect(screen.queryByText(/Agrega tus mesas/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Agregar mesas' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Configurar mesas' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cuenta sin mesa' })).toBeInTheDocument();
  });
});
