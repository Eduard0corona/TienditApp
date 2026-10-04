import { useCallback, useEffect, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { inventoryApi } from '../../api/inventoryApi';
import { productsApi } from '../../api/productsApi';
import type {
  CreateInventoryLocationRequest,
  CreateInventoryMovementRequest,
  CreateSupplyItemRequest,
  InventoryAlertsDto,
  InventoryKardexEntryDto,
  InventoryLocationDto,
  InventoryMovementDto,
  InventoryMovementReason,
  InventoryMovementType,
  InventoryStockDto,
  PaginatedList,
  ProductDto,
  SupplyItemDto,
  UnitType,
  UpsertProductRecipeRequest,
  UpdateInventoryLocationRequest,
  UpdateSupplyItemRequest,
} from '../../types';
import {
  InventoryMovementReason as InventoryMovementReasonConst,
  InventoryMovementType as InventoryMovementTypeConst,
} from '../../types';
import { getApiErrorMessage } from '../../api/errors';

type InventoryTab = 'supplies' | 'stocks' | 'movements' | 'locations' | 'recipes' | 'kardex' | 'alerts';

interface SupplyFormState {
  name: string;
  code: string;
  sku: string;
  barcode: string;
  description: string;
  brand: string;
  model: string;
  categoryName: string;
  unit: UnitType;
  defaultCost: string;
  minStock: string;
  maxStock: string;
  reorderPoint: string;
  trackBatches: boolean;
  trackExpiration: boolean;
  isPerishable: boolean;
  isCritical: boolean;
  taxRate: string;
  notes: string;
}

interface LocationFormState {
  name: string;
  code: string;
  description: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  state: string;
  country: string;
  postalCode: string;
  contactName: string;
  contactPhone: string;
  isDefault: boolean;
}

interface MovementLineFormState {
  supplyItemId: string;
  inventoryLocationId: string;
  quantity: string;
  unitCost: string;
  lotNumber: string;
  expirationDate: string;
  notes: string;
}

interface MovementFormState {
  movementType: InventoryMovementType;
  movementReason: InventoryMovementReason;
  movementDate: string;
  referenceNumber: string;
  externalReference: string;
  counterpartyName: string;
  documentNumber: string;
  notes: string;
  lines: MovementLineFormState[];
}

interface RecipeLineFormState {
  clientId: string;
  requirementId?: string;
  supplyItemId: string;
  inventoryLocationId: string;
  quantity: string;
  wastePercent: string;
  notes: string;
}

const SUPPLY_COLUMNS = [
  { header: 'Insumo' },
  { header: 'Clave / SKU' },
  { header: 'Unidad', className: 'text-center' },
  { header: 'Costo promedio', className: 'text-end' },
  { header: 'Existencia', className: 'text-end' },
  { header: 'Reorden', className: 'text-end' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

const STOCK_COLUMNS = [
  { header: 'Insumo' },
  { header: 'Ubicacion' },
  { header: 'Existencia', className: 'text-end' },
  { header: 'Reservado', className: 'text-end' },
  { header: 'Disponible', className: 'text-end' },
  { header: 'Ultimo costo', className: 'text-end' },
  { header: 'Estado', className: 'text-center' },
];

const MOVEMENT_COLUMNS = [
  { header: 'Folio' },
  { header: 'Tipo', className: 'text-center' },
  { header: 'Motivo', className: 'text-center' },
  { header: 'Fecha', className: 'text-center' },
  { header: 'Referencia' },
  { header: 'Monto', className: 'text-end' },
  { header: 'Acciones', className: 'text-center' },
];

const LOCATION_COLUMNS = [
  { header: 'Nombre' },
  { header: 'Codigo' },
  { header: 'Contacto' },
  { header: 'Ciudad / Estado' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

const RECIPE_COLUMNS = [
  { header: 'Insumo' },
  { header: 'Ubicacion' },
  { header: 'Cantidad', className: 'text-end' },
  { header: 'Merma %', className: 'text-end' },
  { header: 'Cantidad efectiva', className: 'text-end' },
  { header: 'Notas' },
  { header: 'Acciones', className: 'text-center' },
];

const KARDEX_COLUMNS = [
  { header: 'Fecha', className: 'text-center' },
  { header: 'Folio' },
  { header: 'Insumo' },
  { header: 'Ubicacion' },
  { header: 'Entrada', className: 'text-end' },
  { header: 'Salida', className: 'text-end' },
  { header: 'Saldo', className: 'text-end' },
  { header: 'Costo', className: 'text-end' },
  { header: 'Referencia' },
];

const UNIT_OPTIONS: Array<{ value: UnitType; label: string }> = [
  { value: 0, label: 'Unidad' },
  { value: 1, label: 'Paquete' },
  { value: 2, label: 'Caja' },
  { value: 10, label: 'Kg' },
  { value: 11, label: 'Gramo' },
  { value: 20, label: 'Litro' },
  { value: 21, label: 'Mililitro' },
  { value: 30, label: 'Porcion' },
  { value: 40, label: 'Metro' },
  { value: 50, label: 'Hora' },
];

const MOVEMENT_TYPE_OPTIONS: Array<{ value: InventoryMovementType; label: string }> = [
  { value: InventoryMovementTypeConst.Entry, label: 'Entrada' },
  { value: InventoryMovementTypeConst.Exit, label: 'Salida' },
  { value: InventoryMovementTypeConst.AdjustmentIncrease, label: 'Ajuste (+)' },
  { value: InventoryMovementTypeConst.AdjustmentDecrease, label: 'Ajuste (-)' },
  { value: InventoryMovementTypeConst.TransferIn, label: 'Transferencia entrada' },
  { value: InventoryMovementTypeConst.TransferOut, label: 'Transferencia salida' },
  { value: InventoryMovementTypeConst.ReturnFromProduction, label: 'Devolucion produccion' },
  { value: InventoryMovementTypeConst.ReturnToSupplier, label: 'Devolucion proveedor' },
];

const MOVEMENT_REASON_OPTIONS: Array<{ value: InventoryMovementReason; label: string }> = [
  { value: InventoryMovementReasonConst.Purchase, label: 'Compra' },
  { value: InventoryMovementReasonConst.Sale, label: 'Venta' },
  { value: InventoryMovementReasonConst.Production, label: 'Produccion' },
  { value: InventoryMovementReasonConst.Consumption, label: 'Consumo' },
  { value: InventoryMovementReasonConst.Waste, label: 'Merma' },
  { value: InventoryMovementReasonConst.Damaged, label: 'Danado' },
  { value: InventoryMovementReasonConst.InventoryCount, label: 'Conteo fisico' },
  { value: InventoryMovementReasonConst.InitialLoad, label: 'Carga inicial' },
  { value: InventoryMovementReasonConst.Return, label: 'Devolucion' },
  { value: InventoryMovementReasonConst.ManualAdjustment, label: 'Ajuste manual' },
  { value: InventoryMovementReasonConst.Other, label: 'Otro' },
];

const EMPTY_PAGINATED = {
  items: [],
  pageNumber: 1,
  totalPages: 0,
  totalCount: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

const EMPTY_ALERTS: InventoryAlertsDto = {
  lowStockCount: 0,
  criticalLowStockCount: 0,
  outOfStockCount: 0,
  missingRecipeProductCount: 0,
  items: [],
};

const buildEmptySupplyForm = (): SupplyFormState => ({
  name: '',
  code: '',
  sku: '',
  barcode: '',
  description: '',
  brand: '',
  model: '',
  categoryName: '',
  unit: 0,
  defaultCost: '0',
  minStock: '0',
  maxStock: '',
  reorderPoint: '0',
  trackBatches: false,
  trackExpiration: false,
  isPerishable: false,
  isCritical: false,
  taxRate: '',
  notes: '',
});

const buildEmptyLocationForm = (): LocationFormState => ({
  name: '',
  code: '',
  description: '',
  addressLine1: '',
  addressLine2: '',
  city: '',
  state: '',
  country: '',
  postalCode: '',
  contactName: '',
  contactPhone: '',
  isDefault: false,
});

const buildEmptyMovementLineForm = (): MovementLineFormState => ({
  supplyItemId: '',
  inventoryLocationId: '',
  quantity: '1',
  unitCost: '',
  lotNumber: '',
  expirationDate: '',
  notes: '',
});

const buildEmptyRecipeLineForm = (): RecipeLineFormState => ({
  clientId: (globalThis.crypto && 'randomUUID' in globalThis.crypto)
    ? globalThis.crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`,
  supplyItemId: '',
  inventoryLocationId: '',
  quantity: '1',
  wastePercent: '0',
  notes: '',
});

const toDateTimeLocalValue = (value: Date) => {
  const year = value.getFullYear();
  const month = `${value.getMonth() + 1}`.padStart(2, '0');
  const day = `${value.getDate()}`.padStart(2, '0');
  const hour = `${value.getHours()}`.padStart(2, '0');
  const minute = `${value.getMinutes()}`.padStart(2, '0');
  return `${year}-${month}-${day}T${hour}:${minute}`;
};

const buildEmptyMovementForm = (): MovementFormState => ({
  movementType: InventoryMovementTypeConst.Entry,
  movementReason: InventoryMovementReasonConst.Purchase,
  movementDate: toDateTimeLocalValue(new Date()),
  referenceNumber: '',
  externalReference: '',
  counterpartyName: '',
  documentNumber: '',
  notes: '',
  lines: [buildEmptyMovementLineForm()],
});

const getUnitLabel = (unit: UnitType) => {
  const found = UNIT_OPTIONS.find((x) => x.value === unit);
  return found?.label ?? String(unit);
};

const getMovementTypeLabel = (movementType: InventoryMovementType) =>
  MOVEMENT_TYPE_OPTIONS.find((x) => x.value === movementType)?.label ?? String(movementType);

const getMovementReasonLabel = (movementReason: InventoryMovementReason) =>
  MOVEMENT_REASON_OPTIONS.find((x) => x.value === movementReason)?.label ?? String(movementReason);

const formatDateTime = (value?: string) => {
  if (!value) return '-';
  return new Date(value).toLocaleString('es-MX', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
};

const formatDate = (value?: string) => {
  if (!value) return '-';
  return new Date(value).toLocaleDateString('es-MX');
};

export default function InventoryManagement() {
  const [activeTab, setActiveTab] = useState<InventoryTab>('supplies');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [suppliesData, setSuppliesData] = useState<PaginatedList<SupplyItemDto>>(EMPTY_PAGINATED);
  const [suppliesLoading, setSuppliesLoading] = useState(true);
  const [suppliesPageNumber, setSuppliesPageNumber] = useState(1);
  const [suppliesPageSize, setSuppliesPageSize] = useState(10);
  const [suppliesFilterActive, setSuppliesFilterActive] = useState('');
  const [suppliesLowStockOnly, setSuppliesLowStockOnly] = useState(false);

  const [stocksData, setStocksData] = useState<PaginatedList<InventoryStockDto>>(EMPTY_PAGINATED);
  const [stocksLoading, setStocksLoading] = useState(true);
  const [stocksPageNumber, setStocksPageNumber] = useState(1);
  const [stocksPageSize, setStocksPageSize] = useState(10);
  const [stocksLocationFilter, setStocksLocationFilter] = useState('');
  const [stocksLowStockOnly, setStocksLowStockOnly] = useState(false);

  const [movementsData, setMovementsData] = useState<PaginatedList<InventoryMovementDto>>(EMPTY_PAGINATED);
  const [movementsLoading, setMovementsLoading] = useState(true);
  const [movementsPageNumber, setMovementsPageNumber] = useState(1);
  const [movementsPageSize, setMovementsPageSize] = useState(10);
  const [movementsTypeFilter, setMovementsTypeFilter] = useState('');
  const [movementsReasonFilter, setMovementsReasonFilter] = useState('');
  const [movementsFromDate, setMovementsFromDate] = useState('');
  const [movementsToDate, setMovementsToDate] = useState('');

  const [recipeProducts, setRecipeProducts] = useState<ProductDto[]>([]);
  const [recipeProductId, setRecipeProductId] = useState('');
  const [recipeProductName, setRecipeProductName] = useState('');
  const [recipeProductIsAvailable, setRecipeProductIsAvailable] = useState(false);
  const [recipeLines, setRecipeLines] = useState<RecipeLineFormState[]>([]);
  const [recipeLoading, setRecipeLoading] = useState(false);
  const [recipeSaving, setRecipeSaving] = useState(false);

  const [kardexData, setKardexData] = useState<PaginatedList<InventoryKardexEntryDto>>(EMPTY_PAGINATED);
  const [kardexLoading, setKardexLoading] = useState(false);
  const [kardexPageNumber, setKardexPageNumber] = useState(1);
  const [kardexPageSize, setKardexPageSize] = useState(20);
  const [kardexSupplyFilter, setKardexSupplyFilter] = useState('');
  const [kardexLocationFilter, setKardexLocationFilter] = useState('');
  const [kardexFromDate, setKardexFromDate] = useState('');
  const [kardexToDate, setKardexToDate] = useState('');

  const [alertsData, setAlertsData] = useState<InventoryAlertsDto>(EMPTY_ALERTS);
  const [alertsLoading, setAlertsLoading] = useState(false);

  const [locations, setLocations] = useState<InventoryLocationDto[]>([]);
  const [locationsLoading, setLocationsLoading] = useState(true);
  const [supplyOptions, setSupplyOptions] = useState<SupplyItemDto[]>([]);

  const [showSupplyModal, setShowSupplyModal] = useState(false);
  const [editingSupply, setEditingSupply] = useState<SupplyItemDto | null>(null);
  const [savingSupply, setSavingSupply] = useState(false);
  const [supplyForm, setSupplyForm] = useState<SupplyFormState>(buildEmptySupplyForm());

  const [showLocationModal, setShowLocationModal] = useState(false);
  const [editingLocation, setEditingLocation] = useState<InventoryLocationDto | null>(null);
  const [savingLocation, setSavingLocation] = useState(false);
  const [locationForm, setLocationForm] = useState<LocationFormState>(buildEmptyLocationForm());

  const [showMovementModal, setShowMovementModal] = useState(false);
  const [savingMovement, setSavingMovement] = useState(false);
  const [movementForm, setMovementForm] = useState<MovementFormState>(buildEmptyMovementForm());

  const [showMovementDetailModal, setShowMovementDetailModal] = useState(false);
  const [movementDetailLoading, setMovementDetailLoading] = useState(false);
  const [selectedMovement, setSelectedMovement] = useState<InventoryMovementDto | null>(null);

  const activeLocations = useMemo(
    () => locations.filter((location) => location.isActive),
    [locations],
  );

  const loadLocations = useCallback(async () => {
    setLocationsLoading(true);
    try {
      const { data } = await inventoryApi.getLocations();
      setLocations(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar ubicaciones.'));
    } finally {
      setLocationsLoading(false);
    }
  }, []);

  const loadSupplyOptions = useCallback(async () => {
    try {
      const { data } = await inventoryApi.getSupplies({
        isActive: true,
        pageNumber: 1,
        pageSize: 500,
      });
      setSupplyOptions(data.items);
    } catch {
      setSupplyOptions([]);
    }
  }, []);

  const loadRecipeProducts = useCallback(async () => {
    try {
      const [productsResponse, servicesResponse] = await Promise.all([
        productsApi.getAll({ itemType: 'Product', pageNumber: 1, pageSize: 500 }),
        productsApi.getAll({ itemType: 'Service', pageNumber: 1, pageSize: 500 }),
      ]);

      const merged = [...productsResponse.data.items, ...servicesResponse.data.items];
      const deduped = Array.from(new Map(merged.map((item) => [item.id, item])).values())
        .sort((a, b) => a.name.localeCompare(b.name));

      setRecipeProducts(deduped);
      if (!recipeProductId && deduped.length > 0)
        setRecipeProductId(deduped[0].id);
    } catch {
      setRecipeProducts([]);
    }
  }, [recipeProductId]);

  const loadRecipe = useCallback(async (productId: string) => {
    if (!productId) {
      setRecipeLines([]);
      setRecipeProductName('');
      setRecipeProductIsAvailable(false);
      return;
    }

    setRecipeLoading(true);
    try {
      const { data } = await inventoryApi.getProductRecipe(productId);
      setRecipeProductName(data.productName);
      setRecipeProductIsAvailable(data.productIsAvailable);
      setRecipeLines(
        data.lines.map((line) => ({
          clientId: line.requirementId || buildEmptyRecipeLineForm().clientId,
          requirementId: line.requirementId,
          supplyItemId: line.supplyItemId,
          inventoryLocationId: line.inventoryLocationId ?? '',
          quantity: line.quantity.toString(),
          wastePercent: line.wastePercent.toString(),
          notes: line.notes ?? '',
        })),
      );
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar la receta del producto.'));
      setRecipeLines([]);
    } finally {
      setRecipeLoading(false);
    }
  }, []);

  const loadKardex = useCallback(async () => {
    setKardexLoading(true);
    try {
      const params: {
        supplyItemId?: string;
        inventoryLocationId?: string;
        fromDate?: string;
        toDate?: string;
        pageNumber: number;
        pageSize: number;
      } = {
        pageNumber: kardexPageNumber,
        pageSize: kardexPageSize,
      };

      if (kardexSupplyFilter) params.supplyItemId = kardexSupplyFilter;
      if (kardexLocationFilter) params.inventoryLocationId = kardexLocationFilter;
      if (kardexFromDate) params.fromDate = new Date(`${kardexFromDate}T00:00:00`).toISOString();
      if (kardexToDate) params.toDate = new Date(`${kardexToDate}T23:59:59`).toISOString();

      const { data } = await inventoryApi.getKardex(params);
      setKardexData(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar kardex.'));
    } finally {
      setKardexLoading(false);
    }
  }, [kardexFromDate, kardexLocationFilter, kardexPageNumber, kardexPageSize, kardexSupplyFilter, kardexToDate]);

  const loadAlerts = useCallback(async () => {
    setAlertsLoading(true);
    try {
      const { data } = await inventoryApi.getAlerts();
      setAlertsData(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar alertas de inventario.'));
    } finally {
      setAlertsLoading(false);
    }
  }, []);

  const loadSupplies = useCallback(async () => {
    setSuppliesLoading(true);
    try {
      const params: {
        isActive?: boolean;
        lowStockOnly?: boolean;
        pageNumber: number;
        pageSize: number;
      } = {
        pageNumber: suppliesPageNumber,
        pageSize: suppliesPageSize,
      };

      if (suppliesFilterActive !== '') params.isActive = suppliesFilterActive === 'true';
      if (suppliesLowStockOnly) params.lowStockOnly = true;

      const { data } = await inventoryApi.getSupplies(params);
      setSuppliesData(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar insumos.'));
    } finally {
      setSuppliesLoading(false);
    }
  }, [suppliesFilterActive, suppliesLowStockOnly, suppliesPageNumber, suppliesPageSize]);

  const loadStocks = useCallback(async () => {
    setStocksLoading(true);
    try {
      const params: {
        inventoryLocationId?: string;
        lowStockOnly?: boolean;
        pageNumber: number;
        pageSize: number;
      } = {
        pageNumber: stocksPageNumber,
        pageSize: stocksPageSize,
      };

      if (stocksLocationFilter) params.inventoryLocationId = stocksLocationFilter;
      if (stocksLowStockOnly) params.lowStockOnly = true;

      const { data } = await inventoryApi.getStocks(params);
      setStocksData(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar existencias.'));
    } finally {
      setStocksLoading(false);
    }
  }, [stocksLocationFilter, stocksLowStockOnly, stocksPageNumber, stocksPageSize]);

  const loadMovements = useCallback(async () => {
    setMovementsLoading(true);
    try {
      const params: {
        movementType?: InventoryMovementType;
        movementReason?: InventoryMovementReason;
        fromDate?: string;
        toDate?: string;
        pageNumber: number;
        pageSize: number;
      } = {
        pageNumber: movementsPageNumber,
        pageSize: movementsPageSize,
      };

      if (movementsTypeFilter !== '') params.movementType = Number(movementsTypeFilter) as InventoryMovementType;
      if (movementsReasonFilter !== '') params.movementReason = Number(movementsReasonFilter) as InventoryMovementReason;
      if (movementsFromDate) params.fromDate = new Date(`${movementsFromDate}T00:00:00`).toISOString();
      if (movementsToDate) params.toDate = new Date(`${movementsToDate}T23:59:59`).toISOString();

      const { data } = await inventoryApi.getMovements(params);
      setMovementsData(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar movimientos.'));
    } finally {
      setMovementsLoading(false);
    }
  }, [movementsFromDate, movementsPageNumber, movementsPageSize, movementsReasonFilter, movementsToDate, movementsTypeFilter]);

  useEffect(() => {
    loadLocations();
    loadSupplyOptions();
    loadRecipeProducts();
  }, [loadLocations, loadSupplyOptions, loadRecipeProducts]);

  useEffect(() => {
    loadSupplies();
  }, [loadSupplies]);

  useEffect(() => {
    loadStocks();
  }, [loadStocks]);

  useEffect(() => {
    loadMovements();
  }, [loadMovements]);

  useEffect(() => {
    if (!recipeProductId) return;
    loadRecipe(recipeProductId);
  }, [loadRecipe, recipeProductId]);

  useEffect(() => {
    if (activeTab !== 'kardex') return;
    loadKardex();
  }, [activeTab, loadKardex]);

  useEffect(() => {
    if (activeTab !== 'alerts') return;
    loadAlerts();
  }, [activeTab, loadAlerts]);

  const openSupplyModalForCreate = () => {
    setEditingSupply(null);
    setSupplyForm(buildEmptySupplyForm());
    setShowSupplyModal(true);
  };

  const openSupplyModalForEdit = (supply: SupplyItemDto) => {
    setEditingSupply(supply);
    setSupplyForm({
      name: supply.name,
      code: supply.code ?? '',
      sku: supply.sku ?? '',
      barcode: supply.barcode ?? '',
      description: supply.description ?? '',
      brand: supply.brand ?? '',
      model: supply.model ?? '',
      categoryName: supply.categoryName ?? '',
      unit: supply.unit,
      defaultCost: supply.defaultCost.toString(),
      minStock: supply.minStock.toString(),
      maxStock: supply.maxStock?.toString() ?? '',
      reorderPoint: supply.reorderPoint.toString(),
      trackBatches: supply.trackBatches,
      trackExpiration: supply.trackExpiration,
      isPerishable: supply.isPerishable,
      isCritical: supply.isCritical,
      taxRate: supply.taxRate?.toString() ?? '',
      notes: supply.notes ?? '',
    });
    setShowSupplyModal(true);
  };

  const submitSupply = async () => {
    setError('');
    setSuccess('');

    const name = supplyForm.name.trim();
    if (!name) {
      setError('El nombre del insumo es obligatorio.');
      return;
    }

    const parsedDefaultCost = Number(supplyForm.defaultCost || '0');
    const parsedMinStock = Number(supplyForm.minStock || '0');
    const parsedReorderPoint = Number(supplyForm.reorderPoint || '0');
    const parsedMaxStock = supplyForm.maxStock === '' ? undefined : Number(supplyForm.maxStock);
    const parsedTaxRate = supplyForm.taxRate === '' ? undefined : Number(supplyForm.taxRate);

    if (Number.isNaN(parsedDefaultCost) || parsedDefaultCost < 0) {
      setError('Costo por defecto invalido.');
      return;
    }

    if (Number.isNaN(parsedMinStock) || parsedMinStock < 0) {
      setError('Stock minimo invalido.');
      return;
    }

    if (Number.isNaN(parsedReorderPoint) || parsedReorderPoint < 0) {
      setError('Punto de reorden invalido.');
      return;
    }

    if (parsedMaxStock !== undefined && (Number.isNaN(parsedMaxStock) || parsedMaxStock < parsedMinStock)) {
      setError('El stock maximo debe ser mayor o igual al stock minimo.');
      return;
    }

    if (parsedTaxRate !== undefined && (Number.isNaN(parsedTaxRate) || parsedTaxRate < 0 || parsedTaxRate > 100)) {
      setError('La tasa de impuesto debe estar entre 0 y 100.');
      return;
    }

    const payload: CreateSupplyItemRequest = {
      name,
      code: supplyForm.code.trim() || undefined,
      sku: supplyForm.sku.trim() || undefined,
      barcode: supplyForm.barcode.trim() || undefined,
      description: supplyForm.description.trim() || undefined,
      brand: supplyForm.brand.trim() || undefined,
      model: supplyForm.model.trim() || undefined,
      categoryName: supplyForm.categoryName.trim() || undefined,
      unit: Number(supplyForm.unit) as UnitType,
      defaultCost: parsedDefaultCost,
      minStock: parsedMinStock,
      maxStock: parsedMaxStock,
      reorderPoint: parsedReorderPoint,
      trackBatches: supplyForm.trackBatches,
      trackExpiration: supplyForm.trackExpiration,
      isPerishable: supplyForm.isPerishable,
      isCritical: supplyForm.isCritical,
      taxRate: parsedTaxRate,
      notes: supplyForm.notes.trim() || undefined,
    };

    try {
      setSavingSupply(true);
      if (editingSupply) {
        const updatePayload: UpdateSupplyItemRequest = { ...payload, id: editingSupply.id };
        await inventoryApi.updateSupply(editingSupply.id, updatePayload);
        setSuccess('Insumo actualizado correctamente.');
      } else {
        await inventoryApi.createSupply(payload);
        setSuccess('Insumo creado correctamente.');
      }

      setShowSupplyModal(false);
      setEditingSupply(null);
      await Promise.all([loadSupplies(), loadSupplyOptions(), loadStocks()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al guardar el insumo.'));
    } finally {
      setSavingSupply(false);
    }
  };

  const toggleSupplyActive = async (supply: SupplyItemDto) => {
    setError('');
    setSuccess('');
    try {
      await inventoryApi.setSupplyActive(supply.id, !supply.isActive);
      setSuccess('Estado del insumo actualizado.');
      await Promise.all([loadSupplies(), loadSupplyOptions(), loadStocks()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo actualizar el estado del insumo.'));
    }
  };

  const openLocationModalForCreate = () => {
    setEditingLocation(null);
    setLocationForm(buildEmptyLocationForm());
    setShowLocationModal(true);
  };

  const openLocationModalForEdit = (location: InventoryLocationDto) => {
    setEditingLocation(location);
    setLocationForm({
      name: location.name,
      code: location.code,
      description: location.description ?? '',
      addressLine1: location.addressLine1 ?? '',
      addressLine2: location.addressLine2 ?? '',
      city: location.city ?? '',
      state: location.state ?? '',
      country: location.country ?? '',
      postalCode: location.postalCode ?? '',
      contactName: location.contactName ?? '',
      contactPhone: location.contactPhone ?? '',
      isDefault: location.isDefault,
    });
    setShowLocationModal(true);
  };

  const submitLocation = async () => {
    setError('');
    setSuccess('');

    const name = locationForm.name.trim();
    const code = locationForm.code.trim();
    if (!name || !code) {
      setError('Nombre y codigo de ubicacion son obligatorios.');
      return;
    }

    const payload: CreateInventoryLocationRequest = {
      name,
      code,
      description: locationForm.description.trim() || undefined,
      addressLine1: locationForm.addressLine1.trim() || undefined,
      addressLine2: locationForm.addressLine2.trim() || undefined,
      city: locationForm.city.trim() || undefined,
      state: locationForm.state.trim() || undefined,
      country: locationForm.country.trim() || undefined,
      postalCode: locationForm.postalCode.trim() || undefined,
      contactName: locationForm.contactName.trim() || undefined,
      contactPhone: locationForm.contactPhone.trim() || undefined,
      isDefault: locationForm.isDefault,
    };

    try {
      setSavingLocation(true);
      if (editingLocation) {
        const updatePayload: UpdateInventoryLocationRequest = { ...payload, id: editingLocation.id };
        await inventoryApi.updateLocation(editingLocation.id, updatePayload);
        setSuccess('Ubicacion actualizada correctamente.');
      } else {
        await inventoryApi.createLocation(payload);
        setSuccess('Ubicacion creada correctamente.');
      }

      setShowLocationModal(false);
      setEditingLocation(null);
      await Promise.all([loadLocations(), loadStocks(), loadMovements()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al guardar la ubicacion.'));
    } finally {
      setSavingLocation(false);
    }
  };

  const toggleLocationActive = async (location: InventoryLocationDto) => {
    setError('');
    setSuccess('');
    try {
      await inventoryApi.setLocationActive(location.id, !location.isActive);
      setSuccess('Estado de la ubicacion actualizado.');
      await Promise.all([loadLocations(), loadStocks()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo actualizar el estado de la ubicacion.'));
    }
  };

  const openMovementModal = () => {
    setMovementForm(buildEmptyMovementForm());
    setShowMovementModal(true);
  };

  const updateMovementLine = (index: number, patch: Partial<MovementLineFormState>) => {
    setMovementForm((current) => ({
      ...current,
      lines: current.lines.map((line, lineIndex) => (lineIndex === index ? { ...line, ...patch } : line)),
    }));
  };

  const addMovementLine = () => {
    setMovementForm((current) => ({
      ...current,
      lines: [...current.lines, buildEmptyMovementLineForm()],
    }));
  };

  const removeMovementLine = (index: number) => {
    setMovementForm((current) => {
      if (current.lines.length <= 1) return current;
      return {
        ...current,
        lines: current.lines.filter((_, lineIndex) => lineIndex !== index),
      };
    });
  };

  const submitMovement = async () => {
    setError('');
    setSuccess('');

    if (movementForm.lines.length === 0) {
      setError('Debes agregar al menos un detalle.');
      return;
    }

    for (let index = 0; index < movementForm.lines.length; index += 1) {
      const line = movementForm.lines[index];
      const quantity = Number(line.quantity);
      if (!line.supplyItemId || !line.inventoryLocationId) {
        setError(`Linea ${index + 1}: insumo y ubicacion son obligatorios.`);
        return;
      }
      if (Number.isNaN(quantity) || quantity <= 0) {
        setError(`Linea ${index + 1}: cantidad invalida.`);
        return;
      }
      if (line.unitCost && (Number.isNaN(Number(line.unitCost)) || Number(line.unitCost) < 0)) {
        setError(`Linea ${index + 1}: costo unitario invalido.`);
        return;
      }
    }

    const payload: CreateInventoryMovementRequest = {
      movementType: movementForm.movementType,
      movementReason: movementForm.movementReason,
      movementDate: movementForm.movementDate ? new Date(movementForm.movementDate).toISOString() : undefined,
      referenceNumber: movementForm.referenceNumber.trim() || undefined,
      externalReference: movementForm.externalReference.trim() || undefined,
      counterpartyName: movementForm.counterpartyName.trim() || undefined,
      documentNumber: movementForm.documentNumber.trim() || undefined,
      notes: movementForm.notes.trim() || undefined,
      lines: movementForm.lines.map((line) => ({
        supplyItemId: line.supplyItemId,
        inventoryLocationId: line.inventoryLocationId,
        quantity: Number(line.quantity),
        unitCost: line.unitCost ? Number(line.unitCost) : undefined,
        lotNumber: line.lotNumber.trim() || undefined,
        expirationDate: line.expirationDate ? new Date(`${line.expirationDate}T00:00:00`).toISOString() : undefined,
        notes: line.notes.trim() || undefined,
      })),
    };

    try {
      setSavingMovement(true);
      await inventoryApi.createMovement(payload);
      setSuccess('Movimiento registrado correctamente.');
      setShowMovementModal(false);
      await Promise.all([loadMovements(), loadStocks(), loadSupplies(), loadSupplyOptions()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al registrar movimiento.'));
    } finally {
      setSavingMovement(false);
    }
  };

  const openMovementDetail = async (movementId: string) => {
    setError('');
    setMovementDetailLoading(true);
    setShowMovementDetailModal(true);
    setSelectedMovement(null);
    try {
      const { data } = await inventoryApi.getMovementById(movementId);
      setSelectedMovement(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar el detalle del movimiento.'));
      setShowMovementDetailModal(false);
    } finally {
      setMovementDetailLoading(false);
    }
  };

  const updateRecipeLine = (index: number, patch: Partial<RecipeLineFormState>) => {
    setRecipeLines((current) =>
      current.map((line, lineIndex) => (lineIndex === index ? { ...line, ...patch } : line)),
    );
  };

  const addRecipeLine = () => {
    setRecipeLines((current) => [...current, buildEmptyRecipeLineForm()]);
  };

  const removeRecipeLine = (index: number) => {
    setRecipeLines((current) => current.filter((_, lineIndex) => lineIndex !== index));
  };

  const selectRecipeProduct = (productId: string) => {
    setRecipeProductId(productId);
    setRecipeLines([]);
    setSuccess('');
    setError('');
  };

  const submitRecipe = async () => {
    setError('');
    setSuccess('');

    if (!recipeProductId) {
      setError('Selecciona un producto o servicio.');
      return;
    }

    for (let index = 0; index < recipeLines.length; index += 1) {
      const line = recipeLines[index];
      const quantity = Number(line.quantity);
      const wastePercent = Number(line.wastePercent);

      if (!line.supplyItemId) {
        setError(`Linea ${index + 1}: el insumo es obligatorio.`);
        return;
      }

      if (Number.isNaN(quantity) || quantity <= 0) {
        setError(`Linea ${index + 1}: cantidad invalida.`);
        return;
      }

      if (Number.isNaN(wastePercent) || wastePercent < 0 || wastePercent > 100) {
        setError(`Linea ${index + 1}: merma invalida (0 a 100).`);
        return;
      }
    }

    const payload: UpsertProductRecipeRequest = {
      lines: recipeLines.map((line) => ({
        requirementId: line.requirementId,
        supplyItemId: line.supplyItemId,
        inventoryLocationId: line.inventoryLocationId || undefined,
        quantity: Number(line.quantity),
        wastePercent: Number(line.wastePercent),
        notes: line.notes.trim() || undefined,
      })),
    };

    try {
      setRecipeSaving(true);
      const { data } = await inventoryApi.upsertProductRecipe(recipeProductId, payload);
      setRecipeProductName(data.productName);
      setRecipeProductIsAvailable(data.productIsAvailable);
      setRecipeLines(
        data.lines.map((line) => ({
          clientId: line.requirementId || buildEmptyRecipeLineForm().clientId,
          requirementId: line.requirementId,
          supplyItemId: line.supplyItemId,
          inventoryLocationId: line.inventoryLocationId ?? '',
          quantity: line.quantity.toString(),
          wastePercent: line.wastePercent.toString(),
          notes: line.notes ?? '',
        })),
      );
      setSuccess('Receta guardada correctamente.');
      await Promise.all([loadAlerts(), loadKardex()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al guardar la receta.'));
    } finally {
      setRecipeSaving(false);
    }
  };

  return (
    <>
      <PageTitle title="Inventario" subtitle="Operacion" />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError('')}></button>
        </div>
      )}

      {success && (
        <div className="alert alert-success alert-dismissible fade show">
          {success}
          <button type="button" className="btn-close" onClick={() => setSuccess('')}></button>
        </div>
      )}

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              <div className="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3">
                <ul className="nav nav-tabs nav-bordered mb-0">
                  <li className="nav-item">
                    <button
                      type="button"
                      className={`nav-link ${activeTab === 'supplies' ? 'active' : ''}`}
                      onClick={() => setActiveTab('supplies')}
                    >
                      Insumos
                    </button>
                  </li>
                  <li className="nav-item">
                    <button
                      type="button"
                      className={`nav-link ${activeTab === 'stocks' ? 'active' : ''}`}
                      onClick={() => setActiveTab('stocks')}
                    >
                      Existencias
                    </button>
                  </li>
                  <li className="nav-item">
                    <button
                      type="button"
                      className={`nav-link ${activeTab === 'movements' ? 'active' : ''}`}
                      onClick={() => setActiveTab('movements')}
                    >
                      Entradas y salidas
                    </button>
                  </li>
                  <li className="nav-item">
                    <button
                      type="button"
                      className={`nav-link ${activeTab === 'locations' ? 'active' : ''}`}
                      onClick={() => setActiveTab('locations')}
                    >
                      Ubicaciones
                    </button>
                  </li>
                  <li className="nav-item">
                    <button
                      type="button"
                      className={`nav-link ${activeTab === 'recipes' ? 'active' : ''}`}
                      onClick={() => setActiveTab('recipes')}
                    >
                      Recetas
                    </button>
                  </li>
                  <li className="nav-item">
                    <button
                      type="button"
                      className={`nav-link ${activeTab === 'kardex' ? 'active' : ''}`}
                      onClick={() => setActiveTab('kardex')}
                    >
                      Kardex
                    </button>
                  </li>
                  <li className="nav-item">
                    <button
                      type="button"
                      className={`nav-link ${activeTab === 'alerts' ? 'active' : ''}`}
                      onClick={() => setActiveTab('alerts')}
                    >
                      Alertas
                    </button>
                  </li>
                </ul>

                {activeTab === 'supplies' && (
                  <button className="btn btn-primary btn-sm" onClick={openSupplyModalForCreate}>
                    <i className="ri-add-line me-1"></i>Nuevo insumo
                  </button>
                )}
                {activeTab === 'movements' && (
                  <button className="btn btn-primary btn-sm" onClick={openMovementModal}>
                    <i className="ri-exchange-funds-line me-1"></i>Registrar movimiento
                  </button>
                )}
                {activeTab === 'locations' && (
                  <button className="btn btn-primary btn-sm" onClick={openLocationModalForCreate}>
                    <i className="ri-add-line me-1"></i>Nueva ubicacion
                  </button>
                )}
                {activeTab === 'recipes' && (
                  <div className="d-flex align-items-center gap-2">
                    <button className="btn btn-light btn-sm" onClick={() => recipeProductId && loadRecipe(recipeProductId)}>
                      <i className="ri-refresh-line me-1"></i>Recargar
                    </button>
                    <button className="btn btn-primary btn-sm" onClick={submitRecipe} disabled={recipeSaving || recipeLoading}>
                      <i className="ri-save-line me-1"></i>{recipeSaving ? 'Guardando...' : 'Guardar receta'}
                    </button>
                  </div>
                )}
                {activeTab === 'kardex' && (
                  <button className="btn btn-light btn-sm" onClick={loadKardex} disabled={kardexLoading}>
                    <i className="ri-refresh-line me-1"></i>Actualizar
                  </button>
                )}
                {activeTab === 'alerts' && (
                  <button className="btn btn-light btn-sm" onClick={loadAlerts} disabled={alertsLoading}>
                    <i className="ri-refresh-line me-1"></i>Actualizar
                  </button>
                )}
              </div>

              {activeTab === 'supplies' && (
                <>
                  <div className="row mb-2">
                    <div className="col-md-3 mb-2">
                      <label className="form-label">Estado</label>
                      <select
                        className="form-select form-select-sm"
                        value={suppliesFilterActive}
                        onChange={(e) => {
                          setSuppliesFilterActive(e.target.value);
                          setSuppliesPageNumber(1);
                        }}
                      >
                        <option value="">Todos</option>
                        <option value="true">Activos</option>
                        <option value="false">Inactivos</option>
                      </select>
                    </div>
                    <div className="col-md-3 mb-2 d-flex align-items-end">
                      <div className="form-check form-switch">
                        <input
                          className="form-check-input"
                          type="checkbox"
                          checked={suppliesLowStockOnly}
                          onChange={(e) => {
                            setSuppliesLowStockOnly(e.target.checked);
                            setSuppliesPageNumber(1);
                          }}
                        />
                        <label className="form-check-label">Solo bajo stock</label>
                      </div>
                    </div>
                  </div>

                  <DataTable
                    data={suppliesData.items}
                    columns={SUPPLY_COLUMNS}
                    keyExtractor={(item) => item.id}
                    loading={suppliesLoading}
                    emptyMessage="No hay insumos registrados."
                    searchFilter={(item, search) =>
                      item.name.toLowerCase().includes(search) ||
                      (item.code || '').toLowerCase().includes(search) ||
                      (item.sku || '').toLowerCase().includes(search) ||
                      (item.barcode || '').toLowerCase().includes(search)
                    }
                    serverSide={{
                      totalCount: suppliesData.totalCount,
                      page: suppliesPageNumber,
                      pageSize: suppliesPageSize,
                      onPageChange: setSuppliesPageNumber,
                      onPageSizeChange: (size) => {
                        setSuppliesPageSize(size);
                        setSuppliesPageNumber(1);
                      },
                    }}
                    renderRow={(item) => (
                      <>
                        <td>
                          <div className="fw-semibold">{item.name}</div>
                          <div className="text-muted small">
                            {item.categoryName || '-'}
                            {item.isCritical && <span className="badge bg-danger ms-1">Critico</span>}
                          </div>
                        </td>
                        <td>
                          <div>{item.code || '-'}</div>
                          <div className="text-muted small">{item.sku || '-'}</div>
                        </td>
                        <td className="text-center">{getUnitLabel(item.unit)}</td>
                        <td className="text-end">${item.averageCost.toFixed(2)}</td>
                        <td className="text-end">
                          {item.totalOnHand.toFixed(2)}
                          {item.isLowStock && (
                            <span className="badge bg-warning text-dark ms-1">Bajo</span>
                          )}
                        </td>
                        <td className="text-end">{item.reorderPoint.toFixed(2)}</td>
                        <td className="text-center">
                          <span className={`badge ${item.isActive ? 'bg-success' : 'bg-danger'}`}>
                            {item.isActive ? 'Activo' : 'Inactivo'}
                          </span>
                        </td>
                        <td className="text-center">
                          <button
                            className="btn btn-sm btn-soft-primary me-1"
                            title="Editar"
                            onClick={() => openSupplyModalForEdit(item)}
                          >
                            <i className="ri-pencil-line"></i>
                          </button>
                          <button
                            className={`btn btn-sm ${item.isActive ? 'btn-soft-warning' : 'btn-soft-success'}`}
                            title={item.isActive ? 'Desactivar' : 'Activar'}
                            onClick={() => toggleSupplyActive(item)}
                          >
                            <i className={item.isActive ? 'ri-pause-line' : 'ri-play-line'}></i>
                          </button>
                        </td>
                      </>
                    )}
                  />
                </>
              )}

              {activeTab === 'stocks' && (
                <>
                  <div className="row mb-2">
                    <div className="col-md-3 mb-2">
                      <label className="form-label">Ubicacion</label>
                      <select
                        className="form-select form-select-sm"
                        value={stocksLocationFilter}
                        onChange={(e) => {
                          setStocksLocationFilter(e.target.value);
                          setStocksPageNumber(1);
                        }}
                      >
                        <option value="">Todas</option>
                        {locations.map((location) => (
                          <option key={location.id} value={location.id}>
                            {location.name}
                          </option>
                        ))}
                      </select>
                    </div>
                    <div className="col-md-3 mb-2 d-flex align-items-end">
                      <div className="form-check form-switch">
                        <input
                          className="form-check-input"
                          type="checkbox"
                          checked={stocksLowStockOnly}
                          onChange={(e) => {
                            setStocksLowStockOnly(e.target.checked);
                            setStocksPageNumber(1);
                          }}
                        />
                        <label className="form-check-label">Solo bajo stock</label>
                      </div>
                    </div>
                  </div>

                  <DataTable
                    data={stocksData.items}
                    columns={STOCK_COLUMNS}
                    keyExtractor={(item) => item.stockId}
                    loading={stocksLoading}
                    emptyMessage="No hay existencias registradas."
                    searchFilter={(item, search) =>
                      item.supplyItemName.toLowerCase().includes(search) ||
                      item.inventoryLocationName.toLowerCase().includes(search) ||
                      (item.supplyCode || '').toLowerCase().includes(search) ||
                      (item.supplySku || '').toLowerCase().includes(search)
                    }
                    serverSide={{
                      totalCount: stocksData.totalCount,
                      page: stocksPageNumber,
                      pageSize: stocksPageSize,
                      onPageChange: setStocksPageNumber,
                      onPageSizeChange: (size) => {
                        setStocksPageSize(size);
                        setStocksPageNumber(1);
                      },
                    }}
                    renderRow={(item) => (
                      <>
                        <td>
                          <div className="fw-semibold">{item.supplyItemName}</div>
                          <div className="text-muted small">{item.supplyCode || item.supplySku || '-'}</div>
                        </td>
                        <td>
                          <div>{item.inventoryLocationName}</div>
                          <div className="text-muted small">{item.inventoryLocationCode}</div>
                        </td>
                        <td className="text-end">
                          {item.onHandQuantity.toFixed(2)} {getUnitLabel(item.unit)}
                        </td>
                        <td className="text-end">{item.reservedQuantity.toFixed(2)}</td>
                        <td className="text-end">
                          {item.availableQuantity.toFixed(2)}
                          {item.isLowStock && (
                            <span className="badge bg-warning text-dark ms-1">Bajo</span>
                          )}
                        </td>
                        <td className="text-end">${item.lastCost.toFixed(2)}</td>
                        <td className="text-center">
                          <span className={`badge ${item.isSupplyActive && item.isLocationActive ? 'bg-success' : 'bg-secondary'}`}>
                            {item.isSupplyActive && item.isLocationActive ? 'Activo' : 'Inactivo'}
                          </span>
                          {item.lastMovementAt && (
                            <div className="text-muted small mt-1">{formatDateTime(item.lastMovementAt)}</div>
                          )}
                        </td>
                      </>
                    )}
                  />
                </>
              )}

              {activeTab === 'movements' && (
                <>
                  <div className="row mb-2">
                    <div className="col-md-3 mb-2">
                      <label className="form-label">Tipo</label>
                      <select
                        className="form-select form-select-sm"
                        value={movementsTypeFilter}
                        onChange={(e) => {
                          setMovementsTypeFilter(e.target.value);
                          setMovementsPageNumber(1);
                        }}
                      >
                        <option value="">Todos</option>
                        {MOVEMENT_TYPE_OPTIONS.map((option) => (
                          <option key={option.value} value={option.value}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </div>
                    <div className="col-md-3 mb-2">
                      <label className="form-label">Motivo</label>
                      <select
                        className="form-select form-select-sm"
                        value={movementsReasonFilter}
                        onChange={(e) => {
                          setMovementsReasonFilter(e.target.value);
                          setMovementsPageNumber(1);
                        }}
                      >
                        <option value="">Todos</option>
                        {MOVEMENT_REASON_OPTIONS.map((option) => (
                          <option key={option.value} value={option.value}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </div>
                    <div className="col-md-3 mb-2">
                      <label className="form-label">Desde</label>
                      <input
                        type="date"
                        className="form-control form-control-sm"
                        value={movementsFromDate}
                        onChange={(e) => {
                          setMovementsFromDate(e.target.value);
                          setMovementsPageNumber(1);
                        }}
                      />
                    </div>
                    <div className="col-md-3 mb-2">
                      <label className="form-label">Hasta</label>
                      <input
                        type="date"
                        className="form-control form-control-sm"
                        value={movementsToDate}
                        onChange={(e) => {
                          setMovementsToDate(e.target.value);
                          setMovementsPageNumber(1);
                        }}
                      />
                    </div>
                  </div>

                  <DataTable
                    data={movementsData.items}
                    columns={MOVEMENT_COLUMNS}
                    keyExtractor={(item) => item.id}
                    loading={movementsLoading}
                    emptyMessage="No hay movimientos registrados."
                    searchFilter={(item, search) =>
                      item.movementNumber.toLowerCase().includes(search) ||
                      (item.referenceNumber || '').toLowerCase().includes(search) ||
                      (item.counterpartyName || '').toLowerCase().includes(search)
                    }
                    serverSide={{
                      totalCount: movementsData.totalCount,
                      page: movementsPageNumber,
                      pageSize: movementsPageSize,
                      onPageChange: setMovementsPageNumber,
                      onPageSizeChange: (size) => {
                        setMovementsPageSize(size);
                        setMovementsPageNumber(1);
                      },
                    }}
                    renderRow={(item) => (
                      <>
                        <td className="fw-semibold">{item.movementNumber}</td>
                        <td className="text-center">{getMovementTypeLabel(item.movementType)}</td>
                        <td className="text-center">{getMovementReasonLabel(item.movementReason)}</td>
                        <td className="text-center">{formatDateTime(item.movementDate)}</td>
                        <td>
                          <div>{item.referenceNumber || '-'}</div>
                          <div className="text-muted small">{item.counterpartyName || '-'}</div>
                        </td>
                        <td className="text-end">${item.totalAmount.toFixed(2)}</td>
                        <td className="text-center">
                          <button
                            className="btn btn-sm btn-soft-secondary"
                            onClick={() => openMovementDetail(item.id)}
                          >
                            <i className="ri-eye-line"></i>
                          </button>
                        </td>
                      </>
                    )}
                  />
                </>
              )}

              {activeTab === 'recipes' && (
                <>
                  <div className="row mb-3">
                    <div className="col-md-6 mb-2">
                      <label className="form-label">Producto o servicio</label>
                      <select
                        className="form-select"
                        value={recipeProductId}
                        onChange={(e) => selectRecipeProduct(e.target.value)}
                      >
                        <option value="">Seleccionar...</option>
                        {recipeProducts.map((product) => (
                          <option key={product.id} value={product.id}>
                            {product.name}
                          </option>
                        ))}
                      </select>
                    </div>
                    <div className="col-md-6 mb-2 d-flex align-items-end justify-content-between">
                      <div>
                        <div className="text-muted small">Item seleccionado</div>
                        <div className="fw-semibold">{recipeProductName || '-'}</div>
                      </div>
                      <div>
                        <span className={`badge ${recipeProductIsAvailable ? 'bg-success' : 'bg-secondary'}`}>
                          {recipeProductIsAvailable ? 'Disponible' : 'No disponible'}
                        </span>
                      </div>
                    </div>
                  </div>

                  {recipeProductId ? (
                    <>
                      <div className="d-flex justify-content-between align-items-center mb-2">
                        <h5 className="mb-0">Receta de insumos</h5>
                        <button type="button" className="btn btn-sm btn-light" onClick={addRecipeLine}>
                          <i className="ri-add-line me-1"></i>Agregar linea
                        </button>
                      </div>

                      <DataTable
                        data={recipeLines}
                        columns={RECIPE_COLUMNS}
                        keyExtractor={(item) => item.clientId}
                        loading={recipeLoading}
                        emptyMessage="Este item aun no tiene receta."
                        searchFilter={(item, search) => {
                          const supplyName = supplyOptions.find((supply) => supply.id === item.supplyItemId)?.name || '';
                          const locationName = locations.find((location) => location.id === item.inventoryLocationId)?.name || '';
                          return (
                            supplyName.toLowerCase().includes(search) ||
                            locationName.toLowerCase().includes(search) ||
                            item.notes.toLowerCase().includes(search)
                          );
                        }}
                        renderRow={(item) => {
                          const lineIndex = recipeLines.findIndex((line) => line.clientId === item.clientId);
                          const quantity = Number(item.quantity || '0');
                          const wastePercent = Number(item.wastePercent || '0');
                          const effectiveQuantity = Number.isNaN(quantity) || Number.isNaN(wastePercent)
                            ? 0
                            : quantity * (1 + (wastePercent / 100));

                          return (
                            <>
                              <td>
                                <select
                                  className="form-select form-select-sm"
                                  value={item.supplyItemId}
                                  onChange={(e) => updateRecipeLine(lineIndex, { supplyItemId: e.target.value })}
                                >
                                  <option value="">Seleccionar...</option>
                                  {supplyOptions.map((option) => (
                                    <option key={option.id} value={option.id}>
                                      {option.name} ({option.code || option.sku || '-'})
                                    </option>
                                  ))}
                                </select>
                              </td>
                              <td>
                                <select
                                  className="form-select form-select-sm"
                                  value={item.inventoryLocationId}
                                  onChange={(e) => updateRecipeLine(lineIndex, { inventoryLocationId: e.target.value })}
                                >
                                  <option value="">Ubicacion por defecto</option>
                                  {activeLocations.map((option) => (
                                    <option key={option.id} value={option.id}>
                                      {option.name}
                                    </option>
                                  ))}
                                </select>
                              </td>
                              <td className="text-end" style={{ minWidth: 120 }}>
                                <input
                                  type="number"
                                  step="0.0001"
                                  className="form-control form-control-sm text-end"
                                  value={item.quantity}
                                  onChange={(e) => updateRecipeLine(lineIndex, { quantity: e.target.value })}
                                />
                              </td>
                              <td className="text-end" style={{ minWidth: 120 }}>
                                <input
                                  type="number"
                                  step="0.01"
                                  className="form-control form-control-sm text-end"
                                  value={item.wastePercent}
                                  onChange={(e) => updateRecipeLine(lineIndex, { wastePercent: e.target.value })}
                                />
                              </td>
                              <td className="text-end">{effectiveQuantity.toFixed(4)}</td>
                              <td style={{ minWidth: 220 }}>
                                <input
                                  type="text"
                                  className="form-control form-control-sm"
                                  value={item.notes}
                                  onChange={(e) => updateRecipeLine(lineIndex, { notes: e.target.value })}
                                />
                              </td>
                              <td className="text-center">
                                <button
                                  type="button"
                                  className="btn btn-sm btn-soft-danger"
                                  onClick={() => removeRecipeLine(lineIndex)}
                                >
                                  <i className="ri-delete-bin-line"></i>
                                </button>
                              </td>
                            </>
                          );
                        }}
                      />
                    </>
                  ) : (
                    <p className="text-muted mb-0">Selecciona un producto o servicio para administrar su receta.</p>
                  )}
                </>
              )}

              {activeTab === 'kardex' && (
                <>
                  <div className="row mb-2">
                    <div className="col-md-3 mb-2">
                      <label className="form-label">Insumo</label>
                      <select
                        className="form-select form-select-sm"
                        value={kardexSupplyFilter}
                        onChange={(e) => {
                          setKardexSupplyFilter(e.target.value);
                          setKardexPageNumber(1);
                        }}
                      >
                        <option value="">Todos</option>
                        {supplyOptions.map((supply) => (
                          <option key={supply.id} value={supply.id}>
                            {supply.name}
                          </option>
                        ))}
                      </select>
                    </div>
                    <div className="col-md-3 mb-2">
                      <label className="form-label">Ubicacion</label>
                      <select
                        className="form-select form-select-sm"
                        value={kardexLocationFilter}
                        onChange={(e) => {
                          setKardexLocationFilter(e.target.value);
                          setKardexPageNumber(1);
                        }}
                      >
                        <option value="">Todas</option>
                        {locations.map((location) => (
                          <option key={location.id} value={location.id}>
                            {location.name}
                          </option>
                        ))}
                      </select>
                    </div>
                    <div className="col-md-3 mb-2">
                      <label className="form-label">Desde</label>
                      <input
                        type="date"
                        className="form-control form-control-sm"
                        value={kardexFromDate}
                        onChange={(e) => {
                          setKardexFromDate(e.target.value);
                          setKardexPageNumber(1);
                        }}
                      />
                    </div>
                    <div className="col-md-3 mb-2">
                      <label className="form-label">Hasta</label>
                      <input
                        type="date"
                        className="form-control form-control-sm"
                        value={kardexToDate}
                        onChange={(e) => {
                          setKardexToDate(e.target.value);
                          setKardexPageNumber(1);
                        }}
                      />
                    </div>
                  </div>

                  <DataTable
                    data={kardexData.items}
                    columns={KARDEX_COLUMNS}
                    keyExtractor={(item) => item.movementLineId}
                    loading={kardexLoading}
                    emptyMessage="No hay movimientos para los filtros seleccionados."
                    searchFilter={(item, search) =>
                      item.movementNumber.toLowerCase().includes(search) ||
                      item.supplyItemName.toLowerCase().includes(search) ||
                      item.inventoryLocationName.toLowerCase().includes(search) ||
                      (item.referenceNumber || '').toLowerCase().includes(search)
                    }
                    serverSide={{
                      totalCount: kardexData.totalCount,
                      page: kardexPageNumber,
                      pageSize: kardexPageSize,
                      onPageChange: setKardexPageNumber,
                      onPageSizeChange: (size) => {
                        setKardexPageSize(size);
                        setKardexPageNumber(1);
                      },
                    }}
                    renderRow={(item) => (
                      <>
                        <td className="text-center">{formatDateTime(item.movementDate)}</td>
                        <td className="fw-semibold">{item.movementNumber}</td>
                        <td>{item.supplyItemName}</td>
                        <td>{item.inventoryLocationName}</td>
                        <td className="text-end">{item.quantityIn > 0 ? item.quantityIn.toFixed(4) : '-'}</td>
                        <td className="text-end">{item.quantityOut > 0 ? item.quantityOut.toFixed(4) : '-'}</td>
                        <td className="text-end">{item.quantityAfter.toFixed(4)}</td>
                        <td className="text-end">{item.unitCost !== undefined ? `$${item.unitCost.toFixed(4)}` : '-'}</td>
                        <td>
                          <div>{item.referenceNumber || '-'}</div>
                          <div className="text-muted small">{item.externalReference || '-'}</div>
                        </td>
                      </>
                    )}
                  />
                </>
              )}

              {activeTab === 'alerts' && (
                <>
                  <div className="row mb-3">
                    <div className="col-md-3 mb-2">
                      <div className="card border-danger h-100 mb-0">
                        <div className="card-body py-2">
                          <div className="text-muted small">Sin stock</div>
                          <div className="h3 mb-0">{alertsData.outOfStockCount}</div>
                        </div>
                      </div>
                    </div>
                    <div className="col-md-3 mb-2">
                      <div className="card border-danger h-100 mb-0">
                        <div className="card-body py-2">
                          <div className="text-muted small">Bajo stock critico</div>
                          <div className="h3 mb-0">{alertsData.criticalLowStockCount}</div>
                        </div>
                      </div>
                    </div>
                    <div className="col-md-3 mb-2">
                      <div className="card border-warning h-100 mb-0">
                        <div className="card-body py-2">
                          <div className="text-muted small">Bajo stock</div>
                          <div className="h3 mb-0">{alertsData.lowStockCount}</div>
                        </div>
                      </div>
                    </div>
                    <div className="col-md-3 mb-2">
                      <div className="card border-info h-100 mb-0">
                        <div className="card-body py-2">
                          <div className="text-muted small">Items sin receta</div>
                          <div className="h3 mb-0">{alertsData.missingRecipeProductCount}</div>
                        </div>
                      </div>
                    </div>
                  </div>

                  <DataTable
                    data={alertsData.items}
                    columns={[
                      { header: 'Insumo' },
                      { header: 'Ubicacion' },
                      { header: 'Existencia', className: 'text-end' },
                      { header: 'Reorden', className: 'text-end' },
                      { header: 'Deficit', className: 'text-end' },
                      { header: 'Estado', className: 'text-center' },
                    ]}
                    keyExtractor={(item) => `${item.supplyItemId}-${item.inventoryLocationId}`}
                    loading={alertsLoading}
                    emptyMessage="No hay alertas de stock actualmente."
                    searchFilter={(item, search) =>
                      item.supplyItemName.toLowerCase().includes(search) ||
                      item.inventoryLocationName.toLowerCase().includes(search)
                    }
                    renderRow={(item) => (
                      <>
                        <td>{item.supplyItemName}</td>
                        <td>{item.inventoryLocationName}</td>
                        <td className="text-end">{item.onHandQuantity.toFixed(4)}</td>
                        <td className="text-end">{item.reorderPoint.toFixed(4)}</td>
                        <td className="text-end">{item.deficit.toFixed(4)}</td>
                        <td className="text-center">
                          <span
                            className={`badge ${
                              item.isOutOfStock
                                ? 'bg-danger'
                                : item.isCritical
                                  ? 'bg-danger-subtle text-danger'
                                  : 'bg-warning text-dark'
                            }`}
                          >
                            {item.isOutOfStock ? 'Sin stock' : item.isCritical ? 'Bajo stock critico' : 'Bajo stock'}
                          </span>
                        </td>
                      </>
                    )}
                  />
                </>
              )}

              {activeTab === 'locations' && (
                <DataTable
                  data={locations}
                  columns={LOCATION_COLUMNS}
                  keyExtractor={(item) => item.id}
                  loading={locationsLoading}
                  emptyMessage="No hay ubicaciones registradas."
                  searchFilter={(item, search) =>
                    item.name.toLowerCase().includes(search) ||
                    item.code.toLowerCase().includes(search) ||
                    (item.city || '').toLowerCase().includes(search)
                  }
                  renderRow={(item) => (
                    <>
                      <td>
                        <div className="fw-semibold">{item.name}</div>
                        <div className="text-muted small">{item.description || '-'}</div>
                      </td>
                      <td>{item.code}</td>
                      <td>
                        <div>{item.contactName || '-'}</div>
                        <div className="text-muted small">{item.contactPhone || '-'}</div>
                      </td>
                      <td>{item.city || '-'}{item.state ? `, ${item.state}` : ''}</td>
                      <td className="text-center">
                        {item.isDefault && <span className="badge bg-info me-1">Default</span>}
                        <span className={`badge ${item.isActive ? 'bg-success' : 'bg-danger'}`}>
                          {item.isActive ? 'Activa' : 'Inactiva'}
                        </span>
                      </td>
                      <td className="text-center">
                        <button
                          className="btn btn-sm btn-soft-primary me-1"
                          onClick={() => openLocationModalForEdit(item)}
                        >
                          <i className="ri-pencil-line"></i>
                        </button>
                        <button
                          className={`btn btn-sm ${item.isActive ? 'btn-soft-warning' : 'btn-soft-success'}`}
                          onClick={() => toggleLocationActive(item)}
                        >
                          <i className={item.isActive ? 'ri-pause-line' : 'ri-play-line'}></i>
                        </button>
                      </td>
                    </>
                  )}
                />
              )}
            </div>
          </div>
        </div>
      </div>

      <Modal show={showSupplyModal} onHide={() => setShowSupplyModal(false)} size="lg" centered>
        <Modal.Header closeButton>
          <Modal.Title>{editingSupply ? 'Editar insumo' : 'Nuevo insumo'}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <div className="row">
            <div className="col-md-6 mb-2">
              <label className="form-label">Nombre *</label>
              <input
                type="text"
                className="form-control"
                value={supplyForm.name}
                onChange={(e) => setSupplyForm((current) => ({ ...current, name: e.target.value }))}
              />
            </div>
            <div className="col-md-3 mb-2">
              <label className="form-label">Codigo</label>
              <input
                type="text"
                className="form-control"
                value={supplyForm.code}
                onChange={(e) => setSupplyForm((current) => ({ ...current, code: e.target.value }))}
              />
            </div>
            <div className="col-md-3 mb-2">
              <label className="form-label">SKU</label>
              <input
                type="text"
                className="form-control"
                value={supplyForm.sku}
                onChange={(e) => setSupplyForm((current) => ({ ...current, sku: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Codigo de barras</label>
              <input
                type="text"
                className="form-control"
                value={supplyForm.barcode}
                onChange={(e) => setSupplyForm((current) => ({ ...current, barcode: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Categoria</label>
              <input
                type="text"
                className="form-control"
                value={supplyForm.categoryName}
                onChange={(e) => setSupplyForm((current) => ({ ...current, categoryName: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Unidad</label>
              <select
                className="form-select"
                value={supplyForm.unit}
                onChange={(e) => setSupplyForm((current) => ({ ...current, unit: Number(e.target.value) as UnitType }))}
              >
                {UNIT_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Marca</label>
              <input
                type="text"
                className="form-control"
                value={supplyForm.brand}
                onChange={(e) => setSupplyForm((current) => ({ ...current, brand: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Modelo</label>
              <input
                type="text"
                className="form-control"
                value={supplyForm.model}
                onChange={(e) => setSupplyForm((current) => ({ ...current, model: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Costo por defecto</label>
              <input
                type="number"
                step="0.01"
                className="form-control"
                value={supplyForm.defaultCost}
                onChange={(e) => setSupplyForm((current) => ({ ...current, defaultCost: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Stock minimo</label>
              <input
                type="number"
                step="0.01"
                className="form-control"
                value={supplyForm.minStock}
                onChange={(e) => setSupplyForm((current) => ({ ...current, minStock: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Stock maximo</label>
              <input
                type="number"
                step="0.01"
                className="form-control"
                value={supplyForm.maxStock}
                onChange={(e) => setSupplyForm((current) => ({ ...current, maxStock: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Punto de reorden</label>
              <input
                type="number"
                step="0.01"
                className="form-control"
                value={supplyForm.reorderPoint}
                onChange={(e) => setSupplyForm((current) => ({ ...current, reorderPoint: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Impuesto (%)</label>
              <input
                type="number"
                step="0.01"
                className="form-control"
                value={supplyForm.taxRate}
                onChange={(e) => setSupplyForm((current) => ({ ...current, taxRate: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2 d-flex align-items-end">
              <div>
                <div className="form-check mb-1">
                  <input
                    className="form-check-input"
                    type="checkbox"
                    checked={supplyForm.trackBatches}
                    onChange={(e) => setSupplyForm((current) => ({ ...current, trackBatches: e.target.checked }))}
                  />
                  <label className="form-check-label">Control por lotes</label>
                </div>
                <div className="form-check mb-1">
                  <input
                    className="form-check-input"
                    type="checkbox"
                    checked={supplyForm.trackExpiration}
                    onChange={(e) => setSupplyForm((current) => ({ ...current, trackExpiration: e.target.checked }))}
                  />
                  <label className="form-check-label">Control caducidad</label>
                </div>
                <div className="form-check">
                  <input
                    className="form-check-input"
                    type="checkbox"
                    checked={supplyForm.isPerishable}
                    onChange={(e) => setSupplyForm((current) => ({ ...current, isPerishable: e.target.checked }))}
                  />
                  <label className="form-check-label">Es perecedero</label>
                </div>
                <div className="form-check">
                  <input
                    className="form-check-input"
                    type="checkbox"
                    checked={supplyForm.isCritical}
                    onChange={(e) => setSupplyForm((current) => ({ ...current, isCritical: e.target.checked }))}
                  />
                  <label className="form-check-label">Insumo critico</label>
                </div>
              </div>
            </div>
            <div className="col-12 mb-2">
              <label className="form-label">Descripcion</label>
              <textarea
                className="form-control"
                rows={2}
                value={supplyForm.description}
                onChange={(e) => setSupplyForm((current) => ({ ...current, description: e.target.value }))}
              />
            </div>
            <div className="col-12 mb-0">
              <label className="form-label">Notas</label>
              <textarea
                className="form-control"
                rows={2}
                value={supplyForm.notes}
                onChange={(e) => setSupplyForm((current) => ({ ...current, notes: e.target.value }))}
              />
            </div>
          </div>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={() => setShowSupplyModal(false)}>
            Cancelar
          </button>
          <button type="button" className="btn btn-primary" onClick={submitSupply} disabled={savingSupply}>
            {savingSupply ? 'Guardando...' : editingSupply ? 'Guardar cambios' : 'Crear insumo'}
          </button>
        </Modal.Footer>
      </Modal>

      <Modal show={showLocationModal} onHide={() => setShowLocationModal(false)} size="lg" centered>
        <Modal.Header closeButton>
          <Modal.Title>{editingLocation ? 'Editar ubicacion' : 'Nueva ubicacion'}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <div className="row">
            <div className="col-md-6 mb-2">
              <label className="form-label">Nombre *</label>
              <input
                type="text"
                className="form-control"
                value={locationForm.name}
                onChange={(e) => setLocationForm((current) => ({ ...current, name: e.target.value }))}
              />
            </div>
            <div className="col-md-3 mb-2">
              <label className="form-label">Codigo *</label>
              <input
                type="text"
                className="form-control"
                value={locationForm.code}
                onChange={(e) => setLocationForm((current) => ({ ...current, code: e.target.value }))}
              />
            </div>
            <div className="col-md-3 mb-2 d-flex align-items-end">
              <div className="form-check">
                <input
                  className="form-check-input"
                  type="checkbox"
                  checked={locationForm.isDefault}
                  onChange={(e) => setLocationForm((current) => ({ ...current, isDefault: e.target.checked }))}
                />
                <label className="form-check-label">Ubicacion por defecto</label>
              </div>
            </div>
            <div className="col-12 mb-2">
              <label className="form-label">Descripcion</label>
              <textarea
                className="form-control"
                rows={2}
                value={locationForm.description}
                onChange={(e) => setLocationForm((current) => ({ ...current, description: e.target.value }))}
              />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Direccion linea 1</label>
              <input
                type="text"
                className="form-control"
                value={locationForm.addressLine1}
                onChange={(e) => setLocationForm((current) => ({ ...current, addressLine1: e.target.value }))}
              />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Direccion linea 2</label>
              <input
                type="text"
                className="form-control"
                value={locationForm.addressLine2}
                onChange={(e) => setLocationForm((current) => ({ ...current, addressLine2: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Ciudad</label>
              <input
                type="text"
                className="form-control"
                value={locationForm.city}
                onChange={(e) => setLocationForm((current) => ({ ...current, city: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Estado</label>
              <input
                type="text"
                className="form-control"
                value={locationForm.state}
                onChange={(e) => setLocationForm((current) => ({ ...current, state: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Codigo postal</label>
              <input
                type="text"
                className="form-control"
                value={locationForm.postalCode}
                onChange={(e) => setLocationForm((current) => ({ ...current, postalCode: e.target.value }))}
              />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Contacto</label>
              <input
                type="text"
                className="form-control"
                value={locationForm.contactName}
                onChange={(e) => setLocationForm((current) => ({ ...current, contactName: e.target.value }))}
              />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Telefono contacto</label>
              <input
                type="text"
                className="form-control"
                value={locationForm.contactPhone}
                onChange={(e) => setLocationForm((current) => ({ ...current, contactPhone: e.target.value }))}
              />
            </div>
          </div>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={() => setShowLocationModal(false)}>
            Cancelar
          </button>
          <button type="button" className="btn btn-primary" onClick={submitLocation} disabled={savingLocation}>
            {savingLocation ? 'Guardando...' : editingLocation ? 'Guardar cambios' : 'Crear ubicacion'}
          </button>
        </Modal.Footer>
      </Modal>

      <Modal show={showMovementModal} onHide={() => setShowMovementModal(false)} size="xl" centered>
        <Modal.Header closeButton>
          <Modal.Title>Registrar movimiento de inventario</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <div className="row mb-2">
            <div className="col-md-3 mb-2">
              <label className="form-label">Tipo *</label>
              <select
                className="form-select"
                value={movementForm.movementType}
                onChange={(e) => setMovementForm((current) => ({
                  ...current,
                  movementType: Number(e.target.value) as InventoryMovementType,
                }))}
              >
                {MOVEMENT_TYPE_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>{option.label}</option>
                ))}
              </select>
            </div>
            <div className="col-md-3 mb-2">
              <label className="form-label">Motivo *</label>
              <select
                className="form-select"
                value={movementForm.movementReason}
                onChange={(e) => setMovementForm((current) => ({
                  ...current,
                  movementReason: Number(e.target.value) as InventoryMovementReason,
                }))}
              >
                {MOVEMENT_REASON_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>{option.label}</option>
                ))}
              </select>
            </div>
            <div className="col-md-3 mb-2">
              <label className="form-label">Fecha y hora</label>
              <input
                type="datetime-local"
                className="form-control"
                value={movementForm.movementDate}
                onChange={(e) => setMovementForm((current) => ({ ...current, movementDate: e.target.value }))}
              />
            </div>
            <div className="col-md-3 mb-2">
              <label className="form-label">Referencia</label>
              <input
                type="text"
                className="form-control"
                value={movementForm.referenceNumber}
                onChange={(e) => setMovementForm((current) => ({ ...current, referenceNumber: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Referencia externa</label>
              <input
                type="text"
                className="form-control"
                value={movementForm.externalReference}
                onChange={(e) => setMovementForm((current) => ({ ...current, externalReference: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Contraparte</label>
              <input
                type="text"
                className="form-control"
                value={movementForm.counterpartyName}
                onChange={(e) => setMovementForm((current) => ({ ...current, counterpartyName: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Documento</label>
              <input
                type="text"
                className="form-control"
                value={movementForm.documentNumber}
                onChange={(e) => setMovementForm((current) => ({ ...current, documentNumber: e.target.value }))}
              />
            </div>
            <div className="col-12 mb-2">
              <label className="form-label">Notas</label>
              <textarea
                className="form-control"
                rows={2}
                value={movementForm.notes}
                onChange={(e) => setMovementForm((current) => ({ ...current, notes: e.target.value }))}
              />
            </div>
          </div>

          <div className="d-flex justify-content-between align-items-center mb-2">
            <h5 className="mb-0">Detalles</h5>
            <button type="button" className="btn btn-sm btn-light" onClick={addMovementLine}>
              <i className="ri-add-line me-1"></i>Agregar linea
            </button>
          </div>

          {movementForm.lines.map((line, index) => (
            <div className="border rounded p-2 mb-2" key={`line-${index}`}>
              <div className="d-flex justify-content-between align-items-center mb-2">
                <strong>Linea {index + 1}</strong>
                {movementForm.lines.length > 1 && (
                  <button
                    type="button"
                    className="btn btn-sm btn-soft-danger"
                    onClick={() => removeMovementLine(index)}
                  >
                    <i className="ri-delete-bin-line"></i>
                  </button>
                )}
              </div>
              <div className="row">
                <div className="col-md-4 mb-2">
                  <label className="form-label">Insumo *</label>
                  <select
                    className="form-select"
                    value={line.supplyItemId}
                    onChange={(e) => updateMovementLine(index, { supplyItemId: e.target.value })}
                  >
                    <option value="">Seleccionar...</option>
                    {supplyOptions.map((option) => (
                      <option key={option.id} value={option.id}>
                        {option.name} ({option.code || option.sku || '-'})
                      </option>
                    ))}
                  </select>
                </div>
                <div className="col-md-3 mb-2">
                  <label className="form-label">Ubicacion *</label>
                  <select
                    className="form-select"
                    value={line.inventoryLocationId}
                    onChange={(e) => updateMovementLine(index, { inventoryLocationId: e.target.value })}
                  >
                    <option value="">Seleccionar...</option>
                    {activeLocations.map((option) => (
                      <option key={option.id} value={option.id}>
                        {option.name}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="col-md-2 mb-2">
                  <label className="form-label">Cantidad *</label>
                  <input
                    type="number"
                    step="0.01"
                    className="form-control"
                    value={line.quantity}
                    onChange={(e) => updateMovementLine(index, { quantity: e.target.value })}
                  />
                </div>
                <div className="col-md-3 mb-2">
                  <label className="form-label">Costo unitario</label>
                  <input
                    type="number"
                    step="0.01"
                    className="form-control"
                    value={line.unitCost}
                    onChange={(e) => updateMovementLine(index, { unitCost: e.target.value })}
                  />
                </div>
                <div className="col-md-3 mb-2">
                  <label className="form-label">Lote</label>
                  <input
                    type="text"
                    className="form-control"
                    value={line.lotNumber}
                    onChange={(e) => updateMovementLine(index, { lotNumber: e.target.value })}
                  />
                </div>
                <div className="col-md-3 mb-2">
                  <label className="form-label">Caducidad</label>
                  <input
                    type="date"
                    className="form-control"
                    value={line.expirationDate}
                    onChange={(e) => updateMovementLine(index, { expirationDate: e.target.value })}
                  />
                </div>
                <div className="col-md-6 mb-2">
                  <label className="form-label">Notas</label>
                  <input
                    type="text"
                    className="form-control"
                    value={line.notes}
                    onChange={(e) => updateMovementLine(index, { notes: e.target.value })}
                  />
                </div>
              </div>
            </div>
          ))}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={() => setShowMovementModal(false)}>
            Cancelar
          </button>
          <button type="button" className="btn btn-primary" onClick={submitMovement} disabled={savingMovement}>
            {savingMovement ? 'Guardando...' : 'Registrar movimiento'}
          </button>
        </Modal.Footer>
      </Modal>

      <Modal show={showMovementDetailModal} onHide={() => setShowMovementDetailModal(false)} size="lg" centered>
        <Modal.Header closeButton>
          <Modal.Title>Detalle de movimiento</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {movementDetailLoading ? (
            <div className="text-center py-4">
              <div className="spinner-border text-primary" role="status"></div>
            </div>
          ) : selectedMovement ? (
            <>
              <div className="row mb-3">
                <div className="col-md-6">
                  <p className="mb-1"><strong>Folio:</strong> {selectedMovement.movementNumber}</p>
                  <p className="mb-1"><strong>Tipo:</strong> {getMovementTypeLabel(selectedMovement.movementType)}</p>
                  <p className="mb-1"><strong>Motivo:</strong> {getMovementReasonLabel(selectedMovement.movementReason)}</p>
                </div>
                <div className="col-md-6">
                  <p className="mb-1"><strong>Fecha:</strong> {formatDateTime(selectedMovement.movementDate)}</p>
                  <p className="mb-1"><strong>Referencia:</strong> {selectedMovement.referenceNumber || '-'}</p>
                  <p className="mb-1"><strong>Monto:</strong> ${selectedMovement.totalAmount.toFixed(2)}</p>
                </div>
              </div>

              <div className="table-responsive">
                <table className="table table-sm table-striped mb-2">
                  <thead>
                    <tr>
                      <th>Insumo</th>
                      <th>Ubicacion</th>
                      <th className="text-end">Cantidad</th>
                      <th className="text-end">Costo unit.</th>
                      <th className="text-end">Total</th>
                      <th className="text-end">Antes</th>
                      <th className="text-end">Despues</th>
                      <th>Lote</th>
                      <th>Caducidad</th>
                    </tr>
                  </thead>
                  <tbody>
                    {selectedMovement.lines.map((line) => (
                      <tr key={line.id}>
                        <td>{line.supplyItemName}</td>
                        <td>{line.inventoryLocationName}</td>
                        <td className="text-end">{line.quantity.toFixed(2)}</td>
                        <td className="text-end">{line.unitCost !== undefined ? `$${line.unitCost.toFixed(2)}` : '-'}</td>
                        <td className="text-end">{line.totalCost !== undefined ? `$${line.totalCost.toFixed(2)}` : '-'}</td>
                        <td className="text-end">{line.quantityBefore.toFixed(2)}</td>
                        <td className="text-end">{line.quantityAfter.toFixed(2)}</td>
                        <td>{line.lotNumber || '-'}</td>
                        <td>{formatDate(line.expirationDate)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {selectedMovement.notes && (
                <div className="mt-2">
                  <strong>Notas:</strong> {selectedMovement.notes}
                </div>
              )}
            </>
          ) : (
            <p className="text-muted mb-0">No se encontro informacion.</p>
          )}
        </Modal.Body>
      </Modal>
    </>
  );
}
