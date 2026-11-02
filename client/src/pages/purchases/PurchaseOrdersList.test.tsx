import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { inventoryApi } from '../../api/inventoryApi';
import { purchaseOrdersApi } from '../../api/purchaseOrdersApi';
import { suppliersApi } from '../../api/suppliersApi';
import { setBusinessTimeZone } from '../../utils/dates';
import PurchaseOrdersList from './PurchaseOrdersList';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../components/purchases/PurchaseOrderDetail', () => ({ default: () => null }));
vi.mock('../../api/purchaseOrdersApi', () => ({ purchaseOrdersApi: { getAll: vi.fn(), create: vi.fn() } }));
vi.mock('../../api/suppliersApi', () => ({ suppliersApi: { getAll: vi.fn() } }));
vi.mock('../../api/inventoryApi', () => ({ inventoryApi: { getSupplies: vi.fn(), getLocations: vi.fn() } }));

const page = <T,>(items: T[]) => ({
  items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false,
});

describe('PurchaseOrdersList expected delivery', () => {
  // The business is in Baja California (UTC-7 in October); the device is on Mexico City time (UTC-6).
  beforeEach(() => {
    vi.stubEnv('TZ', 'America/Mexico_City');
    setBusinessTimeZone('America/Tijuana');
    vi.mocked(purchaseOrdersApi.getAll).mockResolvedValue({ data: page([]) } as never);
    vi.mocked(purchaseOrdersApi.create).mockResolvedValue({ data: { id: 'po1' } } as never);
    vi.mocked(suppliersApi.getAll).mockResolvedValue({ data: page([{ id: 's1', name: 'Abarrotes Norte', isActive: true }]) } as never);
    vi.mocked(inventoryApi.getSupplies).mockResolvedValue({
      data: page([{ id: 'i1', name: 'Harina', lastCost: 0, defaultCost: 0 }]),
    } as never);
    vi.mocked(inventoryApi.getLocations).mockResolvedValue({ data: [{ id: 'l1', name: 'Bodega' }] } as never);
  });

  afterEach(() => {
    setBusinessTimeZone(null);
    vi.unstubAllEnvs();
  });

  it('sends the delivery typed as the clock of the business, not the device', async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><PurchaseOrdersList /></MemoryRouter>);

    await user.click(await screen.findByRole('button', { name: 'Nueva compra' }));
    const form = within(await screen.findByRole('dialog', { name: 'Nueva compra' }));
    await waitFor(() => expect(form.getByRole('option', { name: 'Abarrotes Norte' })).toBeInTheDocument());
    await user.selectOptions(form.getByLabelText('Proveedor *'), 'Abarrotes Norte');
    fireEvent.change(form.getByLabelText('Entrega esperada'), { target: { value: '2026-10-08T10:30' } });
    await user.selectOptions(form.getByLabelText('Insumo'), 'Harina');
    await user.selectOptions(form.getByLabelText('Ubicación'), 'Bodega');
    await user.clear(form.getByLabelText('Costo'));
    await user.type(form.getByLabelText('Costo'), '50');
    await user.click(form.getByRole('button', { name: 'Registrar compra' }));

    await waitFor(() => expect(purchaseOrdersApi.create).toHaveBeenCalledTimes(1));
    expect(vi.mocked(purchaseOrdersApi.create).mock.calls[0][0].expectedDeliveryAt).toBe('2026-10-08T17:30:00.000Z');
  });
});
