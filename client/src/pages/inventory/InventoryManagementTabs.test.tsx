import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { inventoryApi } from '../../api/inventoryApi';
import type { InventoryCount, InventoryCountListItem } from '../../api/inventoryCounts';
import type { InventoryTransfer, InventoryTransferListItem } from '../../api/inventoryTransfers';
import { productsApi } from '../../api/productsApi';
import { apiRequest } from '../../api/typedClient';
import {
  InventoryCountStatus,
  InventoryMovementReason,
  InventoryMovementType,
  UnitType,
  type InventoryAlertsDto,
  type InventoryKardexEntryDto,
  type InventoryLocationDto,
  type ProductDto,
  type ProductRecipeDto,
  type SupplyItemDto,
} from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import InventoryManagement from './InventoryManagement';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: vi.fn() }) }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../api/productsApi', () => ({ productsApi: { getAll: vi.fn() } }));
vi.mock('../../api/inventoryApi', () => ({
  inventoryApi: {
    getLocations: vi.fn(), getSupplies: vi.fn(), getStocks: vi.fn(), getMovements: vi.fn(), getKardex: vi.fn(), getAlerts: vi.fn(),
    getProductRecipe: vi.fn(), upsertProductRecipe: vi.fn(), setLocationActive: vi.fn(), setSupplyActive: vi.fn(),
  },
}));

const request = vi.mocked(apiRequest);

/** A page as the API sends it. */
const apiPage = <T,>(items: T[], totalCount = items.length) => ({
  items, pageNumber: 1, totalPages: Math.max(1, Math.ceil(totalCount / 10)), totalCount, hasPreviousPage: false, hasNextPage: totalCount > 10,
});
/** The same page as the axios clients return it. */
const page = <T,>(items: T[], totalCount = items.length) => ({ data: apiPage(items, totalCount) }) as never;

function deferred<T = unknown>() {
  let resolve: (value: T) => void = () => undefined;
  let reject: (reason: unknown) => void = () => undefined;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** What the API answers when it refuses: its message is the one said. */
const refused = (message: string, status = 400) => ({ isAxiosError: true, response: { status, data: { error: message } } });

const bar = {
  id: 'bar', name: 'Barra', code: 'BAR', branchName: 'Centro', description: 'Junto a la caja', contactName: 'Ana Ruiz',
  contactPhone: '9991234567', city: 'Mérida', state: 'Yucatán', isDefault: true, isActive: true, createdAt: '2026-01-01T00:00:00Z',
} as InventoryLocationDto;
/** A state without a city. */
const store = {
  id: 'store', name: 'Almacén', code: 'ALM', branchName: null, state: 'Yucatán', isDefault: false, isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
} as InventoryLocationDto;
const old = {
  id: 'old', name: 'Bodega vieja', code: 'BOD', branchName: null, isDefault: false, isActive: false, createdAt: '2026-01-01T00:00:00Z',
} as InventoryLocationDto;
const LOCATIONS = [bar, store, old];

const supply = (id: string, name: string, code: string, unit: UnitType) => ({
  id, name, code, unit, isActive: true, defaultCost: 10, lastCost: 10, averageCost: 10, minStock: 1, reorderPoint: 1, trackBatches: false,
  trackExpiration: false, isPerishable: false, isCritical: false, createdAt: '2026-09-01T12:00:00Z', totalOnHand: 5, totalReserved: 0,
  totalAvailable: 5, isLowStock: false,
}) as SupplyItemDto;
const milk = supply('milk', 'Leche entera', 'LEC-01', UnitType.Liter);
const sugar = supply('sugar', 'Azúcar', 'AZU-01', UnitType.Kg);

const product = (id: string, name: string) => ({ id, name }) as unknown as ProductDto;
/** «Latte» is a product and a service: the editor offers it once. */
const PRODUCTS = [product('latte', 'Latte'), product('americano', 'Americano')];
const SERVICES = [product('catering', 'Servicio de café'), product('latte', 'Latte')];

const AMERICANO: ProductRecipeDto = {
  productId: 'americano', productName: 'Americano', productIsAvailable: true,
  lines: [{
    requirementId: 'r1', supplyItemId: 'milk', supplyItemName: 'Leche entera', supplyCode: 'LEC-01', inventoryLocationId: 'bar',
    inventoryLocationName: 'Barra', quantity: 0.2, wastePercent: 10, effectiveQuantity: 0.22, notes: 'Espumada', isActive: true,
  }],
};
const LATTE: ProductRecipeDto = {
  productId: 'latte', productName: 'Latte', productIsAvailable: false,
  lines: [{
    requirementId: 'r2', supplyItemId: 'milk', supplyItemName: 'Leche entera', supplyCode: 'LEC-01', quantity: 0.3, wastePercent: 0,
    effectiveQuantity: 0.3, notes: 'Para el latte', isActive: true,
  }],
};

/** The counts are the server's: they do not add up to the items listed, so they are read, not counted on screen. */
const ALERTS: InventoryAlertsDto = {
  outOfStockCount: 1, criticalLowStockCount: 2, lowStockCount: 5, missingRecipeProductCount: 4,
  items: [
    { supplyItemId: 'milk', supplyItemName: 'Leche entera', inventoryLocationId: 'bar', inventoryLocationName: 'Barra', onHandQuantity: 0,
      reorderPoint: 15, deficit: 15, isOutOfStock: true, isCritical: true },
    { supplyItemId: 'beans', supplyItemName: 'Café en grano', inventoryLocationId: 'store', inventoryLocationName: 'Almacén',
      onHandQuantity: 0.5, reorderPoint: 2, deficit: 1.5, isOutOfStock: false, isCritical: true },
    { supplyItemId: 'sugar', supplyItemName: 'Azúcar', inventoryLocationId: 'bar', inventoryLocationName: 'Barra', onHandQuantity: 3,
      reorderPoint: 5, deficit: 2, isOutOfStock: false, isCritical: false },
  ],
} as InventoryAlertsDto;

const KARDEX = [
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

const TRANSFER_ROW = {
  id: 't1', transferNumber: 'TRF-20261021-0001', fromLocationId: 'store', fromLocationName: 'Almacén', toLocationId: 'bar',
  toLocationName: 'Barra', lineCount: 2, totalCost: 195, transferredAt: '2026-10-21T18:00:00Z', transferredBy: 'encargado',
} as InventoryTransferListItem;
const TRANSFER = {
  ...TRANSFER_ROW, notes: null, outMovementId: 'm-out', outMovementNumber: 'INV-20261021-0007', inMovementId: 'm-in',
  inMovementNumber: 'INV-20261021-0008',
  lines: [{
    supplyItemId: 'sugar', supplyItemName: 'Azúcar', unit: UnitType.Kg, quantity: 2.5, unitCost: 30, totalCost: 75, fromQuantityBefore: 10,
    fromQuantityAfter: 7.5, toQuantityBefore: 0, toQuantityAfter: 2.5,
  }],
} as unknown as InventoryTransfer;

const OPEN_COUNT = {
  id: 'c1', countNumber: 'CNT-20261021-0001', inventoryLocationId: 'bar', inventoryLocationName: 'Barra', categoryName: 'Bebidas',
  status: InventoryCountStatus.InProgress, lineCount: 3, countedLineCount: 2, startedAt: '2026-10-21T18:00:00Z', startedBy: 'dueno',
} as InventoryCountListItem;
const POSTED_COUNT = {
  id: 'c2', countNumber: 'CNT-20261001-0004', inventoryLocationId: 'store', inventoryLocationName: 'Almacén', categoryName: null,
  status: InventoryCountStatus.Posted, lineCount: 10, countedLineCount: 10, startedAt: '2026-10-01T15:00:00Z', startedBy: 'encargado',
} as InventoryCountListItem;
const COUNT = {
  ...OPEN_COUNT, differenceLineCount: 0, surplusValue: 0, shortageValue: 0, revision: 1,
} as unknown as InventoryCount;

function renderInventory() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter><InventoryManagement /></MemoryRouter>
    </QueryClientProvider>,
  );
  return { client };
}

const tab = (name: string) => screen.getByRole('tab', { name });

/**
 * The typed client answers by path: the transfers and counts read through it, and the branches of the location dialog
 * too, which the screen reads when it opens.
 */
function answer(paths: Record<string, () => unknown>) {
  request.mockImplementation(((_method: string, path: string) => {
    if (path === '/api/v1/branches') return Promise.resolve([]);
    const reply = paths[path];
    return reply ? Promise.resolve().then(reply) : Promise.reject(new Error(`Sin respuesta para ${path}`));
  }) as never);
}

/** The answers in turn: a function throws or returns a promise, a value answers at once. */
const inTurn = (...replies: Array<() => unknown>) => {
  let next = 0;
  return () => replies[Math.min(next++, replies.length - 1)]();
};

/** The controls of a line of the recipe. */
const line = (n: number) => ({
  supply: screen.getByLabelText(`Insumo de la línea ${n}`),
  location: screen.getByLabelText(`Ubicación de la línea ${n}`),
  quantity: screen.getByLabelText(`Cantidad de la línea ${n}`),
  waste: screen.getByLabelText(`Merma de la línea ${n}`),
  notes: screen.getByLabelText(`Notas de la línea ${n}`),
});

const options = (select: HTMLElement) => within(select).getAllByRole('option').map((option) => option.textContent);

const cellsOf = (element: HTMLElement) =>
  within(element.closest('tr') as HTMLElement).getAllByRole('cell').map((cell) => cell.textContent);

describe('InventoryManagement tabs', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(productsApi.getAll).mockImplementation(((params: { itemType?: string }) =>
      Promise.resolve(page(params.itemType === 'Service' ? SERVICES : PRODUCTS))) as never);
    vi.mocked(inventoryApi.getLocations).mockResolvedValue({ data: LOCATIONS } as never);
    vi.mocked(inventoryApi.getSupplies).mockResolvedValue(page([milk, sugar]));
    vi.mocked(inventoryApi.getStocks).mockResolvedValue(page([]));
    vi.mocked(inventoryApi.getMovements).mockResolvedValue(page([]));
    vi.mocked(inventoryApi.getKardex).mockResolvedValue(page(KARDEX));
    vi.mocked(inventoryApi.getAlerts).mockResolvedValue({ data: ALERTS } as never);
    vi.mocked(inventoryApi.getProductRecipe).mockImplementation(((id: string) =>
      Promise.resolve({ data: id === 'latte' ? LATTE : AMERICANO })) as never);
  });

  afterEach(() => setBusinessTimeZone(null));

  describe('Recetas', () => {
    it('reads the products and services when the tab first opens, opens the first with its recipe, and keeps the changes while another tab is open', async () => {
      const user = userEvent.setup();
      renderInventory();
      await screen.findByText('Leche entera');
      expect(productsApi.getAll).not.toHaveBeenCalled();
      expect(inventoryApi.getProductRecipe).not.toHaveBeenCalled();

      await user.click(tab('Recetas'));
      const item = screen.getByLabelText('Producto o servicio');
      await waitFor(() => expect(item).toHaveValue('americano'));
      expect(options(item)).toEqual(['Seleccionar...', 'Americano', 'Latte', 'Servicio de café']);
      expect(productsApi.getAll).toHaveBeenCalledWith({ itemType: 'Product', pageNumber: 1, pageSize: 500 });
      expect(productsApi.getAll).toHaveBeenCalledWith({ itemType: 'Service', pageNumber: 1, pageSize: 500 });
      expect(inventoryApi.getProductRecipe).toHaveBeenCalledWith('americano');

      // The line as the server sent it, with what it takes with its waste.
      expect(await screen.findByLabelText('Insumo de la línea 1')).toHaveDisplayValue('Leche entera (LEC-01)');
      expect(line(1).location).toHaveDisplayValue('Barra');
      expect(line(1).quantity).toHaveValue(0.2);
      expect(line(1).waste).toHaveValue(10);
      expect(line(1).notes).toHaveValue('Espumada');
      expect(cellsOf(line(1).supply)[4]).toBe('0.22');
      expect(screen.getByText('Ítem seleccionado').nextElementSibling).toHaveTextContent('Americano');
      expect(screen.getByText('Disponible')).toBeInTheDocument();
      // Only the active supplies and locations are offered.
      expect(options(line(1).supply)).toEqual(['Seleccionar...', 'Leche entera (LEC-01)', 'Azúcar (AZU-01)']);
      expect(options(line(1).location)).toEqual(['Ubicación por defecto', 'Barra', 'Almacén']);

      await user.clear(line(1).quantity);
      await user.type(line(1).quantity, '0.25');
      expect(cellsOf(line(1).supply)[4]).toBe('0.275');

      await user.click(tab('Alertas'));
      await user.click(tab('Recetas'));
      expect(line(1).quantity).toHaveValue(0.25);
      expect(productsApi.getAll).toHaveBeenCalledTimes(2);
      expect(inventoryApi.getProductRecipe).toHaveBeenCalledTimes(1);
    });

    it('saves the recipe with nothing changing meanwhile, says so, and reads the alerts and the history again behind', async () => {
      const user = userEvent.setup();
      const saving = deferred();
      vi.mocked(inventoryApi.upsertProductRecipe).mockReturnValue(saving.promise as never);
      // The lists read again behind do not hold the editor.
      vi.mocked(inventoryApi.getAlerts).mockReturnValue(new Promise(() => undefined) as never);
      renderInventory();
      await user.click(tab('Recetas'));
      await screen.findByLabelText('Insumo de la línea 1');
      await user.clear(line(1).quantity);
      await user.type(line(1).quantity, '0.25');

      await user.click(screen.getByRole('button', { name: 'Guardar receta' }));
      expect(inventoryApi.upsertProductRecipe).toHaveBeenCalledWith('americano', {
        lines: [{ requirementId: 'r1', supplyItemId: 'milk', inventoryLocationId: 'bar', quantity: 0.25, wastePercent: 10, notes: 'Espumada' }],
      });
      // The answer replaces the lines: nothing changes until it arrives.
      expect(screen.getByRole('button', { name: 'Guardando…' })).toBeDisabled();
      for (const control of [
        screen.getByLabelText('Producto o servicio'), ...Object.values(line(1)), screen.getByRole('button', { name: 'Quitar la línea 1' }),
        screen.getByRole('button', { name: 'Agregar línea' }), screen.getByRole('button', { name: 'Recargar' }),
      ]) expect(control).toBeDisabled();

      await act(async () => saving.resolve({
        data: { ...AMERICANO, productIsAvailable: false, lines: [{ ...AMERICANO.lines[0], quantity: 0.25, notes: 'Espumada, sin azúcar' }] },
      }));

      expect(await screen.findByText('Receta guardada correctamente.')).toBeInTheDocument();
      expect(line(1).notes).toHaveValue('Espumada, sin azúcar');
      expect(screen.getByText('No disponible')).toBeInTheDocument();
      // Ready again with the answer, though the alerts have not answered.
      expect(screen.getByRole('button', { name: 'Guardar receta' })).toBeEnabled();
      expect(line(1).quantity).toBeEnabled();
      expect(inventoryApi.getAlerts).toHaveBeenCalledTimes(1);
      expect(inventoryApi.getKardex).toHaveBeenCalledTimes(1);

      // Another item does not keep the message of this one.
      await user.selectOptions(screen.getByLabelText('Producto o servicio'), 'latte');
      expect(screen.queryByText('Receta guardada correctamente.')).not.toBeInTheDocument();
      await waitFor(() => expect(line(1).notes).toHaveValue('Para el latte'));
    });

    it('checks each line before sending it, says what the server refused, and keeps the lines', async () => {
      const user = userEvent.setup();
      vi.mocked(inventoryApi.upsertProductRecipe)
        .mockRejectedValue(refused('No puedes repetir el mismo insumo y ubicación en la receta.'));
      renderInventory();
      await user.click(tab('Recetas'));
      await screen.findByLabelText('Insumo de la línea 1');
      const save = screen.getByRole('button', { name: 'Guardar receta' });

      await user.click(screen.getByRole('button', { name: 'Agregar línea' }));
      expect(line(2).supply).toHaveValue('');
      expect(line(2).quantity).toHaveValue(1);
      expect(line(2).waste).toHaveValue(0);
      await user.click(save);
      expect(screen.getByRole('alert')).toHaveTextContent('Línea 2: el insumo es obligatorio.');

      await user.selectOptions(line(2).supply, 'sugar');
      await user.clear(line(2).quantity);
      await user.type(line(2).quantity, '0');
      await user.click(save);
      expect(screen.getByRole('alert')).toHaveTextContent('Línea 2: cantidad inválida.');
      expect(inventoryApi.upsertProductRecipe).not.toHaveBeenCalled();

      await user.clear(line(2).quantity);
      await user.type(line(2).quantity, '0.5');
      await user.selectOptions(line(2).location, 'store');
      await user.click(save);
      expect(inventoryApi.upsertProductRecipe).toHaveBeenCalledWith('americano', {
        lines: [
          { requirementId: 'r1', supplyItemId: 'milk', inventoryLocationId: 'bar', quantity: 0.2, wastePercent: 10, notes: 'Espumada' },
          { requirementId: undefined, supplyItemId: 'sugar', inventoryLocationId: 'store', quantity: 0.5, wastePercent: 0, notes: undefined },
        ],
      });
      expect(await screen.findByRole('alert')).toHaveTextContent('No puedes repetir el mismo insumo y ubicación en la receta.');
      expect(line(2).supply).toHaveValue('sugar');
      expect(save).toBeEnabled();

      await user.click(screen.getByRole('button', { name: 'Quitar la línea 2' }));
      expect(screen.queryByLabelText('Insumo de la línea 2')).not.toBeInTheDocument();
      expect(line(1).notes).toHaveValue('Espumada');
    });

    it('does not let a recipe of another item that answers late replace the one chosen after', async () => {
      const user = userEvent.setup();
      const americano = deferred();
      const latte = deferred();
      const catering = deferred();
      const cateringAgain = deferred();
      const reads: Record<string, Array<Promise<unknown>>> = {
        americano: [americano.promise], latte: [latte.promise], catering: [catering.promise, cateringAgain.promise],
      };
      vi.mocked(inventoryApi.getProductRecipe).mockImplementation(((id: string) =>
        (reads[id].length > 1 ? reads[id].shift() : reads[id][0])) as never);
      vi.mocked(inventoryApi.upsertProductRecipe).mockResolvedValue({ data: LATTE } as never);
      renderInventory();
      await user.click(tab('Recetas'));
      const item = screen.getByLabelText('Producto o servicio');
      await waitFor(() => expect(item).toHaveValue('americano'));

      await user.selectOptions(item, 'latte');
      await act(async () => americano.resolve({ data: AMERICANO }));
      // The latte is still being read: the americano neither shows nor ends the reading.
      expect(screen.queryByDisplayValue('Espumada')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Guardar receta' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Agregar línea' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Recargar' })).toBeDisabled();

      await act(async () => latte.resolve({ data: LATTE }));
      expect(line(1).notes).toHaveValue('Para el latte');
      expect(screen.getByRole('button', { name: 'Guardar receta' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Agregar línea' })).toBeEnabled();

      // One left behind that answers after the latte arrived does not replace it.
      await user.selectOptions(item, 'catering');
      await user.selectOptions(item, 'latte');
      await waitFor(() => expect(line(1).notes).toHaveValue('Para el latte'));
      await act(async () => catering.resolve({
        data: { ...LATTE, productId: 'catering', productName: 'Servicio de café', lines: [{ ...LATTE.lines[0], notes: 'Para el evento' }] },
      }));
      expect(line(1).notes).toHaveValue('Para el latte');
      expect(screen.getByText('Ítem seleccionado').nextElementSibling).toHaveTextContent('Latte');
      // Saving sends the recipe of the latte, not the lines of the other item.
      await user.click(screen.getByRole('button', { name: 'Guardar receta' }));
      expect(inventoryApi.upsertProductRecipe).toHaveBeenCalledWith('latte', {
        lines: [{ requirementId: 'r2', supplyItemId: 'milk', inventoryLocationId: undefined, quantity: 0.3, wastePercent: 0, notes: 'Para el latte' }],
      });
      await screen.findByText('Receta guardada correctamente.');

      // Nor does its failure.
      await user.selectOptions(item, 'catering');
      await user.selectOptions(item, 'latte');
      await waitFor(() => expect(line(1).notes).toHaveValue('Para el latte'));
      await act(async () => cateringAgain.reject(refused('El servicio no tiene receta.')));
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(line(1).notes).toHaveValue('Para el latte');
    });

    it('leaves nothing waiting when no item is chosen while a recipe is read', async () => {
      const user = userEvent.setup();
      const americano = deferred();
      vi.mocked(inventoryApi.getProductRecipe).mockReturnValue(americano.promise as never);
      renderInventory();
      await user.click(tab('Recetas'));
      await waitFor(() => expect(screen.getByLabelText('Producto o servicio')).toHaveValue('americano'));
      expect(screen.getByRole('button', { name: 'Recargar' })).toBeDisabled();

      await user.selectOptions(screen.getByLabelText('Producto o servicio'), '');
      expect(screen.getByText('Selecciona un producto o servicio para administrar su receta.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Recargar' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Guardar receta' })).toBeEnabled();
      await act(async () => americano.resolve({ data: AMERICANO }));
      expect(screen.queryByText('Receta de insumos')).not.toBeInTheDocument();
      expect(screen.getByText('Ítem seleccionado').nextElementSibling).toHaveTextContent('-');
    });

    it('does not save empty a recipe it could not read, which would erase it, and Recargar reads it again', async () => {
      const user = userEvent.setup();
      vi.mocked(inventoryApi.getProductRecipe).mockRejectedValueOnce(refused('La receta no está disponible.', 404));
      renderInventory();
      await user.click(tab('Recetas'));

      expect(await screen.findByRole('alert')).toHaveTextContent('La receta no está disponible.');
      expect(screen.getByText('No se pudo leer la receta de este ítem. Usa «Recargar» para intentarlo otra vez.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Guardar receta' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Agregar línea' })).toBeDisabled();

      await user.click(screen.getByRole('button', { name: 'Recargar' }));
      expect(await screen.findByLabelText('Insumo de la línea 1')).toHaveDisplayValue('Leche entera (LEC-01)');
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Guardar receta' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Agregar línea' })).toBeEnabled();
      expect(inventoryApi.getProductRecipe).toHaveBeenCalledTimes(2);
    });

    it('asks for an item when none is chosen, also after one could not be read', async () => {
      const user = userEvent.setup();
      vi.mocked(inventoryApi.getProductRecipe).mockRejectedValueOnce(refused('La receta no está disponible.', 404));
      renderInventory();
      await user.click(tab('Recetas'));
      expect(await screen.findByRole('alert')).toHaveTextContent('La receta no está disponible.');

      await user.selectOptions(screen.getByLabelText('Producto o servicio'), '');
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Guardar receta' }));
      expect(screen.getByRole('alert')).toHaveTextContent('Selecciona un producto o servicio.');
      expect(inventoryApi.upsertProductRecipe).not.toHaveBeenCalled();
    });

    it('says the products are being read, with nothing to save or read again meanwhile', async () => {
      const user = userEvent.setup();
      const listed = deferred();
      vi.mocked(productsApi.getAll).mockImplementation(((params: { itemType?: string }) =>
        listed.promise.then(() => page(params.itemType === 'Service' ? SERVICES : PRODUCTS))) as never);
      renderInventory();
      await user.click(tab('Recetas'));

      expect(await screen.findByText('Cargando productos y servicios…')).toBeInTheDocument();
      expect(screen.getByLabelText('Producto o servicio')).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Guardar receta' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Recargar' })).toBeDisabled();

      await act(async () => listed.resolve(undefined));
      await waitFor(() => expect(screen.getByLabelText('Producto o servicio')).toHaveValue('americano'));
      expect(screen.getByLabelText('Producto o servicio')).toBeEnabled();
      expect(productsApi.getAll).toHaveBeenCalledTimes(2);
    });

    it('says the products could not be read, and Recargar reads them again', async () => {
      const user = userEvent.setup();
      vi.mocked(productsApi.getAll).mockRejectedValueOnce(new Error('Network Error'));
      renderInventory();
      await user.click(tab('Recetas'));

      expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron cargar los productos y servicios.');
      expect(screen.getByText('Selecciona un producto o servicio para administrar su receta.')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Recargar' }));
      await waitFor(() => expect(screen.getByLabelText('Producto o servicio')).toHaveValue('americano'));
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(await screen.findByLabelText('Insumo de la línea 1')).toHaveDisplayValue('Leche entera (LEC-01)');
    });

    it('says which supply or location a line takes when it was turned off after the recipe was saved', async () => {
      const user = userEvent.setup();
      const active = deferred();
      let optionReads = 0;
      vi.mocked(inventoryApi.getSupplies).mockImplementation(((params: { pageSize: number }) => {
        if (params.pageSize !== 500) return Promise.resolve(page([milk, sugar]));
        optionReads += 1;
        // Azúcar is turned off later: the second reading no longer brings it.
        return optionReads === 1 ? active.promise : Promise.resolve(page([milk]));
      }) as never);
      vi.mocked(inventoryApi.setSupplyActive).mockResolvedValue({ data: {} } as never);
      vi.mocked(inventoryApi.upsertProductRecipe).mockRejectedValue(refused('Uno o más insumos no existen o están inactivos.'));
      vi.mocked(inventoryApi.getProductRecipe).mockResolvedValue({
        data: {
          ...AMERICANO,
          lines: [
            { requirementId: 'r1', supplyItemId: 'beans', supplyItemName: 'Café en grano', supplyCode: 'CAF-KG', inventoryLocationId: 'old',
              inventoryLocationName: 'Bodega vieja', quantity: 0.02, wastePercent: 0, effectiveQuantity: 0.02, isActive: true },
            { requirementId: 'r3', supplyItemId: 'milk', supplyItemName: 'Leche entera', supplyCode: 'LEC-01', inventoryLocationId: 'gone',
              inventoryLocationName: 'Bodega cerrada', quantity: 0.1, wastePercent: 0, effectiveQuantity: 0.1, isActive: true },
          ],
        },
      } as never);
      renderInventory();
      await user.click(tab('Recetas'));

      // Before the active supplies arrive it cannot say the supply is off: only which it is.
      expect(await screen.findByLabelText('Insumo de la línea 1')).toHaveDisplayValue('Café en grano (CAF-KG)');
      await act(async () => active.resolve(page([milk, sugar])));
      expect(line(1).supply).toHaveDisplayValue('Café en grano (CAF-KG) · inactivo');
      expect(line(1).location).toHaveDisplayValue('Bodega vieja · inactiva');
      expect(line(2).supply).toHaveDisplayValue('Leche entera (LEC-01)');
      // A location that is not among those of the business: the name the recipe has.
      expect(line(2).location).toHaveDisplayValue('Bodega cerrada');

      // Found by the names the recipe has.
      const search = screen.getByLabelText('Buscar…');
      await user.type(search, 'café');
      expect(screen.getByLabelText('Insumo de la línea 1')).toBeInTheDocument();
      expect(screen.queryByLabelText('Insumo de la línea 2')).not.toBeInTheDocument();
      await user.clear(search);
      await user.type(search, 'cerrada');
      expect(screen.getByLabelText('Insumo de la línea 2')).toBeInTheDocument();
      expect(screen.queryByLabelText('Insumo de la línea 1')).not.toBeInTheDocument();
      await user.clear(search);

      // The server refuses a recipe with a supply or location turned off; the line still says which.
      await user.click(screen.getByRole('button', { name: 'Guardar receta' }));
      expect(await screen.findByRole('alert')).toHaveTextContent('Uno o más insumos no existen o están inactivos.');
      expect(line(1).supply).toHaveDisplayValue('Café en grano (CAF-KG) · inactivo');

      // Changed, the line offers only the active ones.
      await user.selectOptions(line(1).supply, 'sugar');
      expect(options(line(1).supply)).toEqual(['Seleccionar...', 'Leche entera (LEC-01)', 'Azúcar (AZU-01)']);
      await user.selectOptions(line(1).location, '');
      expect(options(line(1).location)).toEqual(['Ubicación por defecto', 'Barra', 'Almacén']);

      // The supply chosen is turned off after: the line does not name the one the recipe saved.
      await user.click(tab('Insumos'));
      await user.click(await screen.findByRole('button', { name: 'Desactivar Azúcar' }));
      await screen.findByText('Estado del insumo actualizado.');
      await user.click(tab('Recetas'));
      expect(line(1).supply).toHaveValue('sugar');
      expect(line(1).supply).toHaveDisplayValue('Insumo inactivo');
    });

    it('does not call a saved supply inactive when not every active supply was read', async () => {
      const user = userEvent.setup();
      // A business with more active supplies than the API sends at once.
      vi.mocked(inventoryApi.getSupplies).mockImplementation(((params: { pageSize: number }) =>
        Promise.resolve(params.pageSize === 500 ? page([milk, sugar], 600) : page([milk, sugar]))) as never);
      vi.mocked(inventoryApi.getProductRecipe).mockResolvedValue({
        data: {
          ...AMERICANO,
          lines: [{ requirementId: 'r1', supplyItemId: 'carrot', supplyItemName: 'Zanahoria', supplyCode: 'ZAN', inventoryLocationId: 'old',
            inventoryLocationName: 'Bodega vieja', quantity: 1, wastePercent: 0, effectiveQuantity: 1, isActive: true }],
        },
      } as never);
      renderInventory();
      await user.click(tab('Recetas'));

      expect(await screen.findByLabelText('Insumo de la línea 1')).toHaveDisplayValue('Zanahoria (ZAN)');
      // Every location is read: that one is off.
      expect(line(1).location).toHaveDisplayValue('Bodega vieja · inactiva');
    });
  });

  describe('Ubicaciones', () => {
    it('lists each location with its branch, contact and place, and finds it by name, code, city, state or branch', async () => {
      const user = userEvent.setup();
      renderInventory();
      await user.click(tab('Ubicaciones'));

      const row = async (name: string) => cellsOf(await screen.findByText(name, { selector: '.fw-semibold' })).slice(0, 5);
      expect(await row('Barra')).toEqual(['BarraSucursal Centro · Junto a la caja', 'BAR', 'Ana Ruiz9991234567', 'Mérida, Yucatán', 'Por defectoActiva']);
      expect(await row('Almacén')).toEqual(['AlmacénAlmacén general', 'ALM', '--', 'Yucatán', 'Activa']);
      expect(await row('Bodega vieja')).toEqual(['Bodega viejaAlmacén general', 'BOD', '--', '-', 'Inactiva']);

      // The default one is not turned off (the server refuses it): another is made the default first.
      expect(screen.getByRole('button', { name: 'Desactivar Barra' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Desactivar Barra' })).toHaveAttribute('title', expect.stringContaining('ubicación por defecto'));
      expect(screen.getByRole('button', { name: 'Desactivar Almacén' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Activar Bodega vieja' })).toBeEnabled();

      const names = () => screen.queryAllByText(/^(Barra|Almacén|Bodega vieja)$/, { selector: '.fw-semibold' }).map((cell) => cell.textContent);
      const search = screen.getByLabelText('Buscar…');
      for (const [term, found] of [['yucat', ['Barra', 'Almacén']], ['centro', ['Barra']], ['mérida', ['Barra']], ['bod', ['Bodega vieja']], ['alm', ['Almacén']]] as const) {
        await user.clear(search);
        await user.type(search, term);
        expect(names()).toEqual(found);
      }

      await user.clear(search);
      await user.click(screen.getByRole('button', { name: 'Editar Almacén' }));
      const dialog = await screen.findByRole('dialog', { name: 'Editar ubicación' });
      expect(within(dialog).getByLabelText('Nombre *')).toHaveValue('Almacén');
    });

    it('turns a location off without sending it twice, reads the list again and says why it cannot', async () => {
      const user = userEvent.setup();
      const answer = deferred();
      const relisted = deferred();
      vi.mocked(inventoryApi.setLocationActive)
        .mockReturnValueOnce(answer.promise as never)
        .mockRejectedValueOnce(new Error('Network Error'));
      vi.mocked(inventoryApi.getLocations)
        .mockResolvedValueOnce({ data: LOCATIONS } as never)
        .mockReturnValueOnce(relisted.promise as never)
        .mockResolvedValue({ data: [bar, { ...store, isActive: false }, old] } as never);
      renderInventory();
      await waitFor(() => expect(inventoryApi.getStocks).toHaveBeenCalledTimes(1));
      await user.click(tab('Ubicaciones'));

      await user.click(await screen.findByRole('button', { name: 'Desactivar Almacén' }));
      expect(inventoryApi.setLocationActive).toHaveBeenCalledWith('store', false);
      expect(screen.getByRole('button', { name: 'Desactivar Almacén' })).toBeDisabled();

      await act(async () => answer.resolve({ data: {} }));
      expect(await screen.findByText('Estado de la ubicación actualizado.')).toBeInTheDocument();
      // The old row would send the same change again: while the list is read again it is not offered.
      expect(screen.queryByRole('button', { name: /Almacén$/ })).not.toBeInTheDocument();

      await act(async () => relisted.resolve({ data: [bar, { ...store, isActive: false }, old] }));
      expect(await screen.findByRole('button', { name: 'Activar Almacén' })).toBeEnabled();
      expect(inventoryApi.getStocks).toHaveBeenCalledTimes(2);
      expect(inventoryApi.setLocationActive).toHaveBeenCalledTimes(1);

      await user.click(screen.getByRole('button', { name: 'Activar Almacén' }));
      expect(inventoryApi.setLocationActive).toHaveBeenLastCalledWith('store', true);
      expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo actualizar el estado de la ubicación.');
      expect(screen.queryByText('Estado de la ubicación actualizado.')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Activar Almacén' })).toBeEnabled();
    });
  });

  describe('Insumos', () => {
    it('reads the supplies again with the search and filters in sight after turning one off', async () => {
      const user = userEvent.setup();
      const answer = deferred();
      vi.mocked(inventoryApi.setSupplyActive).mockReturnValueOnce(answer.promise as never);
      vi.mocked(inventoryApi.getSupplies).mockImplementation(((params: { pageSize: number; search?: string }) =>
        Promise.resolve(page(params.pageSize === 500 || !params.search ? [milk, sugar] : [milk]))) as never);
      const listQueries = () => vi.mocked(inventoryApi.getSupplies).mock.calls
        .map(([params]) => params as { pageSize: number; search?: string })
        .filter((params) => params.pageSize !== 500);
      renderInventory();

      await user.click(await screen.findByRole('button', { name: 'Desactivar Leche entera' }));
      // While the change is on its way, the search changes and the list is read with it.
      await user.type(screen.getByLabelText('Buscar…'), 'leche');
      await waitFor(() => expect(listQueries().at(-1)).toEqual(expect.objectContaining({ search: 'leche', pageNumber: 1 })));
      expect(await screen.findByText('Leche entera')).toBeInTheDocument();
      expect(screen.queryByText('Azúcar')).not.toBeInTheDocument();
      const reads = listQueries().length;

      await act(async () => answer.resolve({ data: {} }));
      await waitFor(() => expect(listQueries()).toHaveLength(reads + 1));
      expect(listQueries().at(-1)).toEqual(expect.objectContaining({ search: 'leche', pageNumber: 1 }));
      await screen.findByText('Estado del insumo actualizado.');
      expect(screen.queryByText('Azúcar')).not.toBeInTheDocument();
    });

    it('does not send the change of a supply twice, and keeps the supplies to choose when they cannot be read again', async () => {
      const user = userEvent.setup();
      const answer = deferred();
      const relisted = deferred();
      let listReads = 0;
      let optionReads = 0;
      vi.mocked(inventoryApi.setSupplyActive).mockReturnValueOnce(answer.promise as never);
      vi.mocked(inventoryApi.getSupplies).mockImplementation(((params: { pageSize: number }) => {
        if (params.pageSize === 500) {
          optionReads += 1;
          return optionReads === 1 ? Promise.resolve(page([milk, sugar])) : Promise.reject(new Error('Network Error'));
        }
        listReads += 1;
        return listReads === 1 ? Promise.resolve(page([milk])) : relisted.promise;
      }) as never);
      renderInventory();

      await user.click(await screen.findByRole('button', { name: 'Desactivar Leche entera' }));
      expect(inventoryApi.setSupplyActive).toHaveBeenCalledWith('milk', false);
      expect(screen.getByRole('button', { name: 'Desactivar Leche entera' })).toBeDisabled();
      await act(async () => answer.resolve({ data: {} }));
      expect(await screen.findByText('Estado del insumo actualizado.')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /Leche entera$/ })).not.toBeInTheDocument();

      await act(async () => relisted.resolve(page([{ ...milk, isActive: false }])));
      expect(await screen.findByRole('button', { name: 'Activar Leche entera' })).toBeEnabled();

      // The supplies to choose could not be read again: it says so, and the dialogs still offer the ones read.
      expect(screen.getByRole('alert')).toHaveTextContent('No se pudieron cargar los insumos para elegir.');
      await user.click(tab('Entradas y salidas'));
      await user.click(screen.getByRole('button', { name: 'Registrar movimiento' }));
      const dialog = await screen.findByRole('dialog', { name: 'Registrar movimiento de inventario' });
      expect(within(within(dialog).getByLabelText('Insumo *')).getAllByRole('option').map((option) => option.getAttribute('value')))
        .toEqual(['', 'milk', 'sugar']);
    });

    it('says when the supplies to choose could not be read', async () => {
      vi.mocked(inventoryApi.getSupplies).mockImplementation(((params: { pageSize: number }) =>
        params.pageSize === 500 ? Promise.reject(new Error('Network Error')) : Promise.resolve(page([milk]))) as never);
      renderInventory();

      expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron cargar los insumos para elegir.');
    });
  });

  describe('Historial por insumo', () => {
    it('does not let a page asked before a filter changed replace the one asked after, nor say its error', async () => {
      const user = userEvent.setup();
      const forMilk = deferred();
      const atBar = deferred();
      const forAll = deferred();
      const lateFailure = deferred();
      vi.mocked(inventoryApi.getKardex)
        .mockResolvedValueOnce(page(KARDEX))
        .mockReturnValueOnce(forMilk.promise as never)
        .mockReturnValueOnce(atBar.promise as never)
        .mockReturnValueOnce(forAll.promise as never)
        .mockResolvedValueOnce(page([KARDEX[1]]))
        .mockReturnValueOnce(lateFailure.promise as never)
        .mockResolvedValueOnce(page([KARDEX[1]]));
      renderInventory();
      await user.click(tab('Historial por insumo'));
      await screen.findByText('INV-20261009-0001');

      await user.selectOptions(screen.getByLabelText('Insumo'), 'milk');
      await user.selectOptions(screen.getByLabelText('Ubicación'), 'bar');
      await act(async () => forMilk.resolve(page(KARDEX)));
      // The page at the bar is still on its way: the earlier one neither shows nor ends the reading.
      expect(screen.queryByText('INV-20261009-0001')).not.toBeInTheDocument();
      await act(async () => atBar.resolve(page([KARDEX[0]])));
      expect(await screen.findByText('INV-20261001-0003')).toBeInTheDocument();
      expect(screen.queryByText('INV-20261009-0001')).not.toBeInTheDocument();

      // One asked before that answers after the current one arrived does not replace it.
      await user.selectOptions(screen.getByLabelText('Insumo'), '');
      await user.selectOptions(screen.getByLabelText('Ubicación'), '');
      expect(await screen.findByText('INV-20261009-0001')).toBeInTheDocument();
      await act(async () => forAll.resolve(page(KARDEX)));
      expect(screen.queryByText('INV-20261001-0003')).not.toBeInTheDocument();

      // Nor does its error.
      await user.selectOptions(screen.getByLabelText('Insumo'), 'milk');
      await user.selectOptions(screen.getByLabelText('Insumo'), '');
      expect(await screen.findByText('INV-20261009-0001')).toBeInTheDocument();
      await act(async () => lateFailure.reject(refused('El historial no está disponible.')));
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(screen.getByText('INV-20261009-0001')).toBeInTheDocument();
    });
  });

  describe('Alertas', () => {
    it('reads the alerts each time the tab opens, with the counts of the server and each supply that is short', async () => {
      const user = userEvent.setup();
      renderInventory();
      await screen.findByText('Leche entera');
      expect(inventoryApi.getAlerts).not.toHaveBeenCalled();

      await user.click(tab('Alertas'));
      const card = (label: string) => screen.getByText(label, { selector: 'div.text-muted.small' }).nextElementSibling?.textContent;
      await waitFor(() => expect(card('Agotado')).toBe('1'));
      expect([card('Bajo stock crítico'), card('Bajo stock'), card('Ítems sin receta')]).toEqual(['2', '5', '4']);
      expect(cellsOf(screen.getByText('Leche entera'))).toEqual(['Leche entera', 'Barra', '0', '15', '15', 'Sin stock']);
      expect(cellsOf(screen.getByText('Café en grano'))).toEqual(['Café en grano', 'Almacén', '0.5', '2', '1.5', 'Bajo stock crítico']);
      expect(cellsOf(screen.getByText('Azúcar'))).toEqual(['Azúcar', 'Barra', '3', '5', '2', 'Bajo stock']);

      await user.type(screen.getByLabelText('Buscar…'), 'almac');
      expect(screen.queryByText('Leche entera')).not.toBeInTheDocument();
      expect(screen.getByText('Café en grano')).toBeInTheDocument();

      const reading = deferred();
      vi.mocked(inventoryApi.getAlerts).mockReturnValueOnce(reading.promise as never);
      await user.click(screen.getByRole('button', { name: 'Actualizar' }));
      expect(screen.getByRole('button', { name: 'Actualizar' })).toBeDisabled();
      await act(async () => reading.resolve({ data: { ...ALERTS, outOfStockCount: 3 } }));
      expect(card('Agotado')).toBe('3');
      expect(screen.getByRole('button', { name: 'Actualizar' })).toBeEnabled();

      await user.click(tab('Insumos'));
      await user.click(tab('Alertas'));
      await waitFor(() => expect(inventoryApi.getAlerts).toHaveBeenCalledTimes(3));
    });

    it('says the alerts could not be read instead of showing zeros, and keeps the ones read when reading them again fails', async () => {
      const user = userEvent.setup();
      vi.mocked(inventoryApi.getAlerts).mockRejectedValueOnce(new Error('Network Error'));
      renderInventory();
      await user.click(tab('Alertas'));

      expect(await screen.findByRole('alert')).toHaveTextContent('Error al cargar alertas de inventario.');
      const card = (label: string) => screen.getByText(label, { selector: 'div.text-muted.small' }).nextElementSibling?.textContent;
      expect(['Agotado', 'Bajo stock crítico', 'Bajo stock', 'Ítems sin receta'].map(card)).toEqual(['–', '–', '–', '–']);
      expect(screen.getByText('No se pudieron leer las alertas.')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Actualizar' }));
      await waitFor(() => expect(card('Agotado')).toBe('1'));

      vi.mocked(inventoryApi.getAlerts).mockRejectedValueOnce(new Error('Network Error'));
      await user.click(screen.getByRole('button', { name: 'Actualizar' }));
      expect(await screen.findByRole('alert')).toHaveTextContent('Error al cargar alertas de inventario.');
      expect(card('Agotado')).toBe('1');
      expect(screen.getByText('Café en grano')).toBeInTheDocument();
    });

    it('says there is nothing to restock when the server has no alerts', async () => {
      const user = userEvent.setup();
      vi.mocked(inventoryApi.getAlerts).mockResolvedValue({
        data: { outOfStockCount: 0, criticalLowStockCount: 0, lowStockCount: 0, missingRecipeProductCount: 0, items: [] },
      } as never);
      renderInventory();
      await user.click(tab('Alertas'));

      expect(await screen.findByText('No hay alertas de stock actualmente.')).toBeInTheDocument();
      expect(screen.getByText('Agotado', { selector: 'div.text-muted.small' }).nextElementSibling).toHaveTextContent('0');
    });
  });

  describe('Transferencias', () => {
    it('lists the transfers, filters them on the server, and opening one and coming back finds the list as it was', async () => {
      const user = userEvent.setup();
      setBusinessTimeZone('America/Cancun');
      answer({
        '/api/v1/inventory/transfers': () => apiPage([TRANSFER_ROW], 25),
        '/api/v1/inventory/transfers/{id}': () => TRANSFER,
      });
      renderInventory();
      await user.click(tab('Transferencias'));

      // Folio, origin and destination, items, value, and when (Cancún, UTC-5) with who.
      expect(cellsOf(await screen.findByText('TRF-20261021-0001')).slice(0, 5))
        .toEqual(['TRF-20261021-0001', 'Almacéna Barra', '2', '$195.00', expect.stringMatching(/^21\/10\/2026, 01:00.*encargado$/)]);
      const query = () => (request.mock.calls.filter(([, path]) => path === '/api/v1/inventory/transfers').at(-1)?.[2] as { query: unknown }).query;
      expect(query()).toEqual({ inventoryLocationId: undefined, search: undefined, pageNumber: 1, pageSize: 10 });

      // Each filter takes the list back to its first page.
      await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
      await waitFor(() => expect(query()).toEqual(expect.objectContaining({ pageNumber: 2 })));
      // Every location, the inactive ones included: their transfers are still history.
      expect(options(screen.getByLabelText('Ubicación'))).toEqual(['Todas', 'Barra', 'Almacén', 'Bodega vieja']);
      await user.selectOptions(screen.getByLabelText('Ubicación'), 'store');
      await waitFor(() => expect(query()).toEqual({ inventoryLocationId: 'store', search: undefined, pageNumber: 1, pageSize: 10 }));
      await user.type(screen.getByLabelText('Buscar por folio o ubicación'), 'TRF-2026');
      await waitFor(() => expect(query()).toEqual({ inventoryLocationId: 'store', search: 'TRF-2026', pageNumber: 1, pageSize: 10 }));

      await user.click(screen.getByRole('button', { name: 'Ver la transferencia TRF-20261021-0001' }));
      expect(await screen.findByRole('heading', { name: 'TRF-20261021-0001 · de Almacén a Barra' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Nueva transferencia' })).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Transferencias' }));
      expect(await screen.findByText('TRF-20261021-0001')).toBeInTheDocument();
      expect(screen.getByLabelText('Ubicación')).toHaveValue('store');
      expect(screen.getByLabelText('Buscar por folio o ubicación')).toHaveValue('TRF-2026');
      expect(screen.getByRole('button', { name: 'Nueva transferencia' })).toBeInTheDocument();
    });
  });

  describe('Conteos', () => {
    it('lists the counts, filters them on the server, and opening one and coming back finds the list as it was', async () => {
      const user = userEvent.setup();
      setBusinessTimeZone('America/Cancun');
      answer({
        '/api/v1/inventory/counts': () => apiPage([OPEN_COUNT, POSTED_COUNT], 25),
        '/api/v1/inventory/counts/{id}': () => COUNT,
        '/api/v1/inventory/counts/{id}/lines': () => ({ ...apiPage([]), revision: 1 }),
      });
      renderInventory();
      await user.click(tab('Conteos'));

      // Folio, location with its category, status, how many were counted, and when it started (Cancún) with who.
      expect(cellsOf(await screen.findByText('CNT-20261021-0001')).slice(0, 5))
        .toEqual(['CNT-20261021-0001', 'BarraBebidas', 'Abierto', '2 de 3', expect.stringMatching(/^21\/10\/2026, 01:00.*dueno$/)]);
      expect(cellsOf(screen.getByText('CNT-20261001-0004')).slice(0, 4)).toEqual(['CNT-20261001-0004', 'Almacén', 'Aplicado', '10 de 10']);
      // An open count is counted; the others are seen.
      expect(screen.getByRole('button', { name: 'Contar el conteo CNT-20261021-0001' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Ver el conteo CNT-20261001-0004' })).toBeInTheDocument();

      const query = () => (request.mock.calls.filter(([, path]) => path === '/api/v1/inventory/counts').at(-1)?.[2] as { query: unknown }).query;
      expect(query()).toEqual({ status: undefined, inventoryLocationId: undefined, search: undefined, pageNumber: 1, pageSize: 10 });
      await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
      await waitFor(() => expect(query()).toEqual(expect.objectContaining({ pageNumber: 2 })));
      expect(options(screen.getByLabelText('Estado'))).toEqual(['Todos', 'Abierto', 'Aplicado', 'Cancelado']);
      await user.selectOptions(screen.getByLabelText('Estado'), 'Aplicado');
      await waitFor(() => expect(query()).toEqual(expect.objectContaining({ status: InventoryCountStatus.Posted, pageNumber: 1 })));
      await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
      await user.selectOptions(screen.getByLabelText('Ubicación'), 'bar');
      await waitFor(() => expect(query()).toEqual({
        status: InventoryCountStatus.Posted, inventoryLocationId: 'bar', search: undefined, pageNumber: 1, pageSize: 10,
      }));
      await user.type(screen.getByLabelText('Buscar por folio o ubicación'), 'CNT');
      await waitFor(() => expect(query()).toEqual(expect.objectContaining({ search: 'CNT', pageNumber: 1 })));

      await user.click(screen.getByRole('button', { name: 'Contar el conteo CNT-20261021-0001' }));
      expect(await screen.findByRole('heading', { name: 'CNT-20261021-0001 · Barra' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Nuevo conteo' })).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Conteos' }));
      expect(await screen.findByText('CNT-20261021-0001')).toBeInTheDocument();
      expect(screen.getByLabelText('Estado')).toHaveValue(String(InventoryCountStatus.Posted));
      expect(screen.getByLabelText('Ubicación')).toHaveValue('bar');
      expect(screen.getByLabelText('Buscar por folio o ubicación')).toHaveValue('CNT');
      expect(screen.getByRole('button', { name: 'Nuevo conteo' })).toBeInTheDocument();
    });
  });

  describe.each([
    {
      name: 'Transferencias', path: '/api/v1/inventory/transfers', key: 'inventory-transfers', rows: () => apiPage([TRANSFER_ROW]),
      folio: 'TRF-20261021-0001', unread: 'No se pudieron leer las transferencias.',
      none: 'Todavía no hay transferencias. Haz una para llevar artículos de una ubicación a otra.',
      noMatch: 'Ninguna transferencia coincide con los filtros.', filter: 'Ubicación', value: 'bar',
    },
    {
      name: 'Conteos', path: '/api/v1/inventory/counts', key: 'inventory-counts', rows: () => apiPage([OPEN_COUNT]),
      folio: 'CNT-20261021-0001', unread: 'No se pudieron leer los conteos.',
      none: 'Todavía no hay conteos. Empieza uno para cuadrar lo que hay en el anaquel con lo que dice el sistema.',
      noMatch: 'Ningún conteo coincide con los filtros.', filter: 'Estado', value: 'Abierto',
    },
  ])('$name que no se pudieron leer', ({ name, path, key, rows, folio, unread, none, noMatch, filter, value }) => {
    it('says why and reads them again, and tells there are none from none that matches', async () => {
      const user = userEvent.setup();
      answer({ [path]: inTurn(() => Promise.reject(refused('El servicio de inventario no responde.')), () => apiPage([])) });
      renderInventory();
      await user.click(tab(name));

      expect(await screen.findByRole('alert')).toHaveTextContent('El servicio de inventario no responde.');
      // Not «there are none»: they could not be read.
      expect(screen.getByText(unread)).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Reintentar' }));

      expect(await screen.findByText(none)).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      await user.selectOptions(screen.getByLabelText(filter), value);
      expect(await screen.findByText(noMatch)).toBeInTheDocument();
    });

    it('keeps the ones read when reading them again fails, and its retry waits for the answer', async () => {
      const user = userEvent.setup();
      const retried = deferred();
      answer({ [path]: inTurn(rows, () => Promise.reject(refused('El servicio de inventario no responde.')), () => retried.promise) });
      const { client } = renderInventory();
      await user.click(tab(name));
      await screen.findByText(folio);

      // What changes them (a transfer made, a count posted) reads them again.
      await act(async () => client.invalidateQueries({ queryKey: [key] }));
      expect(await screen.findByRole('alert')).toHaveTextContent('El servicio de inventario no responde.');
      expect(screen.getByText(folio)).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Reintentar' }));
      expect(screen.getByRole('button', { name: 'Reintentar' })).toBeDisabled();
      await act(async () => retried.resolve(rows()));
      await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
      expect(screen.getByText(folio)).toBeInTheDocument();
    });
  });
});
