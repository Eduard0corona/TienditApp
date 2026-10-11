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
import { enqueueSale } from '../../offline/saleQueue';
import {
  BusinessType,
  CfdiTaxObject,
  KitchenTicketStatus,
  OrderChannel,
  OrderPaymentMethod,
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
// jsdom has no IndexedDB: the queue of sales charged offline is replaced, the rest of the module is the real one.
vi.mock('../../offline/saleQueue', async (importOriginal) => ({
  ...await importOriginal<typeof import('../../offline/saleQueue')>(),
  enqueueSale: vi.fn(),
}));
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

/** Cobrar from the ticket, paid exactly in cash. */
async function chargeInCash(total: string) {
  await userEvent.click(ticket().getByRole('button', { name: `Cobrar ${total} F9` }));
  await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: `Cobrar ${total}` }));
}

/** The idempotency key of each sale sent, in order. */
function sentKeys() {
  return vi.mocked(ordersApi.create).mock.calls.map(([request]) => request.idempotencyKey);
}

/** The answer of the server: the sale recorded with a taco paid in cash. */
const chargedTaco: OrderDto = {
  id: 'o3',
  createdByBusiness: true,
  orderNumber: 'ORD-000003',
  businessType: BusinessType.Food,
  orderType: OrderType.Retail,
  status: 2 as OrderDto['status'],
  canCancel: false,
  allowedTransitions: [],
  customerName: 'Público en general',
  pricesIncludeTax: true,
  subTotal: 21.55,
  tax: 3.45,
  total: 25,
  taxBreakdown: [],
  paidAmount: 25,
  balance: 0,
  createdAt: '2026-10-10T18:00:00Z',
  items: [{
    id: 'i1',
    productId: 'taco',
    productName: 'Taco',
    quantity: 1,
    unitPrice: 25,
    totalPrice: 25,
    taxRate: 16,
    isTaxExempt: false,
    unit: UnitType.Unit,
  }],
  statusHistory: [],
  servicePieces: [],
  payments: [{ id: 'p1', amount: 25, method: OrderPaymentMethod.Cash, paidAt: '2026-10-10T18:00:00Z', cashReceived: 25, change: 0 }],
  stockWarnings: [],
};

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
    // The toasts come once the round is back from the API, not with the click.
    await waitFor(() => expect(toast.warning).toHaveBeenCalledWith(expect.stringContaining('Tortilla quedó en -2'), { title: ROUND_STOCK_WARNING_TITLE }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Ronda enviada a cocina.'));
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

  it('charges with the same key after the page reloads while the charge is on its way', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    // The server never answers before the reload: it may have recorded the sale or not.
    vi.mocked(ordersApi.create).mockReturnValueOnce(new Promise(() => {}) as never);
    const page = renderAt('/sell');
    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    await chargeInCash('$25.00');
    expect(ordersApi.create).toHaveBeenCalledTimes(1);

    page.unmount();
    vi.mocked(ordersApi.create).mockResolvedValueOnce({ data: chargedTaco } as never);
    renderAt('/sell');
    expect(await ticket().findByRole('textbox', { name: 'Cantidad de Taco' })).toHaveValue('1');
    await chargeInCash('$25.00');

    // With the same key the server answers with the sale it already had instead of recording another one.
    expect(await screen.findByRole('heading', { name: 'Venta ORD-000003 cobrada' })).toBeInTheDocument();
    const [sent, retried] = sentKeys();
    expect(sent).toEqual(expect.any(String));
    expect(retried).toBe(sent);
  });

  it('sends a new key when the cart that came back after the reload changes: it is another sale', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    vi.mocked(ordersApi.create).mockReturnValue(new Promise(() => {}) as never);
    const page = renderAt('/sell');
    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    await chargeInCash('$25.00');

    page.unmount();
    renderAt('/sell');
    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    await chargeInCash('$50.00');

    const [sent, other] = sentKeys();
    expect(other).toEqual(expect.any(String));
    expect(other).not.toBe(sent);
  });

  it('retries a refused charge with its key and gives the cart a new one once it changes', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    vi.mocked(ordersApi.create).mockRejectedValue(new Error('refused'));
    renderAt('/sell');
    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    await chargeInCash('$25.00');

    const charge = screen.getByRole('dialog');
    expect(await within(charge).findByText('No se pudo registrar la venta.')).toBeInTheDocument();
    await userEvent.click(within(charge).getByRole('button', { name: 'Cobrar $25.00' }));
    await waitFor(() => expect(ordersApi.create).toHaveBeenCalledTimes(2));
    await userEvent.click(within(charge).getByRole('button', { name: 'Volver' }));
    await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));
    await chargeInCash('$50.00');

    await waitFor(() => expect(ordersApi.create).toHaveBeenCalledTimes(3));
    const [sent, retried, changed] = sentKeys();
    expect(retried).toBe(sent);
    expect(changed).not.toBe(sent);
  });

  it('gives the next sale a key of its own once the server recorded the previous one, even with the same products', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    vi.mocked(ordersApi.create).mockResolvedValue({ data: { orderNumber: 'ORD-000004', stockWarnings: [] } } as never);
    const page = renderAt('/sell');
    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    await userEvent.click(ticket().getByRole('button', { name: 'Guardar sin cobrar' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledTimes(1));

    page.unmount();
    renderAt('/sell');
    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    expect(ticket().getByRole('textbox', { name: 'Cantidad de Taco' })).toHaveValue('1');
    await userEvent.click(ticket().getByRole('button', { name: 'Guardar sin cobrar' }));

    await waitFor(() => expect(ordersApi.create).toHaveBeenCalledTimes(2));
    const [sent, next] = sentKeys();
    expect(next).not.toBe(sent);
  });

  it('fills in the customer from the customer card as a counter sale that can be charged offline', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    vi.mocked(enqueueSale).mockImplementation(async (sale) => ({ ...sale, folio: 'SC-0001', status: 'pending', attempts: 0 }));
    renderAt('/sell?phone=5512345678&name=Ana');

    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    expect(ticket().getByLabelText('Nombre')).toHaveValue('Ana');
    expect(ticket().getByLabelText('Teléfono')).toHaveValue('5512345678');
    expect(ticket().queryByRole('button', { name: 'Ya no es un pedido por WhatsApp' })).not.toBeInTheDocument();

    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    await userEvent.click(ticket().getByRole('button', { name: /Cobrar \$25\.00/ }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cobrar $25.00' }));

    expect(ordersApi.create).not.toHaveBeenCalled();
    const queued = vi.mocked(enqueueSale).mock.calls[0][0].request;
    expect(queued).toMatchObject({ customerName: 'Ana', customerPhone: '5512345678', payments: [expect.objectContaining({ amount: 25 })] });
    // Without a channel the API records a counter sale.
    expect(queued.channel).toBeUndefined();
    expect(await screen.findByText('Venta SC-0001 guardada sin conexión')).toBeInTheDocument();
  });

  it('records a sale started from a WhatsApp conversation as a WhatsApp order that can be saved unpaid', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash', 'ManageWhatsApp']);
    vi.mocked(ordersApi.create).mockResolvedValue({
      data: {
        orderNumber: 'ORD-000003',
        createdAt: '2026-10-10T18:00:00Z',
        customerName: 'Ana',
        customerPhone: '5512345678',
        items: [],
        taxBreakdown: [],
        payments: [],
        pricesIncludeTax: true,
        subTotal: 21.55,
        total: 25,
        balance: 25,
        stockWarnings: [],
      },
    } as never);
    renderAt('/sell?phone=5512345678&name=Ana&conversation=c1');

    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    expect(ticket().getByRole('button', { name: 'Ya no es un pedido por WhatsApp' })).toBeInTheDocument();
    expect(ticket().getByLabelText('Teléfono')).toHaveValue('5512345678');

    await userEvent.click(ticket().getByRole('button', { name: 'Guardar sin cobrar' }));

    expect(ordersApi.create).toHaveBeenCalledWith(expect.objectContaining({
      customerName: 'Ana',
      customerPhone: '5512345678',
      channel: OrderChannel.WhatsApp,
      payments: undefined,
    }));
    // The summary goes back to the chat, paid or not.
    expect(await screen.findByText('Pedido ORD-000003 guardado')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Volver al chat' })).toHaveAttribute('href', '/whatsapp/chat?conversation=c1');
    expect(toast.success).not.toHaveBeenCalled();
  });
});
