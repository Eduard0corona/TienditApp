import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, onTestFinished, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import type { OrderCancelledPayload, OrderCreatedPayload, OrderUpdatedPayload } from '../../hooks/useOrderSignalR';
import { OnlinePaymentDispute, OnlinePaymentRefundReason, OnlinePaymentRefundStatus } from '../../api/generated/enums';
import { useTenant } from '../../hooks/useTenant';
import { DeliveryCourier, OrderChannel, OrderPaymentMethod, OrderType, PlatformCollectedBy, TenantCapability, type OrderDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import OrdersList from './OrdersList';

// The handler the list gives SignalR for new orders, to play an event as the hub would.
const realtime = vi.hoisted(() => ({
  created: undefined as ((payload: OrderCreatedPayload) => void) | undefined,
  updated: undefined as ((payload: OrderUpdatedPayload) => void) | undefined,
  cancelled: undefined as ((payload: OrderCancelledPayload) => void) | undefined,
  play: vi.fn(),
}));

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
// What the person answers in the dialogs of the page (the reason to cancel, a yes or no).
const dialogs = vi.hoisted(() => ({ prompt: vi.fn(), confirm: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => dialogs }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('../../hooks/useArrivalChime', () => ({ useArrivalChime: () => ({ enabled: false, toggle: vi.fn(), play: realtime.play }) }));
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
  const onOrderCancelled = (callback: (payload: OrderCancelledPayload) => void) => {
    realtime.cancelled = callback;
    return () => undefined;
  };
  return {
    useOrderSignalR: () => ({
      onOrderCreated, onOrderStatusChanged: subscribe, onOrderCancelled, onOrderUpdated, onConnected: subscribe,
    }),
  };
});
vi.mock('../../api/ordersApi', () => ({ ordersApi: { getAll: vi.fn(), getById: vi.fn(), cancel: vi.fn(), setPlatformOrderId: vi.fn(), refund: vi.fn() } }));
// The refund's own form has its tests: here it only hands the order its refund.
vi.mock('../../components/orders/RefundOrderModal', () => ({
  default: ({ onSubmit }: { onSubmit: (data: unknown) => void }) => (
    <button type="button" onClick={() => onSubmit({ amount: 100, method: 0, reason: 'No se hizo una parte', items: [] })}>Registrar el reembolso</button>
  ),
}));
// Where the business receives orders (GET /orders/channels, Fase 29Q) comes through the typed client.
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

function giveChannels(channels: OrderChannel[]) {
  vi.mocked(apiRequest).mockImplementation(((_method: string, path: string) =>
    Promise.resolve(path === '/api/v1/orders/channels' ? { channels } : undefined)) as unknown as typeof apiRequest);
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

function renderOrders(capabilities: number, orders: OrderDto[] = [order({})], entry = '/orders') {
  vi.mocked(useTenant).mockReturnValue({ tenant: { name: 'La Esquina' }, capabilities } as unknown as ReturnType<typeof useTenant>);
  vi.mocked(ordersApi.getAll).mockResolvedValue({
    data: { items: orders, pageNumber: 1, totalPages: 1, totalCount: orders.length, hasPreviousPage: false, hasNextPage: false },
  } as Awaited<ReturnType<typeof ordersApi.getAll>>);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[entry]}>
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
    giveChannels([OrderChannel.Counter]);
  });

  it('filters by where the order came from and clears it with the other filters', async () => {
    const user = userEvent.setup();
    giveChannels([OrderChannel.Counter, OrderChannel.WhatsApp]);
    renderOrders(TenantCapability.Food);
    await screen.findByText('ORD-000001');

    await user.selectOptions(await screen.findByRole('combobox', { name: 'Canal' }), 'WhatsApp');
    await waitFor(() => expect(lastParams()).toMatchObject({ channel: OrderChannel.WhatsApp, pageNumber: 1 }));

    await user.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(lastParams()).not.toHaveProperty('channel'));
    expect(screen.getByRole('combobox', { name: 'Canal' })).toHaveValue('');
  });

  it('opens the online orders still to confirm, of any day, from the bell (Fase 29X)', async () => {
    renderOrders(TenantCapability.Food, [order({})], '/orders?estado=por-confirmar');
    await screen.findByText('ORD-000001');

    await waitFor(() => expect(lastParams()).toMatchObject({ status: 0, pageNumber: 1 }));
    expect(lastParams()).not.toHaveProperty('fromDate');
    expect(lastParams()).not.toHaveProperty('toDate');
    expect(screen.getByRole('combobox', { name: 'Estado' })).toHaveValue('0');
  });

  it('says by when an online order still waiting is confirmed in time, and when the half hour passed (Fase 29X)', async () => {
    const soon = new Date(Date.now() + 10 * 60_000).toISOString();
    const past = new Date(Date.now() - 60_000).toISOString();
    renderOrders(TenantCapability.Food, [
      order({ id: 'a', orderNumber: 'ORD-000001', channel: OrderChannel.Online, createdByBusiness: false, confirmBy: soon }),
      order({ id: 'b', orderNumber: 'ORD-000002', channel: OrderChannel.Online, createdByBusiness: false, confirmBy: past }),
      order({ id: 'c', orderNumber: 'ORD-000003', channel: OrderChannel.Online, createdByBusiness: false, confirmBy: null }),
    ]);

    const row = async (number: string) => (await screen.findByText(number)).closest('tr') as HTMLElement;
    expect(within(await row('ORD-000001')).getByText(/^Confírmalo antes de las? .+ para que no cuente entre los más lentos$/)).toHaveClass('text-muted');
    expect(within(await row('ORD-000002')).getByText('Ya pasó la media hora: cuenta entre los más lentos en Explorar')).toHaveClass('text-danger');
    expect(within(await row('ORD-000003')).queryByText(/Confírmalo|media hora/)).not.toBeInTheDocument();
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

  it('offers the channel filter only with more than one channel, with those the API says (Fase 29Q)', async () => {
    const user = userEvent.setup();
    // At the counter only: nothing to filter.
    const page = renderOrders(TenantCapability.Food);
    await screen.findByText('ORD-000001');
    await waitFor(() => expect(apiRequest).toHaveBeenCalledWith('get', '/api/v1/orders/channels', expect.anything()));
    expect(screen.queryByRole('combobox', { name: 'Canal' })).not.toBeInTheDocument();
    page.unmount();

    // WhatsApp and a platform, with no online menu: those, in the order the API gives.
    giveChannels([OrderChannel.Counter, OrderChannel.WhatsApp, OrderChannel.UberEats]);
    renderOrders(TenantCapability.Food);
    const filter = await screen.findByRole('combobox', { name: 'Canal' });
    expect(within(filter).getAllByRole('option').map((option) => option.textContent)).toEqual(['Todos', 'Mostrador', 'WhatsApp', 'Uber Eats']);

    await user.selectOptions(filter, 'Uber Eats');
    await waitFor(() => expect(lastParams()).toMatchObject({ channel: OrderChannel.UberEats, pageNumber: 1 }));
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

  /** An order paid online as the list gives it: what became of its refunds and its dispute only come with its detail. */
  const asListed = (full: OrderDto): OrderDto => ({
    ...full,
    onlineDispute: undefined,
    onlineDisputeUpdatedAt: undefined,
    refunds: full.refunds?.map((refund) => ({ ...refund, onlineStatus: undefined, onlineFailure: undefined, onlineReason: undefined })),
  });

  /** The list shows the order as the list gives it, and its detail asks for the whole order. */
  function renderOnlineOrder(full: OrderDto) {
    vi.mocked(ordersApi.getById).mockResolvedValue({ data: full } as Awaited<ReturnType<typeof ordersApi.getById>>);
    return renderOrders(TenantCapability.Food, [asListed(full)]);
  }

  it('says what became of each refund given back through Mercado Pago, read from the whole order (Fase 24I)', async () => {
    const user = userEvent.setup();
    const refund = (id: string, onlineStatus: number, onlineFailure: string | null = null) => ({
      id, amount: 40, method: OrderPaymentMethod.Online, reason: 'Faltó un refresco', refundedAt: '2026-10-09T17:00:00Z',
      refundedBy: 'Mariana', restockItems: false, lines: [], onlineStatus, onlineFailure,
    });
    renderOnlineOrder(order({
      channel: OrderChannel.Online, createdByBusiness: false, items: [], taxBreakdown: [], statusHistory: [], servicePieces: [],
      allowedTransitions: [], refundedAmount: 120,
      payments: [{ id: 'p1', amount: 120, method: OrderPaymentMethod.Online, paidAt: '2026-10-09T16:00:00Z', reference: '987654321' }],
      refunds: [
        refund('f1', OnlinePaymentRefundStatus.Requested),
        refund('f2', OnlinePaymentRefundStatus.Completed),
        refund('f3', OnlinePaymentRefundStatus.Failed, 'El pago ya no se puede devolver.'),
      ],
    } as Partial<OrderDto>));

    await user.click(await screen.findByRole('button', { name: 'ORD-000001' }));

    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000001' });
    expect(await within(detail).findByText('Mercado Pago lo está devolviendo.')).toHaveClass('text-muted');
    expect(within(detail).getByText('Mercado Pago ya lo devolvió.')).toHaveClass('text-success');
    expect(within(detail).getByText('El pago ya no se puede devolver. Devuélvelo desde tu cuenta de Mercado Pago.')).toHaveClass('text-danger');
    expect(ordersApi.getById).toHaveBeenCalledWith('o1');
  });

  it('says nothing of a refund made online while the whole order has not come, rather than a guess', async () => {
    const user = userEvent.setup();
    vi.mocked(ordersApi.getById).mockReturnValue(new Promise(() => undefined));
    renderOrders(TenantCapability.Food, [asListed(order({
      channel: OrderChannel.Online, createdByBusiness: false, items: [], taxBreakdown: [], statusHistory: [], servicePieces: [],
      allowedTransitions: [], refundedAmount: 40,
      payments: [{ id: 'p1', amount: 120, method: OrderPaymentMethod.Online, paidAt: '2026-10-09T16:00:00Z', reference: '987654321' }],
      refunds: [{
        id: 'f1', amount: 40, method: OrderPaymentMethod.Online, reason: 'Contracargo: el banco del cliente le regresó el pago.',
        refundedAt: '2026-10-09T17:00:00Z', refundedBy: 'Mercado Pago', restockItems: false, lines: [],
      }],
    } as Partial<OrderDto>))]);

    await user.click(await screen.findByRole('button', { name: 'ORD-000001' }));

    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000001' });
    expect(within(detail).getByRole('heading', { name: 'Reembolsos' })).toBeInTheDocument();
    expect(detail).not.toHaveTextContent(/lo está devolviendo|ya lo devolvió/);
  });

  it('warns of a payment in dispute and says where each refund made in Mercado Pago came from (Fase 24K)', async () => {
    const user = userEvent.setup();
    setBusinessTimeZone('America/Mexico_City');
    onTestFinished(() => setBusinessTimeZone(null));
    const refund = (id: string, reason: string, onlineReason: number, onlineStatus: number = OnlinePaymentRefundStatus.Completed) => ({
      id, amount: 40, method: OrderPaymentMethod.Online, reason, refundedAt: '2026-10-09T17:00:00Z', refundedBy: 'Mercado Pago',
      restockItems: false, lines: [], onlineStatus, onlineFailure: null, onlineReason,
    });
    renderOnlineOrder(order({
      channel: OrderChannel.Online, createdByBusiness: false, items: [], taxBreakdown: [], statusHistory: [], servicePieces: [],
      allowedTransitions: [], refundedAmount: 80, onlineDispute: OnlinePaymentDispute.Open, onlineDisputeUpdatedAt: '2026-10-10T17:00:00Z',
      payments: [{ id: 'p1', amount: 160, method: OrderPaymentMethod.Online, paidAt: '2026-10-09T16:00:00Z', reference: '987654321' }],
      refunds: [
        refund('f1', 'Devolución hecha desde la cuenta de Mercado Pago.', OnlinePaymentRefundReason.InMercadoPago),
        refund('f2', 'Faltó un refresco', OnlinePaymentRefundReason.ByBusiness, OnlinePaymentRefundStatus.DoneInMercadoPago),
      ],
    } as Partial<OrderDto>));

    await user.click(await screen.findByRole('button', { name: 'ORD-000001' }));

    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000001' });
    const alert = await within(detail).findByRole('status');
    expect(alert).toHaveTextContent(/^Pago en disputa\. .*Mercado Pago retiene el dinero mientras se resuelve/);
    expect(alert).toHaveClass('alert-warning');
    // 17:00 UTC is 11:00 in Mexico City.
    expect(within(alert).getByText(/^Última noticia de Mercado Pago: 10\/10\/2026.*11:00.*\.$/)).toBeInTheDocument();
    expect(within(detail).getByText('Se hizo en Mercado Pago.')).toHaveClass('text-success');
    expect(within(detail).getByText('Mercado Pago no lo devolvió al pedirlo; después se devolvió en tu cuenta de Mercado Pago.')).toHaveClass('text-success');
  });

  it('says a lost dispute with its refund, and a won one that keeps the refund recorded when it was lost (Fase 24K)', async () => {
    const user = userEvent.setup();
    const claim = {
      id: 'f1', amount: 120, method: OrderPaymentMethod.Online, reason: 'Mercado Pago se lo devolvió al cliente por una reclamación.',
      refundedAt: '2026-10-09T17:00:00Z', refundedBy: 'Mercado Pago', restockItems: false, lines: [],
      onlineStatus: OnlinePaymentRefundStatus.Completed, onlineFailure: null, onlineReason: OnlinePaymentRefundReason.Claim,
    };
    const disputed = (dispute: OnlinePaymentDispute) => order({
      channel: OrderChannel.Online, createdByBusiness: false, items: [], taxBreakdown: [], statusHistory: [], servicePieces: [],
      allowedTransitions: [], refundedAmount: 120, onlineDispute: dispute, refunds: [claim],
      payments: [{ id: 'p1', amount: 120, method: OrderPaymentMethod.Online, paidAt: '2026-10-09T16:00:00Z', reference: '987654321' }],
    } as Partial<OrderDto>);
    const view = renderOnlineOrder(disputed(OnlinePaymentDispute.Lost));

    await user.click(await screen.findByRole('button', { name: 'ORD-000001' }));
    let alert = await within(screen.getByRole('dialog', { name: 'Pedido ORD-000001' })).findByRole('status');
    expect(alert).toHaveClass('alert-danger');
    expect(alert).toHaveTextContent(/^Disputa perdida\. .*Lo ves en los reembolsos del pedido\.$/);
    expect(screen.getByText('Mercado Pago lo devolvió por una reclamación.')).toHaveClass('text-warning');
    view.unmount();

    renderOnlineOrder(disputed(OnlinePaymentDispute.Won));
    await user.click(await screen.findByRole('button', { name: 'ORD-000001' }));
    alert = await within(screen.getByRole('dialog', { name: 'Pedido ORD-000001' })).findByRole('status');
    expect(alert).toHaveClass('alert-success');
    expect(alert).toHaveTextContent(/^Disputa ganada\. Se resolvió a tu favor, pero el reembolso de la disputa sigue anotado en el pedido\./);
  });

  it('shows no dispute for an order paid online with none', async () => {
    const user = userEvent.setup();
    renderOnlineOrder(order({
      channel: OrderChannel.Online, createdByBusiness: false, items: [], taxBreakdown: [], statusHistory: [], servicePieces: [],
      allowedTransitions: [], refunds: [], onlineDispute: null,
      payments: [{ id: 'p1', amount: 120, method: OrderPaymentMethod.Online, paidAt: '2026-10-09T16:00:00Z', reference: '987654321' }],
    } as Partial<OrderDto>));

    await user.click(await screen.findByRole('button', { name: 'ORD-000001' }));

    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000001' });
    await waitFor(() => expect(ordersApi.getById).toHaveBeenCalledWith('o1'));
    expect(detail).not.toHaveTextContent(/disputa/i);
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

  it('opens the accepted quote a service order came from (Fase 29E)', async () => {
    const user = userEvent.setup();
    renderOrders(TenantCapability.Service, [order({
      orderType: OrderType.Service, customerName: 'Valeria Ruiz', total: 4686.4, paidAmount: 464, balance: 4222.4,
      items: [], taxBreakdown: [], statusHistory: [], servicePieces: [], allowedTransitions: [], refunds: [], payments: [],
      quote: { id: 'q-11', number: 'COT-000011', revision: 2 },
    } as Partial<OrderDto>)]);

    await user.click(await screen.findByRole('button', { name: 'ORD-000001' }));
    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000001' });

    expect(within(detail).getByRole('link', { name: 'COT-000011 · versión 2' })).toHaveAttribute('href', '/quotes/q-11');
    expect(within(detail).getByText(/De la cotización/)).toBeInTheDocument();
  });

  it('keeps the quote of a service order after a refund, whose answer does not bring it (Fase 29E)', async () => {
    const user = userEvent.setup();
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', firstName: 'Mariana', role: 'STAFF', permissions: ['ViewOrders', 'ManageOrders', 'ProcessRefunds'] },
    } as unknown as ReturnType<typeof useAuth>);
    const job = order({
      orderType: OrderType.Service, customerName: 'Valeria Ruiz', total: 4686.4, paidAmount: 464, refundedAmount: 0, balance: 4222.4,
      items: [], taxBreakdown: [], statusHistory: [], servicePieces: [], allowedTransitions: [], refunds: [], payments: [],
      quote: { id: 'q-11', number: 'COT-000011', revision: 2 },
    } as Partial<OrderDto>);
    renderOrders(TenantCapability.Service, [job]);
    vi.mocked(ordersApi.refund).mockResolvedValue({ data: { ...job, refundedAmount: 100, quote: null } } as never);

    await user.click(await screen.findByRole('button', { name: 'ORD-000001' }));
    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000001' });
    await user.click(within(detail).getByRole('button', { name: 'Reembolsar' }));
    await user.click(screen.getByRole('button', { name: 'Registrar el reembolso' }));

    await waitFor(() => expect(ordersApi.refund).toHaveBeenCalled());
    expect(await within(screen.getByRole('dialog', { name: 'Pedido ORD-000001' }))
      .findByRole('link', { name: 'COT-000011 · versión 2' })).toHaveAttribute('href', '/quotes/q-11');
  });

  it('shows the appointments of the job of a service order, and keeps them after a refund (Fase 29F)', async () => {
    const user = userEvent.setup();
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', firstName: 'Mariana', role: 'STAFF', permissions: ['ViewOrders', 'ManageOrders', 'ProcessRefunds'] },
    } as unknown as ReturnType<typeof useAuth>);
    const job = order({
      orderType: OrderType.Service, customerName: 'Valeria Ruiz', total: 4686.4, paidAmount: 464, refundedAmount: 0, balance: 4222.4,
      items: [], taxBreakdown: [], statusHistory: [], servicePieces: [], allowedTransitions: [], refunds: [], payments: [],
      quote: { id: 'q-11', number: 'COT-000011', revision: 2 },
      workAppointments: [
        { id: 'w1', startAt: '2026-10-12T16:00:00Z', endAt: '2026-10-12T19:00:00Z', staffName: 'Luis Pérez', status: 1, atCustomersAddress: true },
        { id: 'w2', startAt: '2026-10-13T16:00:00Z', endAt: '2026-10-13T18:00:00Z', staffName: null, status: 99, atCustomersAddress: false },
      ],
    } as Partial<OrderDto>);
    renderOrders(TenantCapability.Service, [job]);
    vi.mocked(ordersApi.refund).mockResolvedValue({ data: { ...job, refundedAmount: 100, quote: null, workAppointments: [] } } as never);

    await user.click(await screen.findByRole('button', { name: 'ORD-000001' }));
    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000001' });
    expect(within(detail).getByRole('heading', { name: 'Citas del trabajo' })).toBeInTheDocument();
    expect(detail).toHaveTextContent('Luis Pérez · a domicilio');
    expect(detail).toHaveTextContent('Sin asignar');
    expect(within(detail).getByText('Cancelada')).toBeInTheDocument();
    await user.click(within(detail).getByRole('button', { name: 'Reembolsar' }));
    await user.click(screen.getByRole('button', { name: 'Registrar el reembolso' }));

    await waitFor(() => expect(ordersApi.refund).toHaveBeenCalled());
    expect(await within(screen.getByRole('dialog', { name: 'Pedido ORD-000001' })).findByText(/Luis Pérez · a domicilio/)).toBeInTheDocument();
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

  it('keeps «Factura» on a cancelled order, where its invoices are downloaded or cancelled', async () => {
    const user = userEvent.setup();
    const cancelled = order({ id: 'c1', orderNumber: 'ORD-000125', status: 99, canCancel: false, items: [], payments: [], refunds: [], statusHistory: [], servicePieces: [], allowedTransitions: [], taxBreakdown: [] });
    renderOrders(TenantCapability.Food, [cancelled]);
    vi.mocked(ordersApi.getById).mockResolvedValue({ data: cancelled } as Awaited<ReturnType<typeof ordersApi.getById>>);

    await user.click(await screen.findByRole('button', { name: 'ORD-000125' }));

    const detail = screen.getByRole('dialog', { name: 'Pedido ORD-000125' });
    expect(within(detail).getByRole('button', { name: 'Factura' })).toBeInTheDocument();
    expect(within(detail).queryByRole('button', { name: 'Cancelar pedido' })).not.toBeInTheDocument();
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

  it('says the customer cancelled an order before it was accepted, ringing down, and reads the orders again (Fase 29R)', async () => {
    renderOrders(TenantCapability.Food);
    await screen.findByText('ORD-000001');
    const reads = vi.mocked(ordersApi.getAll).mock.calls.length;
    realtime.play.mockClear();

    act(() => realtime.cancelled?.({
      orderId: 'o1', orderNumber: 'ORD-000127', previousStatus: 'Pending', reason: 'Lo canceló el cliente.', byCustomer: true,
    }));

    expect(screen.getByRole('status')).toHaveTextContent('El cliente canceló el pedido ORD-000127 antes de que lo aceptaran.');
    expect(realtime.play.mock.calls).toEqual([['leaving']]);
    await waitFor(() => expect(vi.mocked(ordersApi.getAll).mock.calls.length).toBeGreaterThan(reads), { timeout: 3000 });

    // One the business cancelled says its reason, without a sound.
    act(() => realtime.cancelled?.({
      orderId: 'o1', orderNumber: 'ORD-000127', previousStatus: 'Confirmed', reason: 'Sin existencias', byCustomer: false,
    }));
    expect(screen.getByRole('status')).toHaveTextContent('Pedido cancelado: Sin existencias');
    expect(realtime.play).toHaveBeenCalledTimes(1);
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
