import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { KitchenTicketStatus, TenantCapability, UnitType, type OrderDto } from '../../types';
import TabPanel from './TabPanel';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../api/ordersApi', () => ({
  ordersApi: {
    getById: vi.fn(), cancelTabItem: vi.fn(), markRoundServed: vi.fn(), closeTab: vi.fn(), moveTab: vi.fn(), addPayment: vi.fn(),
    uploadPaymentEvidence: vi.fn(),
  },
}));

const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };

function tab(overrides: Partial<OrderDto> = {}): OrderDto {
  return {
    id: 'tab1',
    orderNumber: 'ORD-000012',
    tableNumber: 'Mesa 4',
    customerName: 'Público en general',
    createdByBusiness: true,
    isTab: true,
    isOpenTab: true,
    total: 75,
    paidAmount: 0,
    balance: 75,
    items: [{
      id: 'i1', productId: 'p1', productName: 'Tacos al pastor', quantity: 3, unitPrice: 25, totalPrice: 75, chargedPrice: 75,
      taxRate: 16, isTaxExempt: false, unit: UnitType.Unit, round: 1,
    }],
    kitchenTickets: [{ id: 'k1', round: 1, status: KitchenTicketStatus.Pending, sentAt: '2026-10-08T20:00:00Z' }],
    itemCancellations: [],
    ...overrides,
  } as OrderDto;
}

/** The tables of Mesas behind the tab: read once, and read again after a change. */
function TablesOnScreen({ read }: { read: () => Promise<unknown> }) {
  useQuery({ queryKey: ['tables'], queryFn: read });
  return null;
}

function renderPanel(onClose = vi.fn(), tables?: () => Promise<unknown>) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        {tables && <TablesOnScreen read={tables} />}
        <TabPanel orderId="tab1" freeTables={[]} businessName="Fonda Lupita" onClose={onClose} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return onClose;
}

describe('TabPanel', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'Employee', permissions: ['ManageOrders'] } } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({ capabilities: TenantCapability.Food | TenantCapability.Kitchen } as unknown as ReturnType<typeof useTenant>);
  });

  it('shows where each round is and cancels a dish with its reason, back to stock while the kitchen has not started', async () => {
    const cancelled = tab({
      total: 50,
      balance: 50,
      items: [{ ...tab().items[0], cancelledQuantity: 1, chargedPrice: 50 }],
      itemCancellations: [{
        id: 'c1', orderItemId: 'i1', round: 1, quantity: 1, amount: 25, reason: 'El cliente ya no lo quiso',
        returnedToStock: true, cancelledAt: '2026-10-08T20:10:00Z', cancelledBy: 'Ana',
      }],
    });
    // The panel reloads the tab after the change: the server then answers with the dish cancelled.
    vi.mocked(ordersApi.getById).mockResolvedValueOnce({ data: tab() } as never).mockResolvedValue({ data: cancelled } as never);
    vi.mocked(ordersApi.cancelTabItem).mockResolvedValue({ data: cancelled } as never);
    renderPanel();

    expect(await screen.findByText('En cocina')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar Tacos al pastor' }));
    await userEvent.click(screen.getByRole('button', { name: 'El cliente ya no lo quiso' }));
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar platillo' }));

    expect(ordersApi.cancelTabItem).toHaveBeenCalledWith('tab1', {
      orderItemId: 'i1', quantity: 1, reason: 'El cliente ya no lo quiso', returnToStock: true,
    });
    expect(toast.success).toHaveBeenCalledWith('Platillo cancelado. La cuenta quedó en $50.00.');
    const line = (await screen.findByText(/1 cancelado/)).closest('li') as HTMLElement;
    expect(within(line).getByText(/El cliente ya no lo quiso/)).toBeInTheDocument();
    expect(within(line).getByText('$50.00')).toBeInTheDocument();
  });

  it('crosses out a dish cancelled in full and offers nothing more to cancel on it', async () => {
    vi.mocked(ordersApi.getById).mockResolvedValue({
      data: tab({
        total: 0,
        balance: 0,
        items: [{ ...tab().items[0], cancelledQuantity: 3, chargedPrice: 0 }],
        itemCancellations: [{
          id: 'c1', orderItemId: 'i1', round: 1, quantity: 3, amount: 75, reason: 'Se capturó por error',
          returnedToStock: true, cancelledAt: '2026-10-08T20:10:00Z', cancelledBy: 'Ana',
        }],
      }),
    } as never);
    renderPanel();

    expect(await screen.findByText(/Cancelado: Se capturó por error/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar Tacos al pastor' })).not.toBeInTheDocument();
  });

  it('names a round being cooked like the kitchen and the orders: "En preparación"', async () => {
    vi.mocked(ordersApi.getById).mockResolvedValue({
      data: tab({ kitchenTickets: [{ id: 'k1', round: 1, status: KitchenTicketStatus.Preparing, sentAt: '2026-10-08T20:00:00Z' }] }),
    } as never);
    renderPanel();

    expect(await screen.findByText('En preparación')).toBeInTheDocument();
    expect(screen.queryByText('Preparando')).not.toBeInTheDocument();
  });
  it('stays open while a round is marked served, and waits for the tab before offering it again', async () => {
    const user = userEvent.setup();
    // A waiter who also charges: «Cobrar» is on the tab.
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', role: 'Employee', permissions: ['ManageOrders', 'ChargeOrders'] },
    } as unknown as ReturnType<typeof useAuth>);
    const ready = tab({ kitchenTickets: [{ id: 'k1', round: 1, status: KitchenTicketStatus.Ready, sentAt: '2026-10-08T20:00:00Z' }] });
    const served = tab({ kitchenTickets: [{ id: 'k1', round: 1, status: KitchenTicketStatus.Served, sentAt: '2026-10-08T20:00:00Z' }] });
    let reread: (value: unknown) => void = () => undefined;
    vi.mocked(ordersApi.getById)
      .mockResolvedValueOnce({ data: ready } as never)
      .mockReturnValueOnce(new Promise((resolve) => { reread = resolve; }) as never);
    let marked: (value: unknown) => void = () => undefined;
    vi.mocked(ordersApi.markRoundServed).mockReturnValue(new Promise((resolve) => { marked = resolve; }) as never);
    const onClose = renderPanel();

    await user.click(await screen.findByRole('button', { name: 'Servida' }));
    expect(screen.getByRole('button', { name: 'Servida' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    // Nothing else leaves the tab or starts another change meanwhile.
    expect(screen.getByRole('button', { name: 'Agregar ronda' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Ver cuenta' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cobrar $75.00' })).toBeDisabled();
    await user.keyboard('{Escape}');
    expect(onClose).not.toHaveBeenCalled();

    marked({});
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Ronda 1 servida.'));
    // The answer brings no tab: the button waits for the tab read again, which says the round was served.
    await waitFor(() => expect(ordersApi.getById).toHaveBeenCalledTimes(2));
    expect(screen.getByRole('button', { name: 'Servida' })).toBeDisabled();
    reread({ data: served });
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Servida' })).not.toBeInTheDocument());
    expect(ordersApi.markRoundServed).toHaveBeenCalledTimes(1);
  });

  it('closes a paid tab with the answer, before the tab and the tables are read again', async () => {
    const user = userEvent.setup();
    const paid = tab({ paidAmount: 75, balance: 0 });
    let reads = 0;
    vi.mocked(ordersApi.getById).mockImplementation(() => (++reads === 1 ? Promise.resolve({ data: paid }) : new Promise(() => undefined)) as never);
    vi.mocked(ordersApi.closeTab).mockResolvedValue({ data: { ...paid, isOpenTab: false } } as never);
    const tables = vi.fn().mockResolvedValueOnce({ tables: [] }).mockReturnValue(new Promise(() => undefined));
    const onClose = renderPanel(vi.fn(), tables);

    await user.click(await screen.findByRole('button', { name: 'Cerrar cuenta' }));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(toast.success).toHaveBeenCalledWith('Cuenta cerrada. La mesa quedó libre.');
    expect(tables).toHaveBeenCalledTimes(2);
    // The tab is read again behind it, too.
    expect(reads).toBe(2);
  });

  it('reads the tab again when what it sent fails, and says why', async () => {
    const user = userEvent.setup();
    vi.mocked(ordersApi.getById).mockResolvedValue({ data: tab() } as never);
    vi.mocked(ordersApi.cancelTabItem).mockImplementation(() => Promise.reject(new Error('sin red')));
    renderPanel();

    await user.click(await screen.findByRole('button', { name: 'Cancelar Tacos al pastor' }));
    await user.click(screen.getByRole('button', { name: 'El cliente ya no lo quiso' }));
    await user.click(screen.getByRole('button', { name: 'Cancelar platillo' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo completar la acción.');
    await waitFor(() => expect(ordersApi.getById).toHaveBeenCalledTimes(2));
  });

  it('says why the tab could not be read', async () => {
    vi.mocked(ordersApi.getById).mockRejectedValue({ isAxiosError: true, response: { status: 404, data: { error: 'La cuenta ya se cerró.' } } });
    renderPanel();

    expect(await screen.findByRole('alert')).toHaveTextContent('La cuenta ya se cerró.');
  });
  it('says once, next to the charge, why a payment could not be registered', async () => {
    const user = userEvent.setup();
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', role: 'Employee', permissions: ['ManageOrders', 'ChargeOrders'] },
    } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(ordersApi.getById).mockResolvedValue({ data: tab() } as never);
    vi.mocked(ordersApi.addPayment).mockRejectedValue({ isAxiosError: true, response: { status: 400, data: { error: 'La caja está cerrada.' } } });
    renderPanel();

    await user.click(await screen.findByRole('button', { name: 'Cobrar $75.00' }));
    await user.click(screen.getByRole('radio', { name: /Transferencia/ }));
    await user.click(screen.getByRole('button', { name: /^Registrar/ }));

    expect(await screen.findByRole('alert')).toHaveTextContent('La caja está cerrada.');
    expect(screen.getAllByText('La caja está cerrada.')).toHaveLength(1);
  });

  it('moves the tab, and waits for the tables read again before offering another move', async () => {
    const user = userEvent.setup();
    const atFour = tab({ diningTableId: 'm4' });
    vi.mocked(ordersApi.getById).mockResolvedValue({ data: atFour } as never);
    let moved: (value: unknown) => void = () => undefined;
    vi.mocked(ordersApi.moveTab).mockReturnValue(new Promise((resolve) => { moved = resolve; }) as never);
    let reread: (value: unknown) => void = () => undefined;
    const tables = vi.fn().mockResolvedValueOnce({ tables: [] }).mockReturnValueOnce(new Promise((resolve) => { reread = resolve; }));
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <MemoryRouter>
          <TablesOnScreen read={tables} />
          <TabPanel orderId="tab1" freeTables={[{ id: 'm5', name: 'Mesa 5' } as never]} businessName="Fonda Lupita" onClose={vi.fn()} />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'Cambiar de mesa' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Cambiar de mesa' }));
    await user.click(screen.getByRole('button', { name: 'Mesa 5' }));
    // While it moves, it does not go back to the tab as it was.
    expect(screen.getByRole('button', { name: 'Volver' })).toBeDisabled();
    moved({ data: { ...atFour, diningTableId: 'm5', tableNumber: 'Mesa 5' } });

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('La cuenta pasó a Mesa 5.'));
    // The free tables still say Mesa 5 is free: the move waits for them.
    expect(screen.getByRole('button', { name: 'Cambiar de mesa' })).toBeDisabled();
    reread({ tables: [] });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Cambiar de mesa' })).toBeEnabled());
  });
});
