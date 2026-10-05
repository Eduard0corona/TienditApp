import { inventoryApi } from '../../api/inventoryApi';
import type { InventoryMovementReason, InventoryMovementType } from '../../types';
import { businessDayEnd, businessDayStart } from '../../utils/dates';
import { useServerList, type ServerListQuery } from './useServerList';

// The lists of the inventory tabs, searched and paged on the server. The screen holds them, so each keeps its
// filters while another tab is open.

export interface SupplyFilters {
  /** '' (all), 'true' or 'false'. */
  active: string;
  lowStockOnly: boolean;
}

export interface StockFilters {
  locationId: string;
  lowStockOnly: boolean;
}

export interface MovementFilters {
  /** A movement type or reason as the select gives it, or '' for all. */
  type: string;
  reason: string;
  /** Days of the business (yyyy-mm-dd). */
  fromDate: string;
  toDate: string;
}

export interface KardexFilters {
  supplyId: string;
  locationId: string;
  fromDate: string;
  toDate: string;
}

const fetchSupplies = async ({ filters, search, pageNumber, pageSize }: ServerListQuery<SupplyFilters>) => {
  const { data } = await inventoryApi.getSupplies({
    pageNumber,
    pageSize,
    isActive: filters.active === '' ? undefined : filters.active === 'true',
    lowStockOnly: filters.lowStockOnly || undefined,
    search: search || undefined,
  });
  return data;
};

const fetchStocks = async ({ filters, search, pageNumber, pageSize }: ServerListQuery<StockFilters>) => {
  const { data } = await inventoryApi.getStocks({
    pageNumber,
    pageSize,
    inventoryLocationId: filters.locationId || undefined,
    lowStockOnly: filters.lowStockOnly || undefined,
    search: search || undefined,
  });
  return data;
};

const fetchMovements = async ({ filters, search, pageNumber, pageSize }: ServerListQuery<MovementFilters>) => {
  const { data } = await inventoryApi.getMovements({
    pageNumber,
    pageSize,
    movementType: filters.type === '' ? undefined : Number(filters.type) as InventoryMovementType,
    movementReason: filters.reason === '' ? undefined : Number(filters.reason) as InventoryMovementReason,
    fromDate: filters.fromDate ? businessDayStart(filters.fromDate) : undefined,
    toDate: filters.toDate ? businessDayEnd(filters.toDate) : undefined,
    search: search || undefined,
  });
  return data;
};

const fetchKardex = async ({ filters, search, pageNumber, pageSize }: ServerListQuery<KardexFilters>) => {
  const { data } = await inventoryApi.getKardex({
    pageNumber,
    pageSize,
    supplyItemId: filters.supplyId || undefined,
    inventoryLocationId: filters.locationId || undefined,
    fromDate: filters.fromDate ? businessDayStart(filters.fromDate) : undefined,
    toDate: filters.toDate ? businessDayEnd(filters.toDate) : undefined,
    search: search || undefined,
  });
  return data;
};

export const useSupplyList = (onError: (message: string) => void) => useServerList({
  fetch: fetchSupplies,
  filters: { active: '', lowStockOnly: false },
  errorMessage: 'Error al cargar insumos.',
  onError,
});

export const useStockList = (onError: (message: string) => void) => useServerList({
  fetch: fetchStocks,
  filters: { locationId: '', lowStockOnly: false },
  errorMessage: 'Error al cargar existencias.',
  onError,
});

export const useMovementList = (onError: (message: string) => void) => useServerList({
  fetch: fetchMovements,
  filters: { type: '', reason: '', fromDate: '', toDate: '' },
  errorMessage: 'Error al cargar movimientos.',
  onError,
});

/** Loads only while its tab is open. */
export const useKardexList = (onError: (message: string) => void, enabled: boolean) => useServerList({
  fetch: fetchKardex,
  filters: { supplyId: '', locationId: '', fromDate: '', toDate: '' },
  pageSize: 20,
  enabled,
  errorMessage: 'Error al cargar kardex.',
  onError,
});
