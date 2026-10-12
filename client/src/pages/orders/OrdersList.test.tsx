import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import type { OrderCreatedPayload } from '../../hooks/useOrderSignalR';
import { useTenant } from '../../hooks/useTenant';
import { OrderChannel, OrderPaymentMethod, OrderType, PlatformCollectedBy, TenantCapability, type OrderDto } from '../../types';
import OrdersList from './OrdersList';

// The handler the list gives SignalR for new orders, to play an event as the hub would.
const realtime = vi.hoisted(() => ({ created: undefined as ((payload: OrderCreatedPayload) => void) | undefined }));

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ prompt: vi.fn(), confirm: vi.fn() }) }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('../../hooks/useArrivalChime', () => ({ useArrivalChime: () => ({ enabled: false, toggle: vi.fn(), play: vi.fn() }) }));
vi.mock('../../hooks/useOrderSignalR', () => {
  const subscribe = () => () => undefined;
  const onOrderCreated = (callback: (payload: OrderCreatedPayload) => void) => {
    realtime.created = callback;
    return () => undefined;
  };
  return {
    useOrderSignalR: () => ({
      onOrderCreated, onOrderStatusChanged: subscribe, onOrderCancelled: subscribe, onConnected: subscribe,
    }),
  };
});
vi.mock('../../api/ordersApi', () => ({ ordersApi: { getAll: vi.fn(), getById: vi.fn() } }));
// The delivery platforms of the business (GET /delivery-platforms/channels) come through the typed client.
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

function givePlatforms(rows: Array<{ channel: OrderChannel; isActive: boolean }>) {
  vi.mocked(apiRequest).mockImplementation(((_method: string, path: string) =>
    Promise.resolve(path === '/api/v1/delivery-platforms/channels' ? rows : undefined)) as unknown as typeof apiRequest);
}

/** An order Rappi charged in its app, as the API lists it: the folio and who charged, never the commission. */
const rappiOrder = (overrides: Partial<OrderDto> = {}) => order({
  id: 'r1', orderNumber: 'ORD-000123', channel: OrderChannel.Rappi, customerName: 'Juan', customerPhone: undefined, tableNumber: undefined,
  platformOrder: { externalOrderNumber: 'BC953', collectedBy: PlatformCollectedBy.Platform },
  total: 350, paidAmount: 350, balance: 0, items: [], taxBreakdown: [], allowedTransitions: [], canCancel: false, refunds: [],
  statusHistory: [], servicePieces: [], pricesIncludeTax: true,
  payments: [{ id: 'p1', amount: 350, method: OrderPaymentMethod.Platform, paidAt: '2026-10-09T16:00:00Z', reference: 'BC953' }],
  ...overrides,
});

const order = (overrides: Partial<OrderDto>): OrderDto => ({
  id: 'o1', orderNumber: 'ORD-000001', orderType: OrderType.Retail, channel: OrderChannel.Counter, createdByBusiness: true,
  customerName: 'Público en general', customerPhone: null, status: 0, createdAt: '2026-10-09T16:00:00Z',
  total: 120, balance: 0, paidAmount: 120, refundedAmount: 0,
  ...overrides,
} as unknown as OrderDto);

function renderOrders(capabilities: number, orders: OrderDto[] = [order({})]) {
  vi.mocked(useTenant).mockReturnValue({ tenant: { name: 'La Esquina' }, capabilities } as unknown as ReturnType<typeof useTenant>);
  vi.mocked(ordersApi.getAll).mockResolvedValue({
    data: { items: orders, pageNumber: 1, totalPages: 1, totalCount: orders.length, hasPreviousPage: false, hasNextPage: false },
  } as Awaited<ReturnType<typeof ordersApi.getAll>>);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/orders']}>
        <OrdersList />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const lastParams = () => vi.mocked(ordersApi.getAll).mock.lastCall?.[0];

describe('OrdersList', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', firstName: 'Mariana', role: 'STAFF', permissions: ['ViewOrders', 'CreateSales', 'ManageOrders'] },
    } as unknown as ReturnType<typeof useAuth>);
    givePlatforms([]);
  });

  it('filters by where the order came from and clears it with the other filters', async () => {
    const user = userEvent.setup();
    renderOrders(TenantCapability.Food);
    await screen.findByText('ORD-000001');

    await user.selectOptions(screen.getByRole('combobox', { name: 'Canal' }), 'WhatsApp');
    await waitFor(() => expect(lastParams()).toMatchObject({ channel: OrderChannel.WhatsApp, pageNumber: 1 }));

    await user.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(lastParams()).not.toHaveProperty('channel'));
    expect(screen.getByRole('combobox', { name: 'Canal' })).toHaveValue('');
  });

  it('says where an order came from instead of who created it', async () => {
    renderOrders(TenantCapability.Food, [order({ channel: OrderChannel.Online, createdByBusiness: false })]);

    const row = (await screen.findByText('ORD-000001')).closest('tr') as HTMLElement;
    expect(within(row).getByText('En línea')).toBeInTheDocument();
    expect(screen.queryByText(/Creada por/)).not.toBeInTheDocument();
  });

  it('leaves out the kind of order when the business only sells, with the states of a sale', async () => {
    renderOrders(TenantCapability.Retail | TenantCapability.Food | TenantCapability.Inventory);
    await screen.findByText('ORD-000001');

    expect(screen.queryByRole('combobox', { name: 'Tipo' })).not.toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'Tipo' })).not.toBeInTheDocument();
    const states = within(screen.getByRole('combobox', { name: 'Estado' })).getAllByRole('option').map((option) => option.textContent);
    expect(states).toContain('Entregada');
    expect(states).not.toContain('Aceptada');
  });

  it('shows the kind of order when the business sells and serves, until one kind is picked', async () => {
    const user = userEvent.setup();
    renderOrders(TenantCapability.Retail | TenantCapability.Service);
    await screen.findByText('ORD-000001');

    expect(screen.getByRole('columnheader', { name: 'Tipo' })).toBeInTheDocument();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Tipo' }), 'Servicio');

    await waitFor(() => expect(lastParams()).toMatchObject({ orderType: OrderType.Service }));
    expect(screen.queryByRole('columnheader', { name: 'Tipo' })).not.toBeInTheDocument();
    const states = within(screen.getByRole('combobox', { name: 'Estado' })).getAllByRole('option').map((option) => option.textContent);
    expect(states).toContain('Aceptada');
    expect(states).not.toContain('Entregada');
  });

  it('offers the delivery platforms in the channel filter once the business set one up, active or not', async () => {
    const user = userEvent.setup();
    const page = renderOrders(TenantCapability.Food);
    await screen.findByText('ORD-000001');
    const channels = () => within(screen.getByRole('combobox', { name: 'Canal' })).getAllByRole('option').map((option) => option.textContent);
    await waitFor(() => expect(apiRequest).toHaveBeenCalled());
    expect(channels()).toEqual(['Todos', 'Mostrador', 'WhatsApp', 'En línea']);
    page.unmount();

    givePlatforms([{ channel: OrderChannel.UberEats, isActive: false }]);
    renderOrders(TenantCapability.Food);
    await waitFor(() => expect(channels()).toEqual(['Todos', 'Mostrador', 'WhatsApp', 'En línea', 'Rappi', 'Uber Eats', 'DiDi Food']));

    await user.selectOptions(screen.getByRole('combobox', { name: 'Canal' }), 'Rappi');
    await waitFor(() => expect(lastParams()).toMatchObject({ channel: OrderChannel.Rappi, pageNumber: 1 }));
  });

  it('marks an order of a platform with its badge, the folio of its app and who charged it', async () => {
    renderOrders(TenantCapability.Food, [
      rappiOrder(),
      rappiOrder({
        id: 'd1', orderNumber: 'ORD-000124', channel: OrderChannel.DidiFood, paidAmount: 0, balance: 245, payments: [],
        platformOrder: { externalOrderNumber: '12', collectedBy: PlatformCollectedBy.Business },
      }),
    ]);

    const rappi = (await screen.findByText('ORD-000123')).closest('tr') as HTMLElement;
    expect(within(rappi).getByText('Rappi')).toHaveClass('badge', 'text-bg-light', 'border');
    expect(within(rappi).getByText('Folio BC953')).toBeInTheDocument();
    expect(within(rappi).getByText('Cobró Rappi')).toBeInTheDocument();
    expect(within(rappi).queryByText('Pagado')).not.toBeInTheDocument();

    const didi = screen.getByText('ORD-000124').closest('tr') as HTMLElement;
    expect(within(didi).getByText('DiDi Food')).toBeInTheDocument();
    expect(within(didi).getByText('Folio 12')).toBeInTheDocument();
    expect(within(didi).getByText('Por cobrar $245.00')).toBeInTheDocument();
  });

  it('shows where an order of a platform came from and who charged it, without a phone, a table or a customer card', async () => {
    const user = userEvent.setup();
    renderOrders(TenantCapability.Food, [rappiOrder()]);

    await user.click(await screen.findByRole('button', { name: 'ORD-000123' }));

    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000123' });
    expect(detail).toHaveTextContent('Origen: Rappi · folio BC953');
    expect(detail).toHaveTextContent('Cobró: Rappi');
    expect(within(detail).queryByText('Teléfono:')).not.toBeInTheDocument();
    expect(within(detail).queryByText('Mesa:')).not.toBeInTheDocument();
    expect(within(detail).queryByRole('button', { name: 'Ver ficha' })).not.toBeInTheDocument();
    // What the platform charged reads with its folio; its commission never reaches Pedidos.
    expect(within(detail).getByRole('cell', { name: 'Plataforma · BC953' })).toBeInTheDocument();
    expect(detail).not.toHaveTextContent(/comisi[oó]n|neto/i);
  });

  it('says the business charged an order the courier paid', async () => {
    const user = userEvent.setup();
    renderOrders(TenantCapability.Food, [rappiOrder({
      channel: OrderChannel.DidiFood,
      platformOrder: { externalOrderNumber: '12', collectedBy: PlatformCollectedBy.Business },
      payments: [{ id: 'p1', amount: 350, method: OrderPaymentMethod.Cash, paidAt: '2026-10-09T16:00:00Z', reference: 'R-1' }],
    })]);

    await user.click(await screen.findByRole('button', { name: 'ORD-000123' }));

    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000123' });
    expect(detail).toHaveTextContent('Origen: DiDi Food · folio 12');
    expect(detail).toHaveTextContent('Cobró: el negocio');
    expect(within(detail).getByRole('cell', { name: 'Efectivo' })).toBeInTheDocument();
    expect(within(detail).getByRole('cell', { name: 'R-1' })).toBeInTheDocument();
  });

  it('charges what the courier owes of an order of a platform without a tip', async () => {
    const user = userEvent.setup();
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', firstName: 'Mariana', role: 'STAFF', permissions: ['ManageOrders', 'ChargeOrders'] },
    } as unknown as ReturnType<typeof useAuth>);
    renderOrders(TenantCapability.Food, [
      rappiOrder({
        channel: OrderChannel.DidiFood, paidAmount: 0, balance: 350, payments: [],
        platformOrder: { externalOrderNumber: '12', collectedBy: PlatformCollectedBy.Business },
      }),
      order({ id: 'c1', orderNumber: 'ORD-000125', balance: 80, paidAmount: 0 }),
    ]);

    const didi = (await screen.findByText('ORD-000123')).closest('tr') as HTMLElement;
    await user.click(within(didi).getByRole('button', { name: 'Cobrar' }));
    const charge = await screen.findByRole('dialog', { name: /Cobrar ORD-000123/ });
    expect(within(charge).queryByRole('radiogroup', { name: 'Propina' })).not.toBeInTheDocument();
    await user.click(within(charge).getByRole('button', { name: 'Volver' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: /Cobrar ORD-000123/ })).not.toBeInTheDocument());

    // A counter order still takes a tip.
    const counter = screen.getByText('ORD-000125').closest('tr') as HTMLElement;
    await user.click(within(counter).getByRole('button', { name: 'Cobrar' }));
    expect(within(await screen.findByRole('dialog', { name: /Cobrar ORD-000125/ })).getByRole('radiogroup', { name: 'Propina' })).toBeInTheDocument();
  });

  it('announces a new order of a platform by its platform and folio', async () => {
    renderOrders(TenantCapability.Food);
    await screen.findByText('ORD-000001');

    act(() => realtime.created?.({
      orderId: 'r9', orderNumber: 'ORD-000126', businessType: 'Food', total: 350, status: 'Confirmed',
      channel: 'Rappi', externalOrderNumber: 'BC953',
    }));

    expect(screen.getByRole('status')).toHaveTextContent('Nuevo pedido ORD-000126 de Rappi (folio BC953)');
  });
});
