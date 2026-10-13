import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { kitchenApi } from '../../api/kitchenApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { KitchenTicketStatus, OrderChannel, OrderStatus, TenantCapability, UnitType, type KitchenOrderDto } from '../../types';
import KitchenBoard from './KitchenBoard';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useOrderSignalR', () => {
  const subscribe = () => () => undefined;
  return {
    useOrderSignalR: () => ({
      onOrderCreated: subscribe, onOrderStatusChanged: subscribe, onOrderCancelled: subscribe,
      onKitchenTicketsChanged: subscribe, onConnected: subscribe,
    }),
  };
});
vi.mock('../../api/kitchenApi', () => ({
  kitchenApi: { getOrders: vi.fn(), getTickets: vi.fn(), startPreparation: vi.fn(), markReady: vi.fn() },
}));

function order(id: string, orderNumber: string, status: OrderStatus): KitchenOrderDto {
  return {
    id, orderNumber, status, customerName: 'Público en general', createdAt: '2026-10-09T15:00:00Z',
    items: [{
      id: `${id}-1`, productId: 'p1', productName: 'Chilaquiles', quantity: 1, unitPrice: 95, totalPrice: 95, chargedPrice: 95,
      taxRate: 16, isTaxExempt: false, unit: UnitType.Unit,
    }],
  } as KitchenOrderDto;
}

const card = (orderNumber: string) => screen.getByRole('heading', { name: orderNumber }).closest('.card') as HTMLElement;

function renderBoard() {
  return render(<MemoryRouter initialEntries={['/kitchen']}><KitchenBoard /></MemoryRouter>);
}

describe('KitchenBoard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'Employee', permissions: ['ManageKitchen'] } } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({ capabilities: TenantCapability.Food | TenantCapability.Kitchen } as unknown as ReturnType<typeof useTenant>);
    vi.mocked(kitchenApi.getOrders).mockResolvedValue({
      data: [order('o1', 'ORD-000101', OrderStatus.Pending), order('o2', 'ORD-000102', OrderStatus.InPreparation)],
    } as Awaited<ReturnType<typeof kitchenApi.getOrders>>);
    vi.mocked(kitchenApi.getTickets).mockResolvedValue({ data: [] } as unknown as Awaited<ReturnType<typeof kitchenApi.getTickets>>);
    vi.mocked(kitchenApi.startPreparation).mockResolvedValue({} as Awaited<ReturnType<typeof kitchenApi.startPreparation>>);
    vi.mocked(kitchenApi.markReady).mockResolvedValue({} as Awaited<ReturnType<typeof kitchenApi.markReady>>);
  });

  it('offers only the next step of each order: start a new one, finish one in preparation', async () => {
    renderBoard();
    await screen.findByRole('heading', { name: 'ORD-000101' });

    const fresh = within(card('ORD-000101'));
    expect(fresh.getAllByRole('button').map((button) => button.textContent)).toEqual(['Iniciar']);

    // The chip above already says it is being prepared; a disabled "Preparando" button would only repeat it.
    const cooking = within(card('ORD-000102'));
    expect(cooking.getByText('En preparación')).toBeInTheDocument();
    expect(cooking.getAllByRole('button').map((button) => button.textContent)).toEqual(['Lista']);
  });

  it('moves each order one step with its button', async () => {
    const user = userEvent.setup();
    renderBoard();
    await screen.findByRole('heading', { name: 'ORD-000101' });

    await user.click(within(card('ORD-000101')).getByRole('button', { name: 'Iniciar' }));
    expect(kitchenApi.startPreparation).toHaveBeenCalledWith('o1');

    await user.click(within(card('ORD-000102')).getByRole('button', { name: 'Lista' }));
    expect(kitchenApi.markReady).toHaveBeenCalledWith('o2');
  });

  it('names a round being cooked like an order: "En preparación"', async () => {
    vi.mocked(kitchenApi.getTickets).mockResolvedValue({
      data: [{
        id: 't1', orderId: 'o9', orderNumber: 'ORD-000109', customerName: 'Público en general', tableNumber: 'Mesa 4',
        round: 2, status: KitchenTicketStatus.Preparing, sentAt: '2026-10-09T15:00:00Z',
        items: order('o9', 'ORD-000109', OrderStatus.InPreparation).items,
      }],
    } as unknown as Awaited<ReturnType<typeof kitchenApi.getTickets>>);
    renderBoard();

    await screen.findByRole('heading', { name: 'Mesa 4' });
    expect(within(card('Mesa 4')).getByText('En preparación')).toBeInTheDocument();
    expect(screen.queryByText('Preparando')).not.toBeInTheDocument();
  });

  it('marks an order of a platform for its bag: the platform, the folio of its app in large and the name for the bag', async () => {
    vi.mocked(kitchenApi.getOrders).mockResolvedValue({
      data: [
        { ...order('r1', 'ORD-000123', OrderStatus.Confirmed), channel: OrderChannel.Rappi, externalOrderNumber: 'BC953', customerName: 'Juan' },
        { ...order('u1', 'ORD-000124', OrderStatus.Confirmed), channel: OrderChannel.UberEats, externalOrderNumber: 'X7K2P' },
        { ...order('c1', 'ORD-000125', OrderStatus.Confirmed), channel: OrderChannel.Counter, externalOrderNumber: null },
      ],
    } as Awaited<ReturnType<typeof kitchenApi.getOrders>>);
    renderBoard();

    const rappi = within(await screen.findByRole('heading', { name: 'BC953' }).then((heading) => heading.closest('.card') as HTMLElement));
    expect(rappi.getByText('Rappi')).toHaveClass('badge', 'text-bg-light', 'border');
    expect(rappi.getByText('ORD-000123')).toHaveClass('text-muted');
    expect(rappi.getByText('Bolsa:').parentElement).toHaveTextContent('Bolsa: Juan');
    expect(rappi.queryByText('Cliente:')).not.toBeInTheDocument();
    expect(rappi.getAllByRole('button').map((button) => button.textContent)).toEqual(['Iniciar']);

    // The general public has no name worth writing on the bag.
    const uber = within(card('X7K2P'));
    expect(uber.getByText('Uber Eats')).toBeInTheDocument();
    expect(uber.queryByText('Bolsa:')).not.toBeInTheDocument();
    expect(uber.queryByText('Cliente:')).not.toBeInTheDocument();

    // Any other order reads as always.
    const counter = within(card('ORD-000125'));
    expect(counter.getByText('Cliente:')).toBeInTheDocument();
    expect(counter.queryByText('Rappi')).not.toBeInTheDocument();
    expect(counter.queryByText('A domicilio')).not.toBeInTheDocument();
  });

  it('marks an order that goes to the customer\'s address, without saying where', async () => {
    vi.mocked(kitchenApi.getOrders).mockResolvedValue({
      data: [{ ...order('d1', 'ORD-000126', OrderStatus.Confirmed), channel: OrderChannel.Online, isDelivery: true, customerName: 'Ana' }],
    } as Awaited<ReturnType<typeof kitchenApi.getOrders>>);
    renderBoard();

    await screen.findByRole('heading', { name: 'ORD-000126' });
    const delivery = within(card('ORD-000126'));
    expect(delivery.getByText('A domicilio')).toHaveClass('badge');
    expect(delivery.getByText('Cliente:').parentElement).toHaveTextContent('Cliente: Ana');
  });
});
