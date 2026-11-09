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
// The detail of a purchase has its own tests: here, only which purchase it opens.
vi.mock('../../components/purchases/PurchaseOrderDetail', () => ({
  default: ({ orderId }: { orderId: string | null }) => (orderId ? <p>Detalle de la compra {orderId}</p> : null),
}));
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

describe('PurchaseOrdersList new purchase dialog', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(purchaseOrdersApi.getAll).mockResolvedValue({ data: page([]) } as never);
    vi.mocked(suppliersApi.getAll).mockResolvedValue({ data: page([{ id: 's1', name: 'Abarrotes Norte', isActive: true }]) } as never);
    vi.mocked(inventoryApi.getSupplies).mockResolvedValue({
      data: page([{ id: 'i1', name: 'Harina', lastCost: 0, defaultCost: 0 }]),
    } as never);
    vi.mocked(inventoryApi.getLocations).mockResolvedValue({ data: [{ id: 'l1', name: 'Bodega' }] } as never);
  });

  async function open(user: ReturnType<typeof userEvent.setup>) {
    render(<MemoryRouter><PurchaseOrdersList /></MemoryRouter>);
    await user.click(await screen.findByRole('button', { name: 'Nueva compra' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nueva compra' });
    await waitFor(() => expect(within(dialog).getByRole('option', { name: 'Abarrotes Norte' })).toBeInTheDocument());
    return dialog;
  }

  async function fill(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement) {
    const form = within(dialog);
    await user.selectOptions(form.getByLabelText('Proveedor *'), 'Abarrotes Norte');
    await user.selectOptions(form.getByLabelText('Insumo'), 'Harina');
    await user.selectOptions(form.getByLabelText('Ubicación'), 'Bodega');
    await user.clear(form.getByLabelText('Costo'));
    await user.type(form.getByLabelText('Costo'), '50');
  }

  it('says inside the dialog what is missing and why the server did not save, and starts clean when opened again', async () => {
    const user = userEvent.setup();
    vi.mocked(purchaseOrdersApi.create).mockImplementation(() => Promise.reject({
      isAxiosError: true, response: { status: 400, data: { code: 'business_rule', detail: 'El proveedor está inactivo.', error: 'El proveedor está inactivo.' } },
    }));
    const dialog = await open(user);

    await user.click(within(dialog).getByRole('button', { name: 'Registrar compra' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Selecciona un proveedor.');
    expect(purchaseOrdersApi.create).not.toHaveBeenCalled();

    await fill(user, dialog);
    await user.click(within(dialog).getByRole('button', { name: 'Registrar compra' }));
    await waitFor(() => expect(within(dialog).getByRole('alert')).toHaveTextContent('El proveedor está inactivo.'));
    expect(screen.getAllByText('El proveedor está inactivo.')).toHaveLength(1);
    expect(dialog).toHaveClass('show');

    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva compra' })).not.toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'Nueva compra' }));
    expect(within(await screen.findByRole('dialog', { name: 'Nueva compra' })).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('cannot be closed while it is being saved', async () => {
    const user = userEvent.setup();
    vi.mocked(purchaseOrdersApi.create).mockReturnValue(new Promise(() => undefined) as never);
    const dialog = await open(user);

    await fill(user, dialog);
    await user.click(within(dialog).getByRole('button', { name: 'Registrar compra' }));
    expect(within(dialog).getByRole('button', { name: 'Guardando...' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(dialog).toHaveClass('show');
  });

  it('registers a purchase once, even with a double click when the server answers at once', async () => {
    const user = userEvent.setup();
    vi.mocked(purchaseOrdersApi.create).mockResolvedValue({ data: { id: 'po1' } } as never);
    const dialog = await open(user);

    await fill(user, dialog);
    // The second click lands while the dialog fades out: it neither sends again nor says a supplier is missing.
    await user.dblClick(within(dialog).getByRole('button', { name: 'Registrar compra' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva compra' })).not.toBeInTheDocument());
    expect(purchaseOrdersApi.create).toHaveBeenCalledTimes(1);
    expect(screen.getByText('Compra registrada.')).toBeInTheDocument();
  });

  it('opens the detail of the new purchase without waiting for the list to be read again', async () => {
    const user = userEvent.setup();
    vi.mocked(purchaseOrdersApi.create).mockResolvedValue({ data: { id: 'po1' } } as never);
    const dialog = await open(user);
    // The list read after registering does not arrive.
    vi.mocked(purchaseOrdersApi.getAll).mockReturnValue(new Promise(() => undefined) as never);

    await fill(user, dialog);
    await user.click(within(dialog).getByRole('button', { name: 'Registrar compra' }));
    expect(await screen.findByText('Detalle de la compra po1')).toBeInTheDocument();
  });
});
