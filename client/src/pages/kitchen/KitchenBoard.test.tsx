import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { kitchenApi } from '../../api/kitchenApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { OrderStatus, TenantCapability, UnitType, type KitchenOrderDto } from '../../types';
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
});
