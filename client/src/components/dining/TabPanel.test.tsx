import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
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
  ordersApi: { getById: vi.fn(), cancelTabItem: vi.fn(), markRoundServed: vi.fn() },
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

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <TabPanel orderId="tab1" freeTables={[]} businessName="Fonda Lupita" onClose={vi.fn()} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
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
});
