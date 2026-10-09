import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { OrderChannel, OrderType, TenantCapability, type OrderDto } from '../../types';
import OrdersList from './OrdersList';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ prompt: vi.fn(), confirm: vi.fn() }) }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('../../hooks/useArrivalChime', () => ({ useArrivalChime: () => ({ enabled: false, toggle: vi.fn(), play: vi.fn() }) }));
vi.mock('../../hooks/useOrderSignalR', () => {
  const subscribe = () => () => undefined;
  return {
    useOrderSignalR: () => ({
      onOrderCreated: subscribe, onOrderStatusChanged: subscribe, onOrderCancelled: subscribe, onConnected: subscribe,
    }),
  };
});
vi.mock('../../api/ordersApi', () => ({ ordersApi: { getAll: vi.fn(), getById: vi.fn() } }));

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
});
