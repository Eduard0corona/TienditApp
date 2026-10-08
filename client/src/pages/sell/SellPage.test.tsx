import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { categoriesApi } from '../../api/categoriesApi';
import { cashApi } from '../../api/cashApi';
import { ordersApi } from '../../api/ordersApi';
import { productsApi } from '../../api/productsApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import {
  BusinessType,
  CfdiTaxObject,
  KitchenTicketStatus,
  OrderType,
  TenantCapability,
  UnitType,
  type OrderDto,
  type ProductDto,
} from '../../types';
import { ROUND_STOCK_WARNING_TITLE } from '../../utils/stockWarnings';
import SellPage from './SellPage';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../hooks/useOnlineStatus', () => ({ useOnlineStatus: () => true }));
vi.mock('../../hooks/usePendingSales', () => ({ usePendingSales: () => ({ sales: [], failedCount: 0 }) }));
vi.mock('../../hooks/useWorkingBranch', () => ({
  useWorkingBranch: () => ({ hasSeveral: false, branches: [], branch: undefined, setBranch: vi.fn() }),
}));
vi.mock('../../offline/catalogCache', () => ({ loadCatalogSnapshot: vi.fn(), saveCatalogSnapshot: vi.fn() }));
vi.mock('../../api/productsApi', () => ({ productsApi: { getAll: vi.fn() } }));
vi.mock('../../api/categoriesApi', () => ({ categoriesApi: { getAll: vi.fn() } }));
vi.mock('../../api/cashApi', () => ({ cashApi: { getCurrentSession: vi.fn() } }));
vi.mock('../../api/chatApi', () => ({ chatApi: { sendMessage: vi.fn() } }));
vi.mock('../../api/ordersApi', () => ({
  ordersApi: { create: vi.fn(), addRound: vi.fn(), getById: vi.fn(), uploadPaymentEvidence: vi.fn() },
}));

const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };

beforeAll(() => {
  // jsdom has no matchMedia, and the ticket sheet (Offcanvas) asks it for the screen size.
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList,
  });
});

function product(overrides: Partial<ProductDto>): ProductDto {
  return {
    id: 'taco',
    categoryId: 'c1',
    name: 'Taco',
    sortOrder: 1,
    priceAmount: 25,
    priceCurrency: 'MXN',
    isTaxExempt: false,
    hasDiscount: false,
    taxObject: CfdiTaxObject.Subject,
    isAvailable: true,
    isFeatured: false,
    tracksStock: false,
    isLowStock: false,
    isTrackable: false,
    notifyCustomer: false,
    unit: UnitType.Unit,
    ...overrides,
  };
}

const taco = product({});
const steak = product({ id: 'steak', name: 'Arrachera', priceAmount: 389.9, unit: UnitType.Kg, sortOrder: 2 });
const latte = product({
  id: 'latte',
  name: 'Latte',
  priceAmount: 55,
  sortOrder: 3,
  optionGroups: [{
    id: 'size',
    name: 'Tamaño',
    minSelections: 1,
    maxSelections: 1,
    options: [
      { id: 'small', name: 'Chico', priceDelta: -5, isAvailable: true },
      { id: 'large', name: 'Grande', priceDelta: 12, isAvailable: true },
    ],
  }],
});

function signIn(permissions: string[], role = 'Employee') {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role, permissions } } as unknown as ReturnType<typeof useAuth>);
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Fonda Lupita', businessType: BusinessType.Food, pricesIncludeTax: true, defaultTaxRate: 16 },
    capabilities: TenantCapability.Food | TenantCapability.Kitchen,
    isLoading: false,
    refreshTenant: vi.fn(),
  } as unknown as ReturnType<typeof useTenant>);
}

function renderAt(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/sell" element={<SellPage />} />
          <Route path="/tables" element={<p>Mesas</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

/** The ticket on a wide screen (on a phone the same panel opens in a sheet). */
function ticket() {
  return within(screen.getByRole('complementary', { name: 'Ticket de la venta' }));
}

describe('SellPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(productsApi.getAll).mockResolvedValue({ data: { items: [taco, steak, latte], totalCount: 3 } } as never);
    vi.mocked(categoriesApi.getAll).mockResolvedValue({ data: [{ id: 'c1', name: 'Comida', sortOrder: 1 }] } as never);
    vi.mocked(cashApi.getCurrentSession).mockResolvedValue({ data: { isMine: true } } as never);
  });

  it('adds what is tapped and saves the sale for later with the server checking the total', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    vi.mocked(ordersApi.create).mockResolvedValue({ data: { orderNumber: 'ORD-000001', stockWarnings: [] } } as never);
    renderAt('/sell');

    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));

    expect(ticket().getByRole('textbox', { name: 'Cantidad de Taco' })).toHaveValue('2');
    expect(ticket().getByRole('button', { name: /Cobrar \$50\.00/ })).toBeEnabled();

    await userEvent.click(ticket().getByRole('button', { name: 'Guardar sin cobrar' }));

    expect(ordersApi.create).toHaveBeenCalledWith(expect.objectContaining({
      orderType: OrderType.Retail,
      items: [{ productId: 'taco', quantity: 2, notes: undefined, optionIds: undefined }],
      expectedTotal: 50,
      payments: undefined,
    }));
    expect(toast.success).toHaveBeenCalledWith('Pedido ORD-000001 guardado. Cóbralo desde Pedidos.');
    expect(ticket().getByText('Toca un producto para agregarlo.')).toBeInTheDocument();
  });

  it('asks for the variant of a product with options, and the weight of what is weighed', async () => {
    signIn(['ManageOrders', 'ChargeOrders']);
    renderAt('/sell');

    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Latte/ }));
    const options = screen.getByRole('dialog');
    await userEvent.click(within(options).getByLabelText(/Grande/));
    await userEvent.click(within(options).getByRole('button', { name: 'Agregar · $67.00' }));
    expect(ticket().getByText('Grande')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /^Agregar Arrachera/ }));
    const weight = screen.getByRole('dialog');
    await userEvent.type(within(weight).getByLabelText(/¿Cuánto\?/), '0.5');
    expect(within(weight).getByText('Importe: $194.95')).toBeInTheDocument();
    await userEvent.click(within(weight).getByRole('button', { name: 'Agregar' }));

    expect(ticket().getByText('0.5 kg × $389.90/kg')).toBeInTheDocument();
    expect(ticket().getByRole('button', { name: /Cobrar \$261\.95/ })).toBeInTheDocument();
  });

  it('sends a round of a table to the kitchen instead of charging it, with the stock warning', async () => {
    signIn(['ManageOrders']);
    vi.mocked(ordersApi.getById).mockResolvedValue({
      data: { id: 'tab1', total: 120, kitchenTickets: [{ id: 'k1', round: 1, status: KitchenTicketStatus.Ready, sentAt: '2026-10-08T20:00:00Z' }] },
    } as never);
    vi.mocked(ordersApi.addRound).mockResolvedValue({ data: { id: 'tab1', stockWarnings: ['Tortilla quedó en -2'] } as OrderDto } as never);
    renderAt('/sell?tab=tab1&label=Mesa%202');

    expect(await ticket().findByText(/ronda 2 · llevan/)).toBeInTheDocument();
    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    await userEvent.click(ticket().getByRole('button', { name: 'Enviar a cocina · $25.00' }));

    expect(ordersApi.addRound).toHaveBeenCalledWith('tab1', [{ productId: 'taco', quantity: 1, notes: undefined, optionIds: undefined }]);
    expect(toast.warning).toHaveBeenCalledWith(expect.stringContaining('Tortilla quedó en -2'), { title: ROUND_STOCK_WARNING_TITLE });
    expect(toast.success).toHaveBeenCalledWith('Ronda enviada a cocina.');
    expect(await screen.findByText('Mesas')).toBeInTheDocument();
  });

  it('leaves the charge to the cashier when the person only takes orders', async () => {
    signIn(['ManageOrders']);
    vi.mocked(ordersApi.create).mockResolvedValue({ data: { orderNumber: 'ORD-000002', stockWarnings: [] } } as never);
    renderAt('/sell');

    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));

    expect(ticket().queryByRole('button', { name: /Cobrar/ })).not.toBeInTheDocument();
    await userEvent.click(ticket().getByRole('button', { name: 'Guardar pedido · $25.00' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Pedido ORD-000002 guardado: la caja lo cobra desde Pedidos.'));
  });
});
