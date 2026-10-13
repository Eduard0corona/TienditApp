import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider, onlineManager } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { categoriesApi } from '../../api/categoriesApi';
import { cashApi } from '../../api/cashApi';
import { ordersApi } from '../../api/ordersApi';
import { productsApi } from '../../api/productsApi';
import { apiRequest } from '../../api/typedClient';
import ConfirmProvider from '../../components/feedback/ConfirmProvider';
import { useAuth } from '../../hooks/useAuth';
import { useOnlineStatus } from '../../hooks/useOnlineStatus';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { loadCatalogSnapshot, saveCatalogSnapshot, type CatalogSnapshot } from '../../offline/catalogCache';
import { enqueueSale } from '../../offline/saleQueue';
import {
  BusinessType,
  CfdiTaxObject,
  KitchenTicketStatus,
  OrderChannel,
  OrderPaymentMethod,
  OrderType,
  PlatformCollectedBy,
  TenantCapability,
  UnitType,
  type CategoryDto,
  type OrderDto,
  type ProductDto,
} from '../../types';
import { ROUND_STOCK_WARNING_TITLE } from '../../utils/stockWarnings';
import SellPage from './SellPage';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../hooks/useOnlineStatus', () => ({ useOnlineStatus: vi.fn() }));
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
// The delivery platforms of the business (GET /delivery-platforms/channels) come through the typed client.
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

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
      <ConfirmProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/sell" element={<SellPage />} />
            <Route path="/tables" element={<p>Mesas</p>} />
          </Routes>
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  );
}

/** The delivery platforms the business set up, as GET /delivery-platforms/channels answers them. */
function givePlatforms(rows: Array<{ channel: OrderChannel; isActive: boolean }>) {
  vi.mocked(apiRequest).mockImplementation(((_method: string, path: string) =>
    Promise.resolve(path === '/api/v1/delivery-platforms/channels' ? rows : undefined)) as unknown as typeof apiRequest);
}

/** The delivery zones of the business too (GET /delivery-zones), besides its platforms. */
function giveZones(zones: Array<{ name: string; fee: number; minimumOrder: number; freeDeliveryFrom?: number | null; postalCodes: string[]; isActive?: boolean }>) {
  vi.mocked(apiRequest).mockImplementation(((_method: string, path: string) => Promise.resolve(
    path === '/api/v1/delivery-zones'
      ? zones.map((zone, index) => ({ id: `z${index}`, revision: 1, isActive: true, freeDeliveryFrom: null, ...zone }))
      : path === '/api/v1/delivery-platforms/channels' ? [] : undefined,
  )) as unknown as typeof apiRequest);
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
    vi.mocked(useOnlineStatus).mockReturnValue(true);
    givePlatforms([]);
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
    const options = screen.getByRole('dialog', { name: 'Latte' });
    await userEvent.click(within(options).getByLabelText(/Grande/));
    await userEvent.click(within(options).getByRole('button', { name: 'Agregar · $67.00' }));
    expect(ticket().getByText('Grande')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /^Agregar Arrachera/ }));
    const weight = screen.getByRole('dialog', { name: 'Arrachera' });
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

  it('names the fields of the phone sheet with its own labels, not with those of the ticket hidden beside the catalog', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    renderAt('/sell');
    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));

    await userEvent.click(screen.getByRole('button', { name: /^1 artículo/ }));
    const sheet = within(await screen.findByRole('dialog', { name: 'Ticket' }));
    await userEvent.click(sheet.getByRole('button', { name: 'Cliente, mesa y notas (opcional)' }));

    for (const name of ['Nombre', 'Teléfono', 'Mesa o referencia', 'Notas del pedido']) {
      expect(sheet.getByRole('textbox', { name })).toBeInTheDocument();
    }
    expect(sheet.getByRole('textbox', { name: 'Teléfono' })).toHaveAccessibleDescription('Con teléfono la venta queda en su historial.');
    // The ticket is in the page twice: a repeated id would send a label or a description to the hidden copy.
    const ids = Array.from(document.querySelectorAll('[id]'), (element) => element.id);
    expect(new Set(ids).size).toBe(ids.length);
  });
});

/** Where the order on screen came from, if the business has a platform active. */
function originPicker() {
  return ticket().getByRole('radiogroup', { name: '¿De dónde viene el pedido?' });
}

/** The server's answer to an order of a platform: the folio and who charged it, never its commission. */
function platformOrder(channel: OrderChannel, folio: string, collectedBy: PlatformCollectedBy, orderNumber: string): OrderDto {
  const paidByPlatform = collectedBy === PlatformCollectedBy.Platform;
  return {
    ...chargedTaco,
    id: orderNumber.toLowerCase(),
    orderNumber,
    channel,
    platformOrder: { externalOrderNumber: folio, collectedBy },
    payments: paidByPlatform
      ? [{ id: 'p1', amount: 25, method: OrderPaymentMethod.Platform, paidAt: '2026-10-10T18:00:00Z', reference: folio }]
      : [],
    paidAmount: paidByPlatform ? 25 : 0,
    balance: paidByPlatform ? 0 : 25,
  };
}

function duplicateFolio(): AxiosError {
  const headers = new AxiosHeaders();
  const data = { code: 'possible_duplicate_order', detail: 'Hoy ya registraste un pedido de esta plataforma con el mismo folio.' };
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
    status: 409, data, statusText: '', headers, config: { headers },
  } as AxiosResponse);
}

const corte = product({ id: 'corte', name: 'Corte de cabello', priceAmount: 150, unit: UnitType.Service, sortOrder: 4 });

describe('SellPage with delivery platforms', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(productsApi.getAll).mockResolvedValue({ data: { items: [taco, steak, latte], totalCount: 3 } } as never);
    vi.mocked(categoriesApi.getAll).mockResolvedValue({ data: [{ id: 'c1', name: 'Comida', sortOrder: 1 }] } as never);
    vi.mocked(cashApi.getCurrentSession).mockResolvedValue({ data: { isMine: true } } as never);
    vi.mocked(useOnlineStatus).mockReturnValue(true);
    givePlatforms([
      { channel: OrderChannel.Rappi, isActive: true },
      { channel: OrderChannel.UberEats, isActive: false },
      { channel: OrderChannel.DidiFood, isActive: true },
    ]);
  });

  it('offers only the platforms the business has active, with the counter chosen', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    renderAt('/sell');

    const radios = within(await waitFor(originPicker)).getAllByRole('radio');
    expect(radios.map((radio) => radio.textContent)).toEqual(['Mostrador', 'Rappi', 'DiDi Food']);
    expect(radios[0]).toHaveAttribute('aria-checked', 'true');
    expect(ticket().queryByLabelText('Folio de Rappi')).not.toBeInTheDocument();
  });

  it('offers no platform from a chat (a WhatsApp order) or for a table', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash', 'ManageWhatsApp']);
    // A closed drawer words its notice by the platforms once they are known: the moment to look for the picker.
    vi.mocked(cashApi.getCurrentSession).mockResolvedValue({ data: null } as never);
    const platformsKnown = 'Tu caja está cerrada: ábrela para cobrar. Sin ella puedes guardar pedidos y registrar los que ya cobró una plataforma.';

    const chat = renderAt('/sell?phone=5512345678&name=Ana&conversation=c1');
    expect(await screen.findByText(platformsKnown)).toBeInTheDocument();
    expect(ticket().getByRole('button', { name: 'Ya no es un pedido por WhatsApp' })).toBeInTheDocument();
    expect(ticket().queryByRole('radiogroup', { name: '¿De dónde viene el pedido?' })).not.toBeInTheDocument();
    chat.unmount();

    renderAt('/sell?table=t1&label=Mesa%202');
    expect(await screen.findByText(platformsKnown)).toBeInTheDocument();
    expect(ticket().getByText('Mesa 2')).toBeInTheDocument();
    expect(ticket().queryByRole('radiogroup', { name: '¿De dónde viene el pedido?' })).not.toBeInTheDocument();
  });

  it('registers an order Rappi charged without charging it, without a drawer or the charge permission', async () => {
    signIn(['ManageOrders', 'ManageCash']);
    vi.mocked(cashApi.getCurrentSession).mockResolvedValue({ data: null } as never);
    vi.mocked(ordersApi.create).mockResolvedValue({ data: platformOrder(OrderChannel.Rappi, 'BC953', PlatformCollectedBy.Platform, 'ORD-000005') } as never);
    renderAt('/sell');

    await userEvent.click(within(await waitFor(originPicker)).getByRole('radio', { name: 'Rappi' }));
    const folio = ticket().getByLabelText('Folio de Rappi');
    // Chosen the platform, the folio is the next thing to type, with the keyboard of Rappi's numbers.
    expect(folio).toHaveFocus();
    expect(folio).toHaveAttribute('inputmode', 'numeric');
    expect(ticket().getByText('El número de pedido que ves en Rappi Aliados.')).toBeInTheDocument();
    // Its app shares neither the phone nor a table: only notes for the kitchen.
    await userEvent.click(ticket().getByRole('button', { name: 'Notas para cocina (opcional)' }));
    expect(ticket().getByLabelText('Notas para cocina')).toBeInTheDocument();
    expect(ticket().queryByLabelText('Teléfono')).not.toBeInTheDocument();
    expect(ticket().queryByLabelText('Mesa o referencia')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));
    expect(ticket().getByText('Rappi ya lo cobró: no entra a tu caja.')).toBeInTheDocument();
    await userEvent.click(ticket().getByRole('button', { name: 'Registrar pedido de Rappi · $25.00 F9' }));

    // The folio is required, and nothing is sent without it.
    expect(ticket().getAllByText('Escribe el folio de Rappi.').length).toBeGreaterThan(0);
    expect(ordersApi.create).not.toHaveBeenCalled();

    await userEvent.type(folio, '#bc 953');
    await userEvent.type(ticket().getByLabelText('Nombre para la bolsa (opcional)'), 'Juan');
    await userEvent.click(ticket().getByRole('button', { name: 'Registrar pedido de Rappi · $25.00 F9' }));

    expect(ordersApi.create).toHaveBeenCalledWith(expect.objectContaining({
      channel: OrderChannel.Rappi,
      externalOrderNumber: 'BC953',
      collectedBy: PlatformCollectedBy.Platform,
      customerName: 'Juan',
      customerPhone: undefined,
      tableNumber: undefined,
      payments: undefined,
      idempotencyKey: expect.any(String),
    }));
    // Neither the charge nor the drawer opened: the platform charged it in its app.
    expect(screen.queryByRole('dialog', { name: 'Cobrar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: /caja/i })).not.toBeInTheDocument();

    const done = await screen.findByRole('dialog', { name: 'Pedido ORD-000005 de Rappi registrado' });
    expect(within(done).getByRole('button', { name: 'Imprimir para la bolsa' })).toBeInTheDocument();
    expect(within(done).queryByRole('link', { name: 'Enviar por WhatsApp' })).not.toBeInTheDocument();
    const bagTicket = within(done).getByLabelText('Ticket ORD-000005');
    expect(bagTicket).toHaveTextContent('Rappi · Folio BC953');
    expect(bagTicket).toHaveTextContent('Pagado en la app de Rappi');
    expect(bagTicket).not.toHaveTextContent('$25.00');

    // Every sale starts at the counter again.
    await userEvent.click(within(done).getByRole('button', { name: 'Nueva venta' }));
    expect(within(originPicker()).getByRole('radio', { name: 'Mostrador' })).toHaveAttribute('aria-checked', 'true');
    expect(ticket().queryByLabelText('Folio de Rappi')).not.toBeInTheDocument();
  });

  it('charges now an order the courier pays, without a tip, and registers it for the cashier otherwise', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    vi.mocked(ordersApi.create).mockResolvedValue({
      data: { ...platformOrder(OrderChannel.DidiFood, '12', PlatformCollectedBy.Business, 'ORD-000006'), balance: 0, paidAmount: 25 },
    } as never);
    renderAt('/sell');

    await userEvent.click(within(await waitFor(originPicker)).getByRole('radio', { name: 'DiDi Food' }));
    await userEvent.type(ticket().getByLabelText('Folio de DiDi Food'), '12');
    await userEvent.click(ticket().getByRole('radio', { name: 'El negocio (el repartidor nos paga)' }));
    expect(ticket().getByText('El repartidor te paga al recoger: cóbralo en la caja como siempre.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));

    // Registering stays the main action; charging now is the other way out.
    expect(ticket().getByRole('button', { name: 'Registrar pedido de DiDi Food · $25.00 F9' })).toHaveClass('btn-primary');
    await userEvent.click(ticket().getByRole('button', { name: 'Cobrar ahora' }));
    const charge = await screen.findByRole('dialog', { name: 'Cobrar' });
    // The tip of an order of a platform is in its app, for the courier.
    expect(within(charge).queryByRole('radiogroup', { name: 'Propina' })).not.toBeInTheDocument();
    await userEvent.click(within(charge).getByRole('button', { name: 'Cobrar $25.00' }));

    expect(ordersApi.create).toHaveBeenCalledWith(expect.objectContaining({
      channel: OrderChannel.DidiFood,
      externalOrderNumber: '12',
      collectedBy: PlatformCollectedBy.Business,
      payments: [expect.objectContaining({ amount: 25, method: OrderPaymentMethod.Cash, tip: undefined })],
    }));
    expect(await screen.findByRole('dialog', { name: 'Pedido ORD-000006 de DiDi Food cobrado' })).toBeInTheDocument();
  });

  it('does not register the order with F9 while it is being charged now, or while the drawer is being opened', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    renderAt('/sell');

    await userEvent.click(within(await waitFor(originPicker)).getByRole('radio', { name: 'DiDi Food' }));
    await userEvent.type(ticket().getByLabelText('Folio de DiDi Food'), '12');
    await userEvent.click(ticket().getByRole('radio', { name: 'El negocio (el repartidor nos paga)' }));
    await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));
    await userEvent.click(ticket().getByRole('button', { name: 'Cobrar ahora' }));
    const charge = await screen.findByRole('dialog', { name: 'Cobrar' });

    // The courier's cash is being counted: F9 (charge, at the counter) must not register it unpaid behind the dialog.
    await userEvent.keyboard('{F9}');
    expect(ordersApi.create).not.toHaveBeenCalled();
    expect(charge).toBeInTheDocument();
  });

  it('does not register the order with F9 behind the dialog to open the drawer', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    vi.mocked(cashApi.getCurrentSession).mockResolvedValue({ data: null } as never);
    renderAt('/sell');

    await userEvent.click(within(await waitFor(originPicker)).getByRole('radio', { name: 'DiDi Food' }));
    await userEvent.type(ticket().getByLabelText('Folio de DiDi Food'), '12');
    await userEvent.click(ticket().getByRole('radio', { name: 'El negocio (el repartidor nos paga)' }));
    await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));
    await userEvent.click(ticket().getByRole('button', { name: 'Cobrar ahora' }));
    await screen.findByRole('dialog', { name: /caja/i });

    await userEvent.keyboard('{F9}');
    expect(ordersApi.create).not.toHaveBeenCalled();
  });

  it('leaves an order the courier pays to the cashier when the person cannot charge', async () => {
    signIn(['ManageOrders']);
    vi.mocked(ordersApi.create).mockResolvedValue({ data: platformOrder(OrderChannel.DidiFood, '12', PlatformCollectedBy.Business, 'ORD-000007') } as never);
    renderAt('/sell');

    await userEvent.click(within(await waitFor(originPicker)).getByRole('radio', { name: 'DiDi Food' }));
    await userEvent.type(ticket().getByLabelText('Folio de DiDi Food'), '12');
    await userEvent.click(ticket().getByRole('radio', { name: 'El negocio (el repartidor nos paga)' }));
    await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));

    expect(ticket().queryByRole('button', { name: 'Cobrar ahora' })).not.toBeInTheDocument();
    expect(ticket().getByText('La caja lo cobra desde Pedidos cuando el repartidor pague.')).toBeInTheDocument();
    await userEvent.keyboard('{F9}');

    // Charging at the drawer is the cashier's: nothing tells this person to do it.
    expect(ticket().queryByText('El repartidor te paga al recoger: cóbralo en la caja como siempre.')).not.toBeInTheDocument();
    expect(ordersApi.create).toHaveBeenCalledWith(expect.objectContaining({ collectedBy: PlatformCollectedBy.Business, payments: undefined }));
    const done = await screen.findByRole('dialog', { name: 'Pedido ORD-000007 de DiDi Food registrado' });
    expect(within(done).getByText('La caja lo cobra desde Pedidos cuando el repartidor pague.')).toBeInTheDocument();
    expect(within(done).queryByText('Cóbralo en Pedidos cuando el repartidor te pague.')).not.toBeInTheDocument();
  });

  it('waits for the connection to register an order of a platform: it is never queued on the device', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    vi.mocked(useOnlineStatus).mockReturnValue(false);
    renderAt('/sell');

    await userEvent.click(within(await waitFor(originPicker)).getByRole('radio', { name: 'Rappi' }));
    await userEvent.type(ticket().getByLabelText('Folio de Rappi'), '152390');
    await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));

    expect(ticket().getByText(
      'Sin conexión no se pueden registrar pedidos de plataforma ni se guardan solos: déjalo en pantalla y regístralo cuando vuelva la conexión.',
    )).toBeInTheDocument();
    expect(ticket().getByRole('button', { name: 'Registrar pedido de Rappi · $25.00 F9' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Registrar' })).toBeDisabled();
    await userEvent.keyboard('{F9}');

    expect(ordersApi.create).not.toHaveBeenCalled();
    expect(enqueueSale).not.toHaveBeenCalled();
  });

  it('asks before registering a folio registered today, and sends it again with the same key once confirmed', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    vi.mocked(ordersApi.create)
      .mockRejectedValueOnce(duplicateFolio())
      .mockRejectedValueOnce(duplicateFolio())
      .mockResolvedValueOnce({ data: platformOrder(OrderChannel.Rappi, 'BC953', PlatformCollectedBy.Platform, 'ORD-000008') } as never);
    renderAt('/sell');

    await userEvent.click(within(await waitFor(originPicker)).getByRole('radio', { name: 'Rappi' }));
    await userEvent.type(ticket().getByLabelText('Folio de Rappi'), 'bc953');
    await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));
    await userEvent.click(ticket().getByRole('button', { name: 'Registrar pedido de Rappi · $25.00 F9' }));

    const ask = await screen.findByRole('dialog', { name: '¿Es otro pedido?' });
    expect(ask).toHaveTextContent(
      'Hoy ya registraste un pedido de Rappi con el folio BC953. Los folios cortos se repiten: si es otro pedido, regístralo; si no, búscalo en Pedidos.',
    );
    // To check first: nothing is recorded and the order stays on screen.
    await userEvent.click(within(ask).getByRole('button', { name: 'Revisar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: '¿Es otro pedido?' })).not.toBeInTheDocument());
    expect(ordersApi.create).toHaveBeenCalledTimes(1);
    expect(ticket().getByRole('textbox', { name: 'Cantidad de Taco' })).toHaveValue('1');

    await userEvent.click(ticket().getByRole('button', { name: 'Registrar pedido de Rappi · $25.00 F9' }));
    await userEvent.click(within(await screen.findByRole('dialog', { name: '¿Es otro pedido?' })).getByRole('button', { name: 'Registrar de todos modos' }));

    expect(await screen.findByRole('dialog', { name: 'Pedido ORD-000008 de Rappi registrado' })).toBeInTheDocument();
    const [first, second, confirmed] = vi.mocked(ordersApi.create).mock.calls.map(([request]) => request);
    expect(first.confirmPossibleDuplicate).toBeUndefined();
    expect(second.idempotencyKey).toBe(first.idempotencyKey);
    expect(confirmed).toEqual({ ...second, confirmPossibleDuplicate: true });
  });

  it('charges an order of a platform at the prices of its app, and a counter sale at the menu\'s', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    // Rappi charges $32 for the taco; the menu, $25.
    vi.mocked(productsApi.getAll).mockImplementation((async (params?: { priceChannel?: OrderChannel }) => ({
      data: { items: params?.priceChannel === OrderChannel.Rappi ? [{ ...taco, priceAmount: 32 }, steak, latte] : [taco, steak, latte], totalCount: 3 },
    })) as never);
    vi.mocked(ordersApi.create).mockResolvedValue({ data: platformOrder(OrderChannel.Rappi, 'BC953', PlatformCollectedBy.Platform, 'ORD-000005') } as never);
    renderAt('/sell');

    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    expect(ticket().getByRole('button', { name: 'Cobrar $25.00 F9' })).toBeEnabled();

    await userEvent.click(within(originPicker()).getByRole('radio', { name: 'Rappi' }));
    expect(productsApi.getAll).toHaveBeenLastCalledWith(expect.objectContaining({ priceChannel: OrderChannel.Rappi }));
    // The ticket takes the app's prices with the catalog of the platform.
    const register = await ticket().findByRole('button', { name: 'Registrar pedido de Rappi · $32.00 F9' });
    expect(ticket().getByText('Con tus precios de Rappi')).toBeInTheDocument();
    await userEvent.type(ticket().getByLabelText('Folio de Rappi'), 'BC953');
    await userEvent.click(register);

    expect(ordersApi.create).toHaveBeenCalledWith(expect.objectContaining({ channel: OrderChannel.Rappi, expectedTotal: 32 }));
    await userEvent.click(within(await screen.findByRole('dialog', { name: 'Pedido ORD-000005 de Rappi registrado' })).getByRole('button', { name: 'Nueva venta' }));

    // Back at the counter, the menu's prices.
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith(expect.objectContaining({ priceChannel: undefined })));
    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    expect(await ticket().findByRole('button', { name: 'Cobrar $25.00 F9' })).toBeEnabled();
    expect(ticket().queryByText(/Con tus precios de/)).not.toBeInTheDocument();
  });

  it('adds what a dialog chose at the app\'s prices that came in while it was open', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    let answerRappi: (answer: unknown) => void = () => undefined;
    // Rappi charges $66 for the latte and $15 more for a large one; the menu, $55 and $12.
    const rappiLatte = {
      ...latte,
      priceAmount: 66,
      optionGroups: latte.optionGroups!.map((group) => ({
        ...group,
        options: group.options.map((option) => (option.id === 'large' ? { ...option, priceDelta: 15 } : option)),
      })),
    };
    vi.mocked(productsApi.getAll).mockImplementation(((params?: { priceChannel?: OrderChannel }) => (params?.priceChannel === OrderChannel.Rappi
      ? new Promise((resolve) => { answerRappi = resolve; })
      : Promise.resolve({ data: { items: [taco, steak, latte], totalCount: 3 } }))) as never);
    renderAt('/sell');

    await userEvent.click(within(await waitFor(originPicker)).getByRole('radio', { name: 'Rappi' }));
    // The latte's dialog opens with the menu's prices, still on screen.
    await userEvent.click(screen.getByRole('button', { name: /^Agregar Latte/ }));
    const options = await screen.findByRole('dialog', { name: 'Latte' });
    await userEvent.click(within(options).getByLabelText(/^Grande/));
    expect(within(options).getByRole('button', { name: 'Agregar · $67.00' })).toBeInTheDocument();

    answerRappi({ data: { items: [taco, steak, rappiLatte], totalCount: 3 } });
    // The app's prices reach the open dialog, and the line takes them.
    await userEvent.click(await within(options).findByRole('button', { name: 'Agregar · $81.00' }));
    expect(await ticket().findByRole('button', { name: 'Registrar pedido de Rappi · $81.00 F9' })).toBeInTheDocument();
  });

  it('says in the ticket when the app\'s prices did not load, and tries again from there', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    let rappiReads = 0;
    vi.mocked(productsApi.getAll).mockImplementation(((params?: { priceChannel?: OrderChannel }) => {
      if (params?.priceChannel !== OrderChannel.Rappi) return Promise.resolve({ data: { items: [taco, steak, latte], totalCount: 3 } });
      rappiReads += 1;
      return rappiReads === 1
        ? Promise.reject(new Error('Network Error'))
        : Promise.resolve({ data: { items: [{ ...taco, priceAmount: 32 }, steak, latte], totalCount: 3 } });
    }) as never);
    renderAt('/sell');

    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    await userEvent.click(within(originPicker()).getByRole('radio', { name: 'Rappi' }));

    // On a phone the ticket covers the catalog and its error: it says so itself, and nothing registers.
    expect(await ticket().findByText('No se cargaron tus precios de Rappi.')).toBeInTheDocument();
    expect(ticket().getByRole('button', { name: 'Registrar pedido de Rappi · $25.00 F9' })).toBeDisabled();
    await userEvent.click(ticket().getByRole('button', { name: 'Reintentar' }));

    expect(await ticket().findByRole('button', { name: 'Registrar pedido de Rappi · $32.00 F9' })).toBeInTheDocument();
    expect(ticket().queryByText('No se cargaron tus precios de Rappi.')).not.toBeInTheDocument();
  });

  it('registers nothing at prices about to change while the app\'s prices load', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    let answerRappi: (answer: unknown) => void = () => undefined;
    vi.mocked(productsApi.getAll).mockImplementation(((params?: { priceChannel?: OrderChannel }) => (params?.priceChannel === OrderChannel.Rappi
      ? new Promise((resolve) => { answerRappi = resolve; })
      : Promise.resolve({ data: { items: [taco, steak, latte], totalCount: 3 } }))) as never);
    renderAt('/sell');

    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
    await userEvent.click(within(originPicker()).getByRole('radio', { name: 'Rappi' }));
    await userEvent.type(ticket().getByLabelText('Folio de Rappi'), 'BC953');

    // Still the menu's $25 on screen: neither the button nor F9 registers it.
    expect(ticket().getByRole('button', { name: 'Registrar pedido de Rappi · $25.00 F9' })).toBeDisabled();
    expect(ticket().getByText('Cargando tus precios de Rappi…')).toBeInTheDocument();
    await userEvent.keyboard('{F9}');
    expect(ordersApi.create).not.toHaveBeenCalled();

    answerRappi({ data: { items: [{ ...taco, priceAmount: 32 }, steak, latte], totalCount: 3 } });
    expect(await ticket().findByRole('button', { name: 'Registrar pedido de Rappi · $32.00 F9' })).toBeEnabled();
    expect(ticket().queryByText('Cargando tus precios de Rappi…')).not.toBeInTheDocument();
  });

  it('sells from the catalog without its hours for an order of a platform, and keeps it off the device', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    renderAt('/sell');

    await userEvent.click(within(await waitFor(originPicker)).getByRole('radio', { name: 'Rappi' }));
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith(expect.objectContaining({ onlyAvailableNow: false })));
    // The catalog saved for selling offline is the one with the hours.
    expect(saveCatalogSnapshot).toHaveBeenCalledTimes(1);

    await userEvent.click(within(originPicker()).getByRole('radio', { name: 'Mostrador' }));
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith(expect.objectContaining({ onlyAvailableNow: true })));
    await waitFor(() => expect(saveCatalogSnapshot).toHaveBeenCalledTimes(2));
  });

  it('searches the whole catalog without its hours for an order of a platform', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    // A catalog bigger than a page: what is not on screen is searched on the server.
    vi.mocked(productsApi.getAll).mockResolvedValue({ data: { items: [taco], totalCount: 900 } } as never);
    renderAt('/sell');

    await userEvent.click(within(await waitFor(originPicker)).getByRole('radio', { name: 'Rappi' }));
    await userEvent.type(screen.getByRole('searchbox', { name: 'Buscar producto o escanear código de barras' }), 'desayuno{Enter}');

    // With the prices of the app, as the catalog on screen.
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'desayuno', onlyAvailableNow: false, priceChannel: OrderChannel.Rappi }),
    ));
  });

  it('brings an order of a platform back after a reload, from the whole catalog', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    window.localStorage.setItem('tienditapp.sell.draft.v1.t1', JSON.stringify({
      lines: [{ productId: 'taco', quantity: 2, notes: '' }],
      customerName: 'Juan', customerPhone: '', tableNumber: '', orderNotes: '',
      channel: OrderChannel.Rappi, externalOrderNumber: 'BC953', collectedBy: PlatformCollectedBy.Platform,
    }));
    renderAt('/sell');

    expect(await ticket().findByLabelText('Folio de Rappi')).toHaveValue('BC953');
    expect(ticket().getByLabelText('Nombre para la bolsa (opcional)')).toHaveValue('Juan');
    expect(ticket().getByRole('textbox', { name: 'Cantidad de Taco' })).toHaveValue('2');
    expect(within(originPicker()).getByRole('radio', { name: 'Rappi' })).toHaveAttribute('aria-checked', 'true');
    // Its app took it whatever the hours here: the very first load already brings every available product.
    expect(vi.mocked(productsApi.getAll).mock.calls[0][0]).toMatchObject({ onlyAvailableNow: false });
  });

  it('takes an order of a platform no longer active back to the counter', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    givePlatforms([{ channel: OrderChannel.Rappi, isActive: false }]);
    window.localStorage.setItem('tienditapp.sell.draft.v1.t1', JSON.stringify({
      lines: [{ productId: 'taco', quantity: 1, notes: '' }],
      customerName: '', customerPhone: '', tableNumber: '', orderNotes: '',
      channel: OrderChannel.Rappi, externalOrderNumber: 'BC953', collectedBy: PlatformCollectedBy.Platform,
    }));
    renderAt('/sell');

    expect(await ticket().findByRole('button', { name: 'Cobrar $25.00 F9' })).toBeInTheDocument();
    expect(ticket().queryByLabelText('Folio de Rappi')).not.toBeInTheDocument();
    // Without an active platform there is nothing to choose: Vender is the counter's.
    expect(ticket().queryByRole('radiogroup', { name: '¿De dónde viene el pedido?' })).not.toBeInTheDocument();
    await waitFor(() => expect(JSON.parse(window.localStorage.getItem('tienditapp.sell.draft.v1.t1') ?? '{}')).toMatchObject({
      channel: OrderChannel.Counter,
      externalOrderNumber: '',
    }));
  });

  it('turns the platforms off while the ticket has a service', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    vi.mocked(productsApi.getAll).mockResolvedValue({ data: { items: [taco, corte], totalCount: 2 } } as never);
    renderAt('/sell');

    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Corte de cabello/ }));

    const picker = await waitFor(originPicker);
    expect(within(picker).getByRole('radio', { name: 'Rappi' })).toBeDisabled();
    expect(within(picker).getByRole('radio', { name: 'Mostrador' })).toBeEnabled();
    expect(ticket().getByText('Los pedidos de plataforma son de productos: quita los servicios del ticket.')).toBeInTheDocument();
  });

  it('says a closed drawer still lets the team register what a platform charged, only with one active', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    vi.mocked(cashApi.getCurrentSession).mockResolvedValue({ data: null } as never);
    const page = renderAt('/sell');
    expect(await screen.findByText(
      'Tu caja está cerrada: ábrela para cobrar. Sin ella puedes guardar pedidos y registrar los que ya cobró una plataforma.',
    )).toBeInTheDocument();
    page.unmount();

    givePlatforms([]);
    renderAt('/sell');
    expect(await screen.findByText(
      'Tu caja está cerrada: ábrela para cobrar. Todo cobro se registra en una caja; sin ella solo puedes guardar pedidos.',
    )).toBeInTheDocument();
  });
});

describe('SellPage with a platform order and no connection', () => {
  // At 19:00 the chilaquiles are out of their hours (sold until 12:00): only the catalog without hours brings them.
  const chilaquiles = product({ id: 'chilaquiles', name: 'Chilaquiles', priceAmount: 95, sortOrder: 5 });
  const comida: CategoryDto[] = [{ id: 'c1', name: 'Comida', sortOrder: 1, isActive: true, productCount: 2 }];
  const draftKey = 'tienditapp.sell.draft.v1.t1';
  let saved: CatalogSnapshot | null;
  let networkDown: boolean;
  const networkError = () => new AxiosError('Network Error', 'ERR_NETWORK');
  const lines = () => ticket().queryAllByRole('textbox', { name: /^Cantidad de / }).map((box) => box.getAttribute('aria-label'));

  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
    saved = null;
    networkDown = false;
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    // The catalog saved on the device is the last one loaded with the hours, as the real cache keeps it.
    vi.mocked(saveCatalogSnapshot).mockImplementation(async (_tenantId, products, categories) => {
      saved = { products, categories, savedAt: '2026-10-12T19:00:00.000Z' };
    });
    vi.mocked(loadCatalogSnapshot).mockImplementation(async () => saved);
    vi.mocked(productsApi.getAll).mockImplementation((async (params?: { onlyAvailableNow?: boolean }) => {
      if (networkDown) throw networkError();
      const items = params?.onlyAvailableNow ? [taco] : [taco, chilaquiles];
      return { data: { items, totalCount: items.length } };
    }) as never);
    vi.mocked(categoriesApi.getAll).mockImplementation((async () => {
      if (networkDown) throw networkError();
      return { data: comida };
    }) as never);
    vi.mocked(ordersApi.create).mockImplementation((async () => {
      if (networkDown) throw networkError();
      return { data: platformOrder(OrderChannel.Rappi, '152390', PlatformCollectedBy.Platform, 'ORD-000009') };
    }) as never);
    vi.mocked(cashApi.getCurrentSession).mockResolvedValue({ data: { isMine: true } } as never);
    vi.mocked(useOnlineStatus).mockReturnValue(true);
    givePlatforms([{ channel: OrderChannel.Rappi, isActive: true }]);
  });

  afterEach(() => {
    onlineManager.setOnline(true);
    vi.restoreAllMocks();
  });

  it('keeps what the app sold out of its hours when the connection drops while registering', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    renderAt('/sell');
    // The counter's catalog, with its hours, is the one saved for selling offline.
    await waitFor(() => expect(saved?.products.map((item) => item.id)).toEqual(['taco']));

    await userEvent.click(within(await waitFor(originPicker)).getByRole('radio', { name: 'Rappi' }));
    await userEvent.type(ticket().getByLabelText('Folio de Rappi'), '152390');
    await userEvent.click(await screen.findByRole('button', { name: /^Agregar Chilaquiles/ }));
    await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));
    expect(lines()).toEqual(['Cantidad de Chilaquiles', 'Cantidad de Taco']);

    networkDown = true;
    const loadsBefore = vi.mocked(productsApi.getAll).mock.calls.length;
    await userEvent.click(ticket().getByRole('button', { name: 'Registrar pedido de Rappi · $120.00 F9' }));
    // The failed sale reloads the catalog; without connection it fails too, and the saved one (with hours) is not used.
    await waitFor(() => expect(vi.mocked(productsApi.getAll).mock.calls.length).toBeGreaterThan(loadsBefore));
    await screen.findByRole('button', { name: 'Reintentar' });
    expect(loadCatalogSnapshot).not.toHaveBeenCalled();
    expect(lines()).toEqual(['Cantidad de Chilaquiles', 'Cantidad de Taco']);

    // Back online, the retry registers the whole order with the same key.
    networkDown = false;
    window.dispatchEvent(new Event('online'));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument());
    await userEvent.click(ticket().getByRole('button', { name: 'Registrar pedido de Rappi · $120.00 F9' }));
    await screen.findByRole('dialog', { name: 'Pedido ORD-000009 de Rappi registrado' });
    const sent = vi.mocked(ordersApi.create).mock.calls.map(([request]) => request);
    expect(sent.map((request) => request.items.map((item) => item.productId))).toEqual([['chilaquiles', 'taco'], ['chilaquiles', 'taco']]);
    expect(sent[1].idempotencyKey).toBe(sent[0].idempotencyKey);
  });

  it('keeps a platform order brought back without connection, with a way back to the counter', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    saved = { products: [taco], categories: comida, savedAt: '2026-10-12T18:00:00.000Z' };
    window.localStorage.setItem(draftKey, JSON.stringify({
      lines: [{ productId: 'chilaquiles', quantity: 1, notes: '' }, { productId: 'taco', quantity: 1, notes: '' }],
      customerName: '', customerPhone: '', tableNumber: '', orderNotes: '',
      channel: OrderChannel.Rappi, externalOrderNumber: '152390', collectedBy: PlatformCollectedBy.Platform,
    }));
    // Without connection the list of platforms waits (paused) and the catalog cannot load.
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);
    onlineManager.setOnline(false);
    vi.mocked(useOnlineStatus).mockReturnValue(false);
    networkDown = true;
    renderAt('/sell');

    await screen.findByRole('button', { name: 'Reintentar' });
    // Nothing of the order is dropped or written over meanwhile, and the counter is still a choice.
    expect(ticket().getByLabelText('Folio de Rappi')).toHaveValue('152390');
    const radios = within(originPicker()).getAllByRole('radio');
    expect(radios.map((radio) => radio.textContent)).toEqual(['Mostrador', 'Rappi']);
    expect(within(originPicker()).getByRole('radio', { name: 'Rappi' })).toHaveAttribute('aria-checked', 'true');
    expect(JSON.parse(window.localStorage.getItem(draftKey) ?? '{}')).toMatchObject({ externalOrderNumber: '152390' });
    expect(JSON.parse(window.localStorage.getItem(draftKey) ?? '{}').lines).toHaveLength(2);

    // Back online, the order comes back whole.
    vi.mocked(useOnlineStatus).mockReturnValue(true);
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(true);
    networkDown = false;
    onlineManager.setOnline(true);
    window.dispatchEvent(new Event('online'));
    await waitFor(() => expect(lines()).toEqual(['Cantidad de Chilaquiles', 'Cantidad de Taco']));
    expect(ticket().getByLabelText('Folio de Rappi')).toHaveValue('152390');
  });

  it('takes a platform order brought back without connection to the counter when the cashier chooses it', async () => {
    signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
    saved = { products: [taco], categories: comida, savedAt: '2026-10-12T18:00:00.000Z' };
    window.localStorage.setItem(draftKey, JSON.stringify({
      lines: [{ productId: 'chilaquiles', quantity: 1, notes: '' }, { productId: 'taco', quantity: 1, notes: '' }],
      customerName: '', customerPhone: '', tableNumber: '', orderNotes: '',
      channel: OrderChannel.Rappi, externalOrderNumber: '152390', collectedBy: PlatformCollectedBy.Platform,
    }));
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);
    onlineManager.setOnline(false);
    vi.mocked(useOnlineStatus).mockReturnValue(false);
    networkDown = true;
    renderAt('/sell');

    await screen.findByRole('button', { name: 'Reintentar' });
    await userEvent.click(within(originPicker()).getByRole('radio', { name: 'Mostrador' }));

    // At the counter the saved catalog (with its hours) sells offline: the sale stays at the counter, without folio.
    expect(await ticket().findByRole('button', { name: 'Cobrar $25.00 F9' })).toBeInTheDocument();
    expect(lines()).toEqual(['Cantidad de Taco']);
    expect(ticket().queryByLabelText('Folio de Rappi')).not.toBeInTheDocument();
    await waitFor(() => expect(JSON.parse(window.localStorage.getItem(draftKey) ?? '{}')).toMatchObject({
      channel: OrderChannel.Counter,
      externalOrderNumber: '',
    }));
  });

  describe('a domicilio', () => {
    it('sends the order to the address with the fee of the zone that reaches its postal code', async () => {
      signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
      giveZones([{ name: 'Centro', fee: 35, minimumOrder: 0, freeDeliveryFrom: 400, postalCodes: ['72000'] }]);
      vi.mocked(ordersApi.create).mockResolvedValue({ data: { orderNumber: 'ORD-000009', stockWarnings: [] } } as never);
      renderAt('/sell');

      await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
      await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));
      await userEvent.click(await ticket().findByRole('switch', { name: 'A domicilio' }));
      await userEvent.type(ticket().getByLabelText('Código postal'), '72000');
      await userEvent.type(ticket().getByLabelText('Dirección'), 'Av. Reforma 123, Centro');
      await userEvent.type(ticket().getByLabelText('Referencias (opcional)'), 'Portón verde');

      expect(ticket().getByText('Centro · Envío $35.00 · gratis desde $400.00')).toBeInTheDocument();
      expect(ticket().getByText('Envío a domicilio').nextSibling).toHaveTextContent('$35.00');
      // The address says where it goes: no table.
      await userEvent.click(ticket().getByRole('button', { name: 'Cliente y notas (opcional)' }));
      expect(ticket().queryByLabelText('Mesa o referencia')).not.toBeInTheDocument();

      await userEvent.click(ticket().getByRole('button', { name: 'Guardar sin cobrar' }));

      expect(ordersApi.create).toHaveBeenCalledWith(expect.objectContaining({
        expectedTotal: 85,
        tableNumber: undefined,
        delivery: { postalCode: '72000', addressLine: 'Av. Reforma 123, Centro', references: 'Portón verde' },
      }));
    });

    it('takes the bill the customer pays the courier with, for the change, and refuses one that does not cover the total', async () => {
      signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
      giveZones([{ name: 'Centro', fee: 35, minimumOrder: 0, freeDeliveryFrom: 400, postalCodes: ['72000'] }]);
      vi.mocked(ordersApi.create).mockResolvedValue({ data: { orderNumber: 'ORD-000010', stockWarnings: [] } } as never);
      renderAt('/sell');

      await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
      await userEvent.click(screen.getByRole('button', { name: /^Agregar Taco/ }));
      await userEvent.click(await ticket().findByRole('switch', { name: 'A domicilio' }));
      await userEvent.type(ticket().getByLabelText('Código postal'), '72000');
      await userEvent.type(ticket().getByLabelText('Dirección'), 'Av. Reforma 123, Centro');
      const bill = ticket().getByLabelText('Paga con (opcional)');
      await userEvent.type(bill, '50');

      expect(ticket().getAllByText('Con $50.00 no alcanza: hay que cobrar $85.00.').length).toBeGreaterThan(0);
      expect(bill).toHaveAttribute('aria-invalid', 'true');
      await userEvent.click(ticket().getByRole('button', { name: 'Guardar sin cobrar' }));
      expect(ordersApi.create).not.toHaveBeenCalled();

      await userEvent.clear(bill);
      await userEvent.type(bill, '100');
      expect(ticket().getByText(/Cambio:/)).toHaveTextContent('$15.00');
      expect(bill).toHaveAttribute('aria-invalid', 'false');
      await userEvent.click(ticket().getByRole('button', { name: 'Guardar sin cobrar' }));

      expect(ordersApi.create).toHaveBeenCalledWith(expect.objectContaining({
        expectedTotal: 85,
        delivery: { postalCode: '72000', addressLine: 'Av. Reforma 123, Centro', references: undefined, paysWith: 100 },
      }));
    });

    it('says where no zone delivers or what the minimum is, and sends nothing', async () => {
      signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
      giveZones([
        { name: 'Centro', fee: 35, minimumOrder: 100, postalCodes: ['72000'] },
        { name: 'Norte', fee: 45, minimumOrder: 0, postalCodes: ['72100'], isActive: false },
      ]);
      renderAt('/sell');

      await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
      await userEvent.click(await ticket().findByRole('switch', { name: 'A domicilio' }));
      const postalCode = ticket().getByLabelText('Código postal');
      await userEvent.type(postalCode, '72100');
      expect(ticket().getByText('Por ahora no llegamos al 72100.')).toBeInTheDocument();

      await userEvent.clear(postalCode);
      await userEvent.type(postalCode, '72000');
      await userEvent.type(ticket().getByLabelText('Dirección'), 'Av. Reforma 123');
      expect(ticket().getByText('El pedido mínimo para el 72000 es de $100.00.')).toBeInTheDocument();
      await userEvent.click(ticket().getByRole('button', { name: 'Guardar sin cobrar' }));

      expect(ordersApi.create).not.toHaveBeenCalled();
      expect(ticket().getByText('El pedido mínimo para el 72000 es de $100.00.', { selector: '.alert' })).toBeInTheDocument();
    });

    it('offers no delivery without active zones, nor for an order of a platform', async () => {
      signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
      giveZones([{ name: 'Norte', fee: 45, minimumOrder: 0, postalCodes: ['72100'], isActive: false }]);
      renderAt('/sell');

      await userEvent.click(await screen.findByRole('button', { name: /^Agregar Taco/ }));
      expect(ticket().queryByRole('switch', { name: 'A domicilio' })).not.toBeInTheDocument();
    });

    it('keeps a delivery brought back with the sale while the zones cannot be read, and says why instead of sending it', async () => {
      signIn(['ManageOrders', 'ChargeOrders', 'ManageCash']);
      const delivery = { postalCode: '72000', addressLine: 'Av. Reforma 123, Centro', references: '' };
      window.localStorage.setItem(draftKey, JSON.stringify({
        lines: [{ productId: 'taco', quantity: 2, notes: '' }],
        customerName: '', customerPhone: '', tableNumber: '', orderNotes: '',
        channel: OrderChannel.Counter, externalOrderNumber: '', collectedBy: PlatformCollectedBy.Platform, delivery,
      }));
      vi.mocked(apiRequest).mockImplementation(((_method: string, path: string) => (path === '/api/v1/delivery-zones'
        ? Promise.reject(new AxiosError('Request failed', 'ERR_BAD_RESPONSE'))
        : Promise.resolve(path === '/api/v1/delivery-platforms/channels' ? [] : undefined))) as unknown as typeof apiRequest);
      renderAt('/sell');

      const zonesError = 'No se pudieron leer tus zonas de entrega: intenta de nuevo o quita «A domicilio».';
      expect(await ticket().findByText(zonesError)).toBeInTheDocument();
      // The address is still there, with its switch, so the cashier can take it off or try again.
      expect(ticket().getByRole('switch', { name: 'A domicilio' })).toBeChecked();
      expect(ticket().getByLabelText('Dirección')).toHaveValue('Av. Reforma 123, Centro');

      await userEvent.click(ticket().getByRole('button', { name: 'Guardar sin cobrar' }));
      expect(ordersApi.create).not.toHaveBeenCalled();
      expect(ticket().getByText(zonesError, { selector: '.alert' })).toBeInTheDocument();
      // A draft from before «paga con» comes back with it empty.
      expect(JSON.parse(window.localStorage.getItem(draftKey) ?? '{}').delivery).toEqual({ ...delivery, paysWith: '' });
    });
  });
});
