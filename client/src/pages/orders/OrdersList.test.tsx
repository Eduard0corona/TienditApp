import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import type { OrderCreatedPayload, OrderUpdatedPayload } from '../../hooks/useOrderSignalR';
import { OnlinePaymentRefundStatus } from '../../api/generated/enums';
import { useTenant } from '../../hooks/useTenant';
import { DeliveryCourier, OrderChannel, OrderPaymentMethod, OrderType, PlatformCollectedBy, TenantCapability, type OrderDto } from '../../types';
import OrdersList from './OrdersList';

// The handler the list gives SignalR for new orders, to play an event as the hub would.
const realtime = vi.hoisted(() => ({
  created: undefined as ((payload: OrderCreatedPayload) => void) | undefined,
  updated: undefined as ((payload: OrderUpdatedPayload) => void) | undefined,
}));

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
// What the person answers in the dialogs of the page (the reason to cancel, a yes or no).
const dialogs = vi.hoisted(() => ({ prompt: vi.fn(), confirm: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => dialogs }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('../../hooks/useArrivalChime', () => ({ useArrivalChime: () => ({ enabled: false, toggle: vi.fn(), play: vi.fn() }) }));
vi.mock('../../hooks/useOrderSignalR', () => {
  const subscribe = () => () => undefined;
  const onOrderCreated = (callback: (payload: OrderCreatedPayload) => void) => {
    realtime.created = callback;
    return () => undefined;
  };
  const onOrderUpdated = (callback: (payload: OrderUpdatedPayload) => void) => {
    realtime.updated = callback;
    return () => undefined;
  };
  return {
    useOrderSignalR: () => ({
      onOrderCreated, onOrderStatusChanged: subscribe, onOrderCancelled: subscribe, onOrderUpdated, onConnected: subscribe,
    }),
  };
});
vi.mock('../../api/ordersApi', () => ({ ordersApi: { getAll: vi.fn(), getById: vi.fn(), cancel: vi.fn(), setPlatformOrderId: vi.fn() } }));
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
      rappiOrder({ platformOrder: { externalOrderNumber: 'BC953', externalOrderId: '4810373920', collectedBy: PlatformCollectedBy.Platform } }),
      rappiOrder({
        id: 'd1', orderNumber: 'ORD-000124', channel: OrderChannel.DidiFood, paidAmount: 0, balance: 245, payments: [],
        platformOrder: { externalOrderNumber: '12', collectedBy: PlatformCollectedBy.Business },
      }),
    ]);

    const rappi = (await screen.findByText('ORD-000123')).closest('tr') as HTMLElement;
    expect(within(rappi).getByText('Rappi')).toHaveClass('badge', 'text-bg-light', 'border');
    expect(within(rappi).getByText('Folio BC953')).toBeInTheDocument();
    // The long id under the folio, when it was written: what its statement names the order by.
    expect(within(rappi).getByText('ID 4810373920')).toBeInTheDocument();
    expect(within(rappi).getByText('Cobró Rappi')).toBeInTheDocument();
    expect(within(rappi).queryByText('Pagado')).not.toBeInTheDocument();

    const didi = screen.getByText('ORD-000124').closest('tr') as HTMLElement;
    expect(within(didi).getByText('DiDi Food')).toBeInTheDocument();
    expect(within(didi).getByText('Folio 12')).toBeInTheDocument();
    expect(within(didi).queryByText(/^ID /)).not.toBeInTheDocument();
    expect(within(didi).getByText('Por cobrar $245.00')).toBeInTheDocument();
  });

  it('adds the long id of a delivered order of a platform in its detail, and reads the order again', async () => {
    const user = userEvent.setup();
    const saved = rappiOrder({ platformOrder: { externalOrderNumber: 'BC953', externalOrderId: '4810373920', collectedBy: PlatformCollectedBy.Platform } });
    vi.mocked(ordersApi.setPlatformOrderId).mockResolvedValue({} as never);
    vi.mocked(ordersApi.getById).mockResolvedValue({ data: saved } as never);
    renderOrders(TenantCapability.Food, [rappiOrder()]);

    await user.click(await screen.findByRole('button', { name: 'ORD-000123' }));
    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000123' });
    expect(detail).toHaveTextContent('ID largo: sin capturar');
    // Esc leaves the field, not the order.
    await user.click(within(detail).getByRole('button', { name: 'Agregar el ID largo del pedido ORD-000123' }));
    await user.keyboard('{Escape}');
    expect(within(detail).queryByLabelText('ID largo del pedido')).not.toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Pedido ORD-000123' })).toBeInTheDocument();

    await user.click(within(detail).getByRole('button', { name: 'Agregar el ID largo del pedido ORD-000123' }));
    await user.type(within(detail).getByLabelText('ID largo del pedido'), '4810373920');
    await user.click(within(detail).getByRole('button', { name: 'Guardar' }));

    expect(ordersApi.setPlatformOrderId).toHaveBeenCalledWith('r1', { externalOrderId: '4810373920' });
    await waitFor(() => expect(detail).toHaveTextContent('ID largo: 4810373920'));
    expect(ordersApi.getById).toHaveBeenCalledWith('r1');
    expect(within(detail).getByRole('button', { name: 'Corregir el ID largo del pedido ORD-000123' })).toBeInTheDocument();
  });

  it('keeps the long id saved though reading the order again fails', async () => {
    const user = userEvent.setup();
    vi.mocked(ordersApi.setPlatformOrderId).mockResolvedValue({} as never);
    vi.mocked(ordersApi.getById).mockRejectedValue(new Error('Network Error'));
    renderOrders(TenantCapability.Food, [rappiOrder()]);

    await user.click(await screen.findByRole('button', { name: 'ORD-000123' }));
    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000123' });
    await user.click(within(detail).getByRole('button', { name: 'Agregar el ID largo del pedido ORD-000123' }));
    await user.type(within(detail).getByLabelText('ID largo del pedido'), '4810373920');
    await user.click(within(detail).getByRole('button', { name: 'Guardar' }));

    // Saved: the field closes instead of saying it could not be saved.
    await waitFor(() => expect(within(detail).queryByLabelText('ID largo del pedido')).not.toBeInTheDocument());
    expect(within(detail).queryByRole('alert')).not.toBeInTheDocument();
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

  it('says what became of each refund given back through Mercado Pago (Fase 24I)', async () => {
    const user = userEvent.setup();
    const refund = (id: string, onlineStatus: number, onlineFailure: string | null = null) => ({
      id, amount: 40, method: OrderPaymentMethod.Online, reason: 'Faltó un refresco', refundedAt: '2026-10-09T17:00:00Z',
      refundedBy: 'Mariana', restockItems: false, lines: [], onlineStatus, onlineFailure,
    });
    renderOrders(TenantCapability.Food, [order({
      channel: OrderChannel.Online, createdByBusiness: false, items: [], taxBreakdown: [], statusHistory: [], servicePieces: [],
      allowedTransitions: [], refundedAmount: 120,
      payments: [{ id: 'p1', amount: 120, method: OrderPaymentMethod.Online, paidAt: '2026-10-09T16:00:00Z', reference: '987654321' }],
      refunds: [
        refund('f1', OnlinePaymentRefundStatus.Requested),
        refund('f2', OnlinePaymentRefundStatus.Completed),
        refund('f3', OnlinePaymentRefundStatus.Failed, 'El pago ya no se puede devolver.'),
      ],
    } as Partial<OrderDto>)]);

    await user.click(await screen.findByRole('button', { name: 'ORD-000001' }));

    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000001' });
    expect(within(detail).getByText('Mercado Pago lo está devolviendo.')).toHaveClass('text-muted');
    expect(within(detail).getByText('Mercado Pago ya lo devolvió.')).toHaveClass('text-success');
    expect(within(detail).getByText('El pago ya no se puede devolver. Devuélvelo desde tu cuenta de Mercado Pago.')).toHaveClass('text-danger');
  });

  it('says «Saldado», not «Pagado», for a sale settled by what was forgiven, and what was undone (Fase 26B)', async () => {
    const user = userEvent.setup();
    const writeOff = (id: string, amount: number, reversedAt: string | null) => ({
      id, amount, reason: 3, notes: null, writtenOffAt: '2026-10-09T17:00:00Z', writtenOffBy: 'Dueña', reversedAt, reversedBy: reversedAt ? 'Admin' : null,
    });
    renderOrders(TenantCapability.Retail, [order({
      customerName: 'Ana López', soldOnCredit: true, total: 100, paidAmount: 0, balance: 0, writtenOffAmount: 100,
      items: [], taxBreakdown: [], statusHistory: [], servicePieces: [], allowedTransitions: [], refunds: [], payments: [],
      writeOffs: [writeOff('w1', 30, '2026-10-09T18:00:00Z'), writeOff('w2', 100, null)],
    } as Partial<OrderDto>)]);

    const row = (await screen.findByRole('button', { name: 'ORD-000001' })).closest('tr')!;
    expect(within(row).getByText('Saldado')).toBeInTheDocument();
    expect(within(row).queryByText('Pagado')).not.toBeInTheDocument();
    expect(within(row).getByText('Condonado $100.00')).toBeInTheDocument();

    await user.click(within(row).getByRole('button', { name: 'ORD-000001' }));
    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000001' });
    expect(within(detail).getByRole('heading', { name: 'Condonado' })).toBeInTheDocument();
    expect(within(detail).getByText(/^Deshecho el .* por Admin$/)).toBeInTheDocument();
    expect(within(detail).getByText('−$30.00')).toHaveClass('text-decoration-line-through');
    expect(within(detail).getByText('−$100.00')).not.toHaveClass('text-decoration-line-through');
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

  it('asks whether the supplies go back when cancelling an order of a platform, and leaves them used by default', async () => {
    const user = userEvent.setup();
    const didi = rappiOrder({
      channel: OrderChannel.DidiFood, status: 1, paidAmount: 0, balance: 350, payments: [], canCancel: true,
      platformOrder: { externalOrderNumber: '12', collectedBy: PlatformCollectedBy.Business },
    });
    renderOrders(TenantCapability.Food, [didi, order({ id: 'c1', orderNumber: 'ORD-000125', status: 1, canCancel: true, paidAmount: 0, balance: 120 })]);
    vi.mocked(ordersApi.getById).mockResolvedValue({ data: didi } as Awaited<ReturnType<typeof ordersApi.getById>>);
    vi.mocked(ordersApi.cancel).mockResolvedValue({} as Awaited<ReturnType<typeof ordersApi.cancel>>);
    dialogs.prompt.mockResolvedValue('DiDi lo canceló');
    dialogs.confirm.mockResolvedValue(false);

    await user.click(await screen.findByRole('button', { name: 'ORD-000123' }));
    await user.click(within(screen.getByRole('dialog', { name: 'Pedido ORD-000123' })).getByRole('button', { name: 'Cancelar pedido' }));

    // The food is usually cooked when the app cancels: closing that question keeps the supplies as used.
    await waitFor(() => expect(ordersApi.cancel).toHaveBeenCalledWith('r1', 'DiDi lo canceló', false));
    expect(dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({
      title: '¿Regresar los insumos al inventario?',
      confirmLabel: 'Sí, regresarlos',
      cancelLabel: 'No, ya se preparó',
    }));
  });

  it('cancels a counter order as always, without asking about the supplies', async () => {
    const user = userEvent.setup();
    const counter = order({ id: 'c1', orderNumber: 'ORD-000125', status: 1, canCancel: true, paidAmount: 0, balance: 120, items: [], payments: [], refunds: [], statusHistory: [], servicePieces: [], allowedTransitions: [], taxBreakdown: [] });
    renderOrders(TenantCapability.Food, [counter]);
    vi.mocked(ordersApi.getById).mockResolvedValue({ data: counter } as Awaited<ReturnType<typeof ordersApi.getById>>);
    vi.mocked(ordersApi.cancel).mockResolvedValue({} as Awaited<ReturnType<typeof ordersApi.cancel>>);
    dialogs.prompt.mockResolvedValue('');

    await user.click(await screen.findByRole('button', { name: 'ORD-000125' }));
    await user.click(within(screen.getByRole('dialog', { name: 'Pedido ORD-000125' })).getByRole('button', { name: 'Cancelar pedido' }));

    await waitFor(() => expect(ordersApi.cancel).toHaveBeenCalledWith('c1', undefined, undefined));
    expect(dialogs.confirm).not.toHaveBeenCalled();
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

  it('marks an order on its way with the courier, and what Paquetenvia owes of one delivered', async () => {
    const delivery = {
      postalCode: '72000', addressLine: 'Av. Reforma 123', zoneName: 'Centro', fee: 35, feeTaxRate: 16,
      courier: DeliveryCourier.Paquetenvia, courierFolio: 'PQ1', dispatchedAt: '2026-10-13T19:05:00Z', cashToCollect: 275, awaitsCourierCash: false,
    };
    renderOrders(TenantCapability.Food, [
      order({ id: 'd1', orderNumber: 'ORD-000201', status: 1, total: 275, paidAmount: 0, balance: 275, delivery }),
      order({ id: 'd2', orderNumber: 'ORD-000202', status: 6, total: 275, paidAmount: 0, balance: 275, delivery: { ...delivery, awaitsCourierCash: true } }),
    ]);

    const onTheWay = (await screen.findByText('ORD-000201')).closest('tr')!;
    const delivered = screen.getByText('ORD-000202').closest('tr')!;
    expect(within(onTheWay).getByText('En camino')).toBeInTheDocument();
    expect(within(onTheWay).getByText(/Por cobrar/)).toHaveTextContent('$275.00');
    expect(within(delivered).queryByText('En camino')).not.toBeInTheDocument();
    expect(within(delivered).getByText(/Lo debe Paquetenvia/)).toHaveTextContent('$275.00');
  });

  it('reads the orders again when one goes out with the courier', async () => {
    renderOrders(TenantCapability.Food);
    await screen.findByText('ORD-000001');
    const reads = vi.mocked(ordersApi.getAll).mock.calls.length;

    act(() => realtime.updated?.({ orderId: 'o1' }));

    await waitFor(() => expect(vi.mocked(ordersApi.getAll).mock.calls.length).toBeGreaterThan(reads), { timeout: 3000 });
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
