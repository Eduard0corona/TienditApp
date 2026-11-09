import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { purchaseOrdersApi } from '../../api/purchaseOrdersApi';
import { PurchaseOrderStatus, UnitType, type PurchaseOrderDto } from '../../types';
import PurchaseOrderDetail from './PurchaseOrderDetail';

vi.mock('../../api/purchaseOrdersApi', () => ({ purchaseOrdersApi: { getById: vi.fn(), receive: vi.fn() } }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: vi.fn(), prompt: vi.fn() }) }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: () => ({ tenant: { name: 'Fonda Lupita' } }) }));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const SENT = {
  id: 'po1', supplierId: 's1', supplierName: 'Abarrotes Díaz', supplierPhone: null, orderNumber: 'OC-000007',
  status: PurchaseOrderStatus.Sent, orderedAt: '2026-10-20T15:00:00Z', subTotal: 100, tax: 0, total: 100,
  lines: [{
    id: 'l1', supplyItemId: 'i1', supplyItemName: 'Tortilla', unit: UnitType.Kg, inventoryLocationId: 'b1',
    inventoryLocationName: 'Bodega', quantity: 5, receivedQuantity: 0, pendingQuantity: 5, unitCost: 20, taxPercent: 0,
    subTotal: 100, taxAmount: 0, total: 100,
  }],
} as PurchaseOrderDto;

describe('PurchaseOrderDetail', () => {
  // The panel asks the screen its width; jsdom has no matchMedia.
  beforeAll(() => {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: (media: string) => ({
        matches: false, media, onchange: null, addEventListener: vi.fn(), removeEventListener: vi.fn(),
        addListener: vi.fn(), removeListener: vi.fn(), dispatchEvent: vi.fn(),
      }),
    });
  });
  afterAll(() => {
    Reflect.deleteProperty(window, 'matchMedia');
  });

  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(purchaseOrdersApi.getById).mockResolvedValue({ data: SENT } as never);
  });

  it('stays open while a reception is on its way, so its answer reaches this purchase', async () => {
    const user = userEvent.setup();
    let received: (value: unknown) => void = () => undefined;
    vi.mocked(purchaseOrdersApi.receive).mockReturnValue(new Promise((resolve) => { received = resolve; }) as never);
    const onHide = vi.fn();
    const onModeChange = vi.fn();
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <PurchaseOrderDetail orderId="po1" mode="receive" onModeChange={onModeChange} onHide={onHide} onChanged={vi.fn()} />
      </QueryClientProvider>,
    );

    const panel = await screen.findByRole('dialog', { name: /OC-000007/ });
    await user.click(await within(panel).findByRole('button', { name: 'Registrar recepción' }));

    expect(within(panel).getByRole('button', { name: 'Registrando…' })).toBeDisabled();
    expect(within(panel).getByRole('button', { name: 'Volver' })).toBeDisabled();
    expect(within(panel).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(onHide).not.toHaveBeenCalled();
    expect(purchaseOrdersApi.receive).toHaveBeenCalledWith('po1', expect.objectContaining({
      lines: [{ purchaseOrderLineId: 'l1', quantityReceived: 5, unitCost: undefined }],
    }));

    received({ data: { ...SENT, status: PurchaseOrderStatus.Received } });
    await waitFor(() => expect(onModeChange).toHaveBeenCalledWith('view'));
    expect(toast.success).toHaveBeenCalledWith('Recepción registrada: las existencias ya se actualizaron.');
    await user.keyboard('{Escape}');
    expect(onHide).toHaveBeenCalledTimes(1);
  });
});
