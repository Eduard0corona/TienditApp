import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { CourierOrder, CourierReceivable, CourierSettlement, CourierSettlementDetail } from '../../api/courierSettlements';
import { apiRequest } from '../../api/typedClient';
import { CourierSettlementAdjustmentKind, DeliveryCourier, OrderPaymentMethod, OrderStatus } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import CourierCash from './CourierCash';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

type Options = { body?: unknown; query?: Record<string, unknown>; path?: Record<string, string> };

const owedOrder = (orderId: string, orderNumber: string, balance: number, folio: string): CourierOrder => ({
  orderId, orderNumber, customerName: 'Ana', courierFolio: folio, dispatchedAt: '2026-10-13T19:05:00Z',
  status: OrderStatus.Delivered, cashToCollect: balance, paysWith: null, balance,
});

const RECEIVABLE: CourierReceivable = {
  courier: DeliveryCourier.Paquetenvia, owedOrders: 2, owedAmount: 550, oldestDispatchedAt: '2026-10-13T19:05:00Z',
  onTheWayOrders: 1, onTheWayAmount: 100, lastSettledAt: '2026-10-12T23:00:00Z',
};
const OWED = [owedOrder('o1', 'ORD-000201', 275, 'PQ1'), owedOrder('o2', 'ORD-000202', 275, 'PQ2')];
// It went out to collect $120.00 and $20.00 was paid by transfer meanwhile: it collects $100.00.
const ON_THE_WAY: CourierOrder = { ...owedOrder('o3', 'ORD-000203', 120, 'PQ3'), status: OrderStatus.Confirmed, paysWith: 200, balance: 100 };
const SETTLEMENT: CourierSettlement = {
  id: 's1', courier: DeliveryCourier.Paquetenvia, settledAt: '2026-10-12T23:00:00Z', settledBy: 'Mariana', method: OrderPaymentMethod.Cash,
  handedAmount: 485, ordersTotal: 530, adjustmentsTotal: -45, orderCount: 2, reference: null, notes: null,
};
const DETAIL: CourierSettlementDetail = {
  settlement: SETTLEMENT,
  orders: [{ orderId: 'o9', orderNumber: 'ORD-000190', customerName: 'Luis', courierFolio: 'PQ0', amount: 530 }],
  adjustments: [{ position: 1, kind: CourierSettlementAdjustmentKind.TripFee, description: null, amount: -45 }],
};

function serve({ ownDrawer = true, settle }: { ownDrawer?: boolean; settle?: (body: unknown) => Promise<unknown> } = {}) {
  vi.mocked(apiRequest).mockImplementation(((method: string, path: string, options: Options = {}) => {
    if (method === 'get' && path === '/api/v1/courier-settlements/receivable') return Promise.resolve([RECEIVABLE]);
    if (method === 'get' && path === '/api/v1/courier-settlements/pending-orders') {
      return Promise.resolve({ owed: OWED, onTheWay: [ON_THE_WAY], truncated: false });
    }
    if (method === 'get' && path === '/api/v1/courier-settlements') {
      return Promise.resolve({ items: [SETTLEMENT], pageNumber: 1, totalPages: 1, totalCount: 1, hasPreviousPage: false, hasNextPage: false });
    }
    if (method === 'get' && path === '/api/v1/courier-settlements/{id}') return Promise.resolve(DETAIL);
    if (method === 'get' && path === '/api/v1/cash/current-session') return Promise.resolve({ id: 'c1', isMine: ownDrawer, status: 0 });
    if (method === 'post' && path === '/api/v1/courier-settlements') return settle ? settle(options.body) : Promise.reject(new Error('sin liquidar'));
    return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as unknown as typeof apiRequest);
}

function renderCash(canSettle = true, onOpenDrawer = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <CourierCash canSettle={canSettle} onOpenDrawer={onOpenDrawer} />
    </QueryClientProvider>,
  );
  return onOpenDrawer;
}

const posts = () => vi.mocked(apiRequest).mock.calls.filter(([method]) => method === 'post');

describe('CourierCash', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    setBusinessTimeZone('America/Mexico_City');
  });

  it('tells what Paquetenvia owes, what it carries and its settlements', async () => {
    serve();
    renderCash();

    const card = (await screen.findByRole('heading', { name: 'Paquetenvia' })).closest('section')!;
    expect(within(card).getByText('Te debe').nextElementSibling).toHaveTextContent('$550.00');
    expect(within(card).getByText(/2 pedidos entregados/)).toBeInTheDocument();
    expect(within(card).getByText('En camino').nextElementSibling).toHaveTextContent('$100.00');
    expect(screen.getByText(/ORD-000203/).closest('li')).toHaveTextContent('Cobra $100.00 · paga con $200.00 · cambio $100.00');
    const owed = screen.getByRole('heading', { name: 'Pedidos que debe' }).closest('section')!;
    expect(within(owed).getAllByRole('row')).toHaveLength(3);
    expect(within(owed).getByText('PQ2')).toBeInTheDocument();

    await userEvent.click(await screen.findByRole('button', { name: /^Ver/ }));
    const detail = await screen.findByRole('dialog');
    expect(within(detail).getByText('ORD-000190')).toBeInTheDocument();
    expect(within(detail).getByText(/Viaje/)).toHaveTextContent('Viaje · −$45.00');
  });

  it('asks to open one\'s own drawer before settling, and leaves it to whoever charges orders', async () => {
    serve({ ownDrawer: false });
    const onOpenDrawer = renderCash();

    await userEvent.click(await screen.findByRole('button', { name: 'Ir a Mi caja' }));
    expect(onOpenDrawer).toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Liquidar a Paquetenvia' })).not.toBeInTheDocument();
  });

  it('says who settles when the person only handles the cash', async () => {
    serve();
    renderCash(false);

    expect(await screen.findByText('Liquida quien cobra pedidos y maneja la caja.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Liquidar a Paquetenvia' })).not.toBeInTheDocument();
  });

  it('settles what the courier handed over once it adds up, with its trip and a shortage', async () => {
    serve({ settle: () => Promise.resolve({ ...DETAIL, settlement: { ...SETTLEMENT, id: 's2', handedAmount: 225 } }) });
    renderCash();

    await userEvent.click(await screen.findByRole('button', { name: 'Liquidar a Paquetenvia' }));
    const dialog = screen.getByRole('dialog');
    // It hands over one order: the other stays owed.
    await userEvent.click(within(dialog).getByLabelText(/ORD-000202/));
    await userEvent.type(within(dialog).getByLabelText('Entregó'), '225');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Agregar ajuste' }));
    await userEvent.selectOptions(within(dialog).getByLabelText('Motivo del ajuste 1'), String(CourierSettlementAdjustmentKind.TripFee));
    await userEvent.type(within(dialog).getByLabelText('Importe del ajuste 1'), '45');

    // $275 − $45 = $230: it handed $5 less, which it does not save until explained.
    const cuadre = within(dialog).getByLabelText('Cuadre');
    expect(within(cuadre).getByText('Debe entregar').nextElementSibling).toHaveTextContent('$230.00');
    expect(within(cuadre).getByText('Diferencia').nextElementSibling).toHaveTextContent('−$5.00');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar liquidación' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Entregó $225.00 y los pedidos con sus ajustes dan $230.00');
    expect(posts()).toHaveLength(0);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Registrar faltante de $5.00' }));
    expect(within(cuadre).getByText('Diferencia').nextElementSibling).toHaveTextContent('$0.00');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Transferencia' }));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar liquidación' }));

    await waitFor(() => expect(posts()).toHaveLength(1));
    expect((posts()[0][2] as Options).body).toEqual({
      courier: DeliveryCourier.Paquetenvia,
      method: OrderPaymentMethod.Transfer,
      handedAmount: 225,
      orderIds: ['o1'],
      ordersTotal: 275,
      adjustments: [
        { kind: CourierSettlementAdjustmentKind.TripFee, description: null, amount: -45 },
        { kind: CourierSettlementAdjustmentKind.Shortage, description: null, amount: -5 },
      ],
      reference: null,
      notes: null,
    });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(toast.success).toHaveBeenCalledWith(expect.stringContaining('Liquidación guardada'));
  });

  it('shows what the server answers when an order is no longer owed', async () => {
    serve({
      settle: () => Promise.reject({
        isAxiosError: true,
        response: { status: 409, data: { detail: 'Un pedido de la lista ya no se le debe al repartidor (alguien lo cobró, lo liquidó o cambió): lee otra vez la lista.' } },
      }),
    });
    renderCash();

    await userEvent.click(await screen.findByRole('button', { name: 'Liquidar a Paquetenvia' }));
    const dialog = screen.getByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText('Entregó'), '550');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar liquidación' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('ya no se le debe al repartidor');
  });
});
