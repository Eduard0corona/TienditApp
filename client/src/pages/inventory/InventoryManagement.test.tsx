import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { inventoryApi } from '../../api/inventoryApi';
import { productsApi } from '../../api/productsApi';
import {
  InventoryMovementReason,
  InventoryMovementType,
  UnitType,
  type InventoryKardexEntryDto,
  type InventoryLocationDto,
  type InventoryMovementDto,
  type InventoryStockDto,
  type SupplyItemDto,
} from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import InventoryManagement from './InventoryManagement';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/productsApi', () => ({ productsApi: { getAll: vi.fn() } }));
vi.mock('../../api/inventoryApi', () => ({
  inventoryApi: {
    getLocations: vi.fn(), getSupplies: vi.fn(), getStocks: vi.fn(), getMovements: vi.fn(), getKardex: vi.fn(),
    getMovementById: vi.fn(), createMovement: vi.fn(), setSupplyActive: vi.fn(),
  },
}));

const page = <T,>(items: T[], totalCount = items.length) =>
  ({ data: { items, pageNumber: 1, totalPages: Math.ceil(totalCount / 10), totalCount, hasPreviousPage: false, hasNextPage: totalCount > 10 } }) as never;

const locations = [
  { id: 'bar', name: 'Barra', code: 'BAR', isActive: true },
  { id: 'store', name: 'Almacén', code: 'ALM', isActive: true },
  { id: 'old', name: 'Bodega vieja', code: 'BOD', isActive: false },
] as unknown as InventoryLocationDto[];

const milk = {
  id: 'milk', name: 'Leche entera', code: 'LEC-01', unit: UnitType.Liter, isActive: true, defaultCost: 24, lastCost: 23.4567,
  averageCost: 23.8, minStock: 15, reorderPoint: 15, trackBatches: false, trackExpiration: true, isPerishable: true, isCritical: false,
  createdAt: '2026-09-01T12:00:00Z', totalOnHand: 30.5, totalReserved: 2, totalAvailable: 28.5, isLowStock: false,
} as SupplyItemDto;

/**
 * What each location has, reserved and available are the server's: figures that do not add up (12.5 − 2 is not 10) show
 * they are read, not computed on screen.
 */
const milkAtBar = {
  stockId: 'st1', supplyItemId: 'milk', supplyItemName: 'Leche entera', supplyCode: 'LEC-01', unit: UnitType.Liter,
  inventoryLocationId: 'bar', inventoryLocationName: 'Barra', inventoryLocationCode: 'BAR',
  onHandQuantity: 12.5, reservedQuantity: 2, availableQuantity: 10, minStock: 15, reorderPoint: 15, lastCost: 23.4567,
  isSupplyActive: true, isLocationActive: true, isLowStock: true,
} as InventoryStockDto;

/** Beans left in a location that was turned off: its stock still shows, as inactive. */
const beansAtOldStore = {
  stockId: 'st2', supplyItemId: 'beans', supplyItemName: 'Café en grano', supplyCode: 'CAF-KG', unit: UnitType.Kg,
  inventoryLocationId: 'old', inventoryLocationName: 'Bodega vieja', inventoryLocationCode: 'BOD',
  onHandQuantity: 3, reservedQuantity: 0, availableQuantity: 3, minStock: 1, reorderPoint: 1, lastCost: 410,
  isSupplyActive: true, isLocationActive: false, isLowStock: false,
} as InventoryStockDto;

/**
 * The waste of 2.5 L, as its detail sends it. The total of the line is the server's: a figure that is not 2.5 × $24.4567
 * shows it is read, not computed; the cost has four decimals.
 */
const waste: InventoryMovementDto = {
  id: 'mv1', movementNumber: 'INV-20261009-0001', movementType: InventoryMovementType.Exit, movementReason: InventoryMovementReason.Waste,
  movementDate: '2026-10-09T15:00:00Z', referenceNumber: 'MER-0042', counterpartyName: 'Cocina', totalAmount: 61.25,
  createdAt: '2026-10-09T15:00:00Z',
  lines: [{
    id: 'l1', supplyItemId: 'milk', supplyItemName: 'Leche entera', inventoryLocationId: 'bar', inventoryLocationName: 'Barra',
    quantity: 2.5, unitCost: 24.4567, totalCost: 61.25, quantityBefore: 15, quantityAfter: 12.5,
  }],
};

/** 20 L that came in at the bar and the 2.5 L of waste that left: the balance each one left. */
const kardex = [
  {
    movementLineId: 'k1', movementId: 'mv0', movementNumber: 'INV-20261001-0003', movementDate: '2026-10-01T16:00:00Z',
    movementType: InventoryMovementType.Entry, movementReason: InventoryMovementReason.Purchase, referenceNumber: 'FAC-881',
    supplyItemId: 'milk', supplyItemName: 'Leche entera', inventoryLocationId: 'bar', inventoryLocationName: 'Barra',
    quantityIn: 20, quantityOut: 0, signedQuantity: 20, quantityBefore: 0, quantityAfter: 20, unitCost: 23.4567, totalCost: 469.13,
  },
  {
    movementLineId: 'k2', movementId: 'mv1', movementNumber: 'INV-20261009-0001', movementDate: '2026-10-09T15:00:00Z',
    movementType: InventoryMovementType.Exit, movementReason: InventoryMovementReason.Waste, referenceNumber: 'MER-0042',
    supplyItemId: 'milk', supplyItemName: 'Leche entera', inventoryLocationId: 'bar', inventoryLocationName: 'Barra',
    quantityIn: 0, quantityOut: 2.5, signedQuantity: -2.5, quantityBefore: 20, quantityAfter: 17.5,
  },
] as InventoryKardexEntryDto[];

function renderInventory() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter><InventoryManagement /></MemoryRouter>
    </QueryClientProvider>,
  );
}

const tab = (name: string) => screen.getByRole('tab', { name });

describe('InventoryManagement', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(productsApi.getAll).mockResolvedValue(page([]));
    vi.mocked(inventoryApi.getLocations).mockResolvedValue({ data: locations } as never);
    vi.mocked(inventoryApi.getSupplies).mockResolvedValue(page([milk]));
    vi.mocked(inventoryApi.getStocks).mockResolvedValue(page([milkAtBar, beansAtOldStore]));
    // The list of movements does not bring their lines: only the detail does.
    vi.mocked(inventoryApi.getMovements).mockResolvedValue(page([{ ...waste, lines: [] }]));
    vi.mocked(inventoryApi.getKardex).mockResolvedValue(page(kardex));
  });

  afterEach(() => setBusinessTimeZone(null));

  it('opens on the supplies and keeps each tab with its own action', async () => {
    const user = userEvent.setup();
    renderInventory();

    expect(tab('Insumos')).toHaveAttribute('aria-selected', 'true');
    expect(await screen.findByText('Leche entera')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Nuevo insumo' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Registrar movimiento' })).not.toBeInTheDocument();
    // The history by supply is read only when its tab opens.
    expect(inventoryApi.getKardex).not.toHaveBeenCalled();

    await user.click(tab('Entradas y salidas'));
    expect(tab('Entradas y salidas')).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('button', { name: 'Registrar movimiento' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nuevo insumo' })).not.toBeInTheDocument();

    await user.click(tab('Existencias'));
    expect(screen.queryByRole('button', { name: 'Registrar movimiento' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nuevo insumo' })).not.toBeInTheDocument();
    expect(inventoryApi.getKardex).not.toHaveBeenCalled();

    await user.click(tab('Historial por insumo'));
    await waitFor(() => expect(inventoryApi.getKardex).toHaveBeenCalledTimes(1));
  });

  it('shows what each location has, reserved and available, and filters by location and low stock on the server', async () => {
    const user = userEvent.setup();
    vi.mocked(inventoryApi.getStocks).mockResolvedValue(page([milkAtBar, beansAtOldStore], 25));
    renderInventory();

    await user.click(tab('Existencias'));
    // Existencia, Reservado, Disponible, Último costo and Estado.
    const cells = async (location: string) => within((await screen.findByText(location, { selector: 'div' })).closest('tr') as HTMLElement)
      .getAllByRole('cell').slice(2, 7).map((cell) => cell.textContent);
    expect(await cells('Barra')).toEqual(['12.5 Litro', '2', '10Bajo', '$23.4567', 'Activo']);
    expect(await cells('Bodega vieja')).toEqual(['3 Kg', '0', '3', '$410.00', 'Inactivo']);

    // Each filter takes the list back to its first page.
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await waitFor(() => expect(inventoryApi.getStocks).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 })));
    await user.selectOptions(screen.getByLabelText('Ubicación'), 'store');
    await waitFor(() => expect(inventoryApi.getStocks).toHaveBeenLastCalledWith(expect.objectContaining({
      inventoryLocationId: 'store', lowStockOnly: undefined, pageNumber: 1,
    })));
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await user.click(screen.getByLabelText('Solo con pocas existencias'));
    await waitFor(() => expect(inventoryApi.getStocks).toHaveBeenLastCalledWith(expect.objectContaining({
      inventoryLocationId: 'store', lowStockOnly: true, pageNumber: 1,
    })));
    // Only the locations of the business, inactive ones included, are filters: their stock is still shown.
    expect(within(screen.getByLabelText('Ubicación')).getAllByRole('option').map((option) => option.textContent))
      .toEqual(['Todas', 'Barra', 'Almacén', 'Bodega vieja']);
  });

  it('registers a movement at an active location and reads again the lists it changed', async () => {
    const user = userEvent.setup();
    vi.mocked(inventoryApi.createMovement).mockResolvedValue({ data: {} } as never);
    renderInventory();
    await user.click(tab('Entradas y salidas'));
    expect(await screen.findByText('INV-20261009-0001')).toBeInTheDocument();
    await waitFor(() => expect(inventoryApi.getStocks).toHaveBeenCalledTimes(1));
    const before = {
      movements: vi.mocked(inventoryApi.getMovements).mock.calls.length,
      stocks: vi.mocked(inventoryApi.getStocks).mock.calls.length,
      supplies: vi.mocked(inventoryApi.getSupplies).mock.calls.length,
    };

    await user.click(screen.getByRole('button', { name: 'Registrar movimiento' }));
    const dialog = await screen.findByRole('dialog', { name: 'Registrar movimiento de inventario' });
    // An inactive location neither receives nor gives stock.
    expect(within(within(dialog).getByLabelText('Ubicación *')).getAllByRole('option').map((option) => option.textContent))
      .toEqual(['Seleccionar...', 'Barra', 'Almacén']);
    await user.selectOptions(within(dialog).getByLabelText('Insumo *'), 'milk');
    await user.selectOptions(within(dialog).getByLabelText('Ubicación *'), 'store');
    await user.click(within(dialog).getByRole('button', { name: 'Registrar movimiento' }));

    expect(inventoryApi.createMovement).toHaveBeenCalledWith(expect.objectContaining({
      movementType: InventoryMovementType.Entry,
      lines: [expect.objectContaining({ supplyItemId: 'milk', inventoryLocationId: 'store', quantity: 1 })],
    }));
    expect(await screen.findByRole('status')).toHaveTextContent('Movimiento registrado correctamente.');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Registrar movimiento de inventario' })).not.toBeInTheDocument());
    await waitFor(() => {
      expect(inventoryApi.getMovements).toHaveBeenCalledTimes(before.movements + 1);
      expect(inventoryApi.getStocks).toHaveBeenCalledTimes(before.stocks + 1);
      // The list of supplies and the options of the dialogs.
      expect(inventoryApi.getSupplies).toHaveBeenCalledTimes(before.supplies + 2);
    });
  });

  it('lists each movement and opens its detail, read from the server', async () => {
    const user = userEvent.setup();
    setBusinessTimeZone('America/Cancun');
    vi.mocked(inventoryApi.getMovementById).mockResolvedValue({ data: waste } as never);
    renderInventory();
    await user.click(tab('Entradas y salidas'));

    // Folio, Tipo, Motivo, Fecha (Cancún, UTC-5), Referencia with its counterparty below, and Monto.
    const row = (await screen.findByText('INV-20261009-0001')).closest('tr') as HTMLElement;
    expect(within(row).getAllByRole('cell').slice(0, 6).map((cell) => cell.textContent))
      .toEqual(['INV-20261009-0001', 'Salida', 'Merma', expect.stringMatching(/^09\/10\/2026, 10:00/), 'MER-0042Cocina', '$61.25']);
    await user.click(within(row).getByRole('button', { name: 'Ver el movimiento INV-20261009-0001' }));

    expect(inventoryApi.getMovementById).toHaveBeenCalledWith('mv1');
    const detail = await screen.findByRole('dialog', { name: 'Detalle de movimiento' });
    expect(await within(detail).findByText('Leche entera')).toBeInTheDocument();
    expect(detail).toHaveTextContent('Folio: INV-20261009-0001Tipo: SalidaMotivo: Merma');
    // The time reads with its a.m. or p.m., as the device's ICU writes it.
    expect(detail).toHaveTextContent(/Fecha: 09\/10\/2026, 10:00.*Referencia: MER-0042Monto: \$61\.25/);
    // Each line with its cost of four decimals, its total and what the location had before and after.
    const line = within(detail).getByText('Leche entera').closest('tr') as HTMLElement;
    expect(within(line).getAllByRole('cell').slice(1, 7).map((cell) => cell.textContent))
      .toEqual(['Barra', '2.5', '$24.4567', '$61.25', '15', '12.5']);
  });

  it('says why the detail of a movement could not be read, and closes it', async () => {
    const user = userEvent.setup();
    vi.mocked(inventoryApi.getMovementById).mockRejectedValue({
      isAxiosError: true, response: { status: 404, data: { error: 'El movimiento no existe.' } },
    });
    renderInventory();
    await user.click(tab('Entradas y salidas'));

    await user.click(await screen.findByRole('button', { name: 'Ver el movimiento INV-20261009-0001' }));
    expect(await screen.findByText('El movimiento no existe.')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Detalle de movimiento' })).not.toBeInTheDocument());
  });

  it('asks the server for the movements of the type, reason and days of the business that are chosen', async () => {
    const user = userEvent.setup();
    setBusinessTimeZone('America/Cancun');
    renderInventory();
    await user.click(tab('Entradas y salidas'));
    await screen.findByText('INV-20261009-0001');

    await user.selectOptions(screen.getByLabelText('Tipo'), 'Salida');
    await user.selectOptions(screen.getByLabelText('Motivo'), 'Merma');
    await user.type(screen.getByLabelText('Desde'), '2026-10-01');
    await user.type(screen.getByLabelText('Hasta'), '2026-10-05');
    await user.type(screen.getByLabelText('Buscar…'), 'MER-0042');

    await waitFor(() => expect(inventoryApi.getMovements).toHaveBeenLastCalledWith(expect.objectContaining({
      movementType: InventoryMovementType.Exit, movementReason: InventoryMovementReason.Waste,
      fromDate: '2026-10-01T05:00:00.000Z', toDate: '2026-10-06T04:59:59.000Z', search: 'MER-0042', pageNumber: 1,
    })));
  });

  it('shows in the history of a supply what came in, what left and the balance each movement left, filtered on the server', async () => {
    const user = userEvent.setup();
    // Not the zone of the team's machines: a filter read in the device's zone fails here too.
    setBusinessTimeZone('America/Cancun');
    renderInventory();
    await user.click(tab('Historial por insumo'));

    // Entrada, Salida, Saldo, Costo and Referencia (with the external one below it).
    const cells = async (folio: string) => within((await screen.findByText(folio)).closest('tr') as HTMLElement)
      .getAllByRole('cell').slice(4, 9).map((cell) => cell.textContent);
    expect(await cells('INV-20261001-0003')).toEqual(['20', '-', '20', '$23.4567', 'FAC-881-']);
    expect(await cells('INV-20261009-0001')).toEqual(['-', '2.5', '17.5', '-', 'MER-0042-']);

    await user.selectOptions(screen.getByLabelText('Insumo'), 'milk');
    await user.selectOptions(screen.getByLabelText('Ubicación'), 'bar');
    await user.type(screen.getByLabelText('Desde'), '2026-10-01');
    await user.type(screen.getByLabelText('Hasta'), '2026-10-05');
    // Cancún is UTC-5 all year: its day starts at 05:00 UTC and ends at 04:59:59 UTC of the next.
    await waitFor(() => expect(inventoryApi.getKardex).toHaveBeenLastCalledWith(expect.objectContaining({
      supplyItemId: 'milk', inventoryLocationId: 'bar', fromDate: '2026-10-01T05:00:00.000Z', toDate: '2026-10-06T04:59:59.000Z', pageNumber: 1,
    })));
  });

  it('turns a supply off, says so and reads again the supplies and the stock; and says why when it cannot', async () => {
    const user = userEvent.setup();
    vi.mocked(inventoryApi.setSupplyActive)
      .mockResolvedValueOnce({ data: {} } as never)
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 409, data: { error: 'Leche entera está en una receta activa.' } } });
    renderInventory();
    await screen.findByText('Leche entera');
    await waitFor(() => expect(inventoryApi.getStocks).toHaveBeenCalledTimes(1));
    const supplies = vi.mocked(inventoryApi.getSupplies).mock.calls.length;

    await user.click(screen.getByRole('button', { name: 'Desactivar Leche entera' }));
    expect(inventoryApi.setSupplyActive).toHaveBeenCalledWith('milk', false);
    expect(await screen.findByRole('status')).toHaveTextContent('Estado del insumo actualizado.');
    await waitFor(() => {
      expect(inventoryApi.getStocks).toHaveBeenCalledTimes(2);
      // The list of supplies and the options of the dialogs.
      expect(inventoryApi.getSupplies).toHaveBeenCalledTimes(supplies + 2);
    });

    await user.click(screen.getByRole('button', { name: 'Desactivar Leche entera' }));
    expect(await screen.findByText('Leche entera está en una receta activa.')).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
