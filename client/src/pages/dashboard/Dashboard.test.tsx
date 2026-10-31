import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { dashboardApi } from '../../api/dashboardApi';
import { tenantApi } from '../../api/tenantApi';
import { useAttention } from '../../api/queries';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { TenantCapability, type DashboardMetricsDto, type TopSellingItemDto } from '../../types';
import Dashboard from './Dashboard';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../api/queries', () => ({ useAttention: vi.fn(), queryKeys: { attention: ['attention'] } }));
vi.mock('../../api/dashboardApi', () => ({ dashboardApi: { getMetrics: vi.fn() } }));
vi.mock('../../api/tenantApi', () => ({ tenantApi: { getSetup: vi.fn() } }));
vi.mock('../../hooks/useOrderSignalR', () => {
  const subscribe = () => () => undefined;
  return { useOrderSignalR: () => ({ onOrderCreated: subscribe, onOrderStatusChanged: subscribe, onOrderCancelled: subscribe }) };
});

const item = (productName: string, itemType: TopSellingItemDto['itemType']): TopSellingItemDto => ({
  productId: productName, productName, itemType, quantitySold: 3, totalAmount: 150,
});

function metrics(overrides: Partial<DashboardMetricsDto> = {}): DashboardMetricsDto {
  return {
    rangeFrom: '2026-10-09T06:00:00Z', rangeTo: '2026-10-10T05:59:59Z',
    totalOrders: 8, inProgressOrders: 6, pendingOrders: 1, completedOrders: 0, cancelledOrders: 1,
    uniqueCustomers: 5, activeProducts: 24, activeServices: 0,
    totalRevenue: 1250, averageOrderValue: 156.25, paidOrders: 8, outstandingBalance: 0, unpaidOrders: 0,
    ordersByStatus: { Delivered: 4, InPreparation: 2, Pending: 1, Cancelled: 1 },
    topSellingItems: [item('Café americano', 'Product'), item('Concha de vainilla', 'Product')],
    ...overrides,
  };
}

function renderDashboard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/dashboard']}>
        <Dashboard />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Dashboard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    // A cashier: sells and sees Inicio, without the first steps of whoever configures the business.
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', firstName: 'Mariana', role: 'STAFF', permissions: ['ViewDashboard', 'CreateSales', 'ManageOrders'] },
    } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({ capabilities: TenantCapability.Food | TenantCapability.Retail } as unknown as ReturnType<typeof useTenant>);
    vi.mocked(useAttention).mockReturnValue({ data: { items: [] }, isLoading: false, isError: false, isSuccess: true } as unknown as ReturnType<typeof useAttention>);
    vi.mocked(dashboardApi.getMetrics).mockResolvedValue({ data: metrics() } as Awaited<ReturnType<typeof dashboardApi.getMetrics>>);
  });

  it('asks a trade with visits on to prepare them among its first steps, and counts only the steps it has (Fase 29A)', async () => {
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', firstName: 'Mariana', role: 'TENANT_ADMIN', permissions: ['ViewDashboard', 'ManageSettings'] },
    } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({ capabilities: TenantCapability.Service | TenantCapability.Appointments } as unknown as ReturnType<typeof useTenant>);
    const steps = (keys: string[], done: string[]) => keys.map((key) => ({ key, done: done.includes(key) }));
    const all = ['salesPolicy', 'catalog', 'homeVisits', 'cash', 'firstSale', 'team', 'logo'];
    vi.mocked(tenantApi.getSetup).mockResolvedValue({ data: { steps: steps(all, ['salesPolicy', 'catalog']) } } as never);
    const { unmount } = renderDashboard();

    expect(await screen.findByRole('link', { name: /Prepara tus visitas a domicilio/ })).toHaveAttribute('href', '/settings/business');
    expect(screen.getByText('2 de 7 listos')).toBeInTheDocument();
    unmount();

    // A business without visits: six steps, none about visits.
    vi.mocked(tenantApi.getSetup).mockResolvedValue({ data: { steps: steps(all.filter((key) => key !== 'homeVisits'), ['salesPolicy']) } } as never);
    renderDashboard();
    expect(await screen.findByText('1 de 6 listos')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /visitas a domicilio/ })).not.toBeInTheDocument();
  });

  it('leaves starting a sale to the top bar and the tab bar, without a second "Nueva venta"', async () => {
    renderDashboard();
    await screen.findByText('Café americano');

    expect(screen.queryByRole('link', { name: /Nueva venta/ })).not.toBeInTheDocument();
  });

  it('says how much of "Cobrado" the delivery platforms charged and still have to settle', async () => {
    vi.mocked(dashboardApi.getMetrics).mockResolvedValue({
      data: metrics({ totalRevenue: 1600, platformCollected: 350 }),
    } as Awaited<ReturnType<typeof dashboardApi.getMetrics>>);
    renderDashboard();

    const line = await screen.findByText('Incluye $350.00 que cobraron las plataformas; no entra a la caja.');
    expect(line).toHaveClass('small', 'text-muted');
    // One line more under "Cobrado", which keeps its meaning: everything charged, the platforms included.
    expect(line.closest('.card-body')).toHaveTextContent('Cobrado$1,600.008 pedidos pagados');
  });

  it('leaves that line out without platform money in the period', async () => {
    vi.mocked(dashboardApi.getMetrics).mockResolvedValue({
      data: metrics({ platformCollected: 0 }),
    } as Awaited<ReturnType<typeof dashboardApi.getMetrics>>);
    renderDashboard();
    await screen.findByText('Café americano');

    expect(screen.queryByText(/que cobraron las plataformas/)).not.toBeInTheDocument();
    expect(screen.queryByText(/pagado en línea/)).not.toBeInTheDocument();
  });

  it('says how much of "Cobrado" was paid online into the business\'s Mercado Pago account (Fase 24I)', async () => {
    vi.mocked(dashboardApi.getMetrics).mockResolvedValue({
      data: metrics({ totalRevenue: 1600, onlineCollected: 290 }),
    } as Awaited<ReturnType<typeof dashboardApi.getMetrics>>);
    renderDashboard();

    expect(await screen.findByText('Incluye $290.00 pagado en línea; está en tu cuenta de Mercado Pago.')).toHaveClass('small', 'text-muted');
  });

  it('counts the orders of the day in singular when there is one', async () => {
    renderDashboard();

    expect(await screen.findByText('6 en curso · 1 nuevo · 1 cancelado')).toBeInTheDocument();
  });

  it('keeps the catalog line without the list of orders by status the cards above already sum up', async () => {
    renderDashboard();
    await screen.findByText('Café americano');

    expect(screen.queryByRole('heading', { name: 'Pedidos por estado' })).not.toBeInTheDocument();
    expect(screen.queryByText('En preparación')).not.toBeInTheDocument();
    expect(screen.getByText(/Activos en catálogo: 24 productos y 0 servicios/)).toBeInTheDocument();
  });

  it('hides the type column when every best seller is of the same type', async () => {
    renderDashboard();
    await screen.findByText('Café americano');

    expect(screen.getByRole('columnheader', { name: 'Producto' })).toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'Tipo' })).not.toBeInTheDocument();
    expect(screen.queryByRole('cell', { name: 'Producto' })).not.toBeInTheDocument();
  });

  it('names the type of each best seller when products and services mix', async () => {
    vi.mocked(dashboardApi.getMetrics).mockResolvedValue({
      data: metrics({ topSellingItems: [item('Corte de cabello', 'Service'), item('Shampoo', 'Product')] }),
    } as Awaited<ReturnType<typeof dashboardApi.getMetrics>>);
    renderDashboard();
    await screen.findByText('Corte de cabello');

    expect(screen.getByRole('columnheader', { name: 'Tipo' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Servicio' })).toBeInTheDocument();
  });
});
