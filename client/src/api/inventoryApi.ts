import api from './axios';
import type {
  CreateInventoryLocationRequest,
  CreateInventoryMovementRequest,
  CreateSupplyItemRequest,
  InventoryLocationDto,
  InventoryMovementDto,
  InventoryMovementReason,
  InventoryMovementType,
  InventoryAlertsDto,
  InventoryKardexEntryDto,
  InventoryStockDto,
  PaginatedList,
  ProductRecipeDto,
  ProductStockDto,
  SupplyItemDto,
  TrackProductStockRequest,
  UpsertProductRecipeRequest,
  UpdateInventoryLocationRequest,
  UpdateSupplyItemRequest,
} from '../types';

export const inventoryApi = {
  getSupplies: (params?: {
    search?: string;
    isActive?: boolean;
    lowStockOnly?: boolean;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<SupplyItemDto>>('/inventory/supplies', { params }),

  getSupplyById: (id: string) =>
    api.get<SupplyItemDto>(`/inventory/supplies/${id}`),

  createSupply: (data: CreateSupplyItemRequest) =>
    api.post<SupplyItemDto>('/inventory/supplies', data),

  updateSupply: (id: string, data: UpdateSupplyItemRequest) =>
    api.put<SupplyItemDto>(`/inventory/supplies/${id}`, data),

  setSupplyActive: (id: string, isActive: boolean) =>
    api.patch(`/inventory/supplies/${id}/active`, { isActive }),

  getLocations: (params?: { isActive?: boolean }) =>
    api.get<InventoryLocationDto[]>('/inventory/locations', { params }),

  createLocation: (data: CreateInventoryLocationRequest) =>
    api.post<InventoryLocationDto>('/inventory/locations', data),

  updateLocation: (id: string, data: UpdateInventoryLocationRequest) =>
    api.put<InventoryLocationDto>(`/inventory/locations/${id}`, data),

  setLocationActive: (id: string, isActive: boolean) =>
    api.patch(`/inventory/locations/${id}/active`, { isActive }),

  getStocks: (params?: {
    search?: string;
    inventoryLocationId?: string;
    lowStockOnly?: boolean;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<InventoryStockDto>>('/inventory/stocks', { params }),

  getMovements: (params?: {
    movementType?: InventoryMovementType;
    movementReason?: InventoryMovementReason;
    fromDate?: string;
    toDate?: string;
    /** Folio, reference, document or counterparty, over every page. */
    search?: string;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<InventoryMovementDto>>('/inventory/movements', { params }),

  getMovementById: (id: string) =>
    api.get<InventoryMovementDto>(`/inventory/movements/${id}`),

  createMovement: (data: CreateInventoryMovementRequest) =>
    api.post<InventoryMovementDto>('/inventory/movements', data),

  getKardex: (params?: {
    supplyItemId?: string;
    inventoryLocationId?: string;
    fromDate?: string;
    toDate?: string;
    /** Supply, location, folio or reference, over every page. */
    search?: string;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<InventoryKardexEntryDto>>('/inventory/kardex', { params }),

  getAlerts: () =>
    api.get<InventoryAlertsDto>('/inventory/alerts'),

  getProductRecipe: (productId: string) =>
    api.get<ProductRecipeDto>(`/inventory/recipes/products/${productId}`),

  upsertProductRecipe: (productId: string, data: UpsertProductRecipeRequest) =>
    api.put<ProductRecipeDto>(`/inventory/recipes/products/${productId}`, data),

  getProductStock: (productId: string) =>
    api.get<ProductStockDto>(`/inventory/products/${productId}/stock`),

  trackProductStock: (productId: string, data: TrackProductStockRequest) =>
    api.post<ProductStockDto>(`/inventory/products/${productId}/stock`, data),

  stopTrackingProductStock: (productId: string) =>
    api.delete<ProductStockDto>(`/inventory/products/${productId}/stock`),
};
