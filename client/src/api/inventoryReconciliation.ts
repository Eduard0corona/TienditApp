import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, type Schemas } from './typedClient';

// Revisar que cuadre (Fase 27C, ADR 0039): the stocks that do not add up to their history, where one stops adding up,
// and recording the gap in the history. Kept out of queries.ts, which the first visit loads.

export type InventoryReconciliation = Schemas['InventoryReconciliationDto'];
export type InventoryReconciliationItem = Schemas['InventoryReconciliationItemDto'];
export type InventoryReconciliationDetail = Schemas['InventoryReconciliationDetailDto'];
export type InventoryReconciliationEvent = Schemas['InventoryReconciliationEventDto'];
export type ReconcileInventoryLedgerBody = Schemas['ReconcileInventoryLedgerCommand'];

export interface InventoryReconciliationFilter {
  /** One location, or '' for every one. */
  locationId: string;
  /** Item or location name. */
  search: string;
  page: number;
  pageSize: number;
}

/** The review as it opens: every location, by location and name. */
export const INITIAL_RECONCILIATION_FILTER: InventoryReconciliationFilter = { locationId: '', search: '', page: 1, pageSize: 25 };

/** Cache keys of the review: reconciling reads the review and the detail again. */
export const reconciliationKeys = {
  all: ['inventory-reconciliation'] as const,
  review: (filter: InventoryReconciliationFilter) => ['inventory-reconciliation', 'review', filter] as const,
  detail: (supplyItemId: string, locationId: string) => ['inventory-reconciliation', 'detail', supplyItemId, locationId] as const,
};

/** The stocks that do not add up to their history, and how many were compared. Read again each time it opens. */
export function useInventoryReconciliation(filter: InventoryReconciliationFilter) {
  return useQuery({
    queryKey: reconciliationKeys.review(filter),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/inventory/reconciliation', {
      query: {
        inventoryLocationId: filter.locationId || undefined,
        search: filter.search || undefined,
        pageNumber: filter.page,
        pageSize: filter.pageSize,
      },
      signal,
    }),
    placeholderData: keepPreviousData,
    staleTime: 0,
    gcTime: 0,
  });
}

/** Where the history of one stock stops adding up to it. */
export function useReconciliationDetail(supplyItemId: string, locationId: string) {
  return useQuery({
    queryKey: reconciliationKeys.detail(supplyItemId, locationId),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/inventory/reconciliation/{supplyItemId}/{inventoryLocationId}', {
      path: { supplyItemId, inventoryLocationId: locationId },
      signal,
    }),
    retry: false,
    staleTime: 0,
    gcTime: 0,
  });
}

/**
 * Records the reviewed differences of a location in its history, without moving the stock. An item that already adds up
 * is left as it is, so sending it again records nothing twice. Whatever happened, the review is read again.
 */
export function useReconcileInventoryLedger() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: ReconcileInventoryLedgerBody) => apiRequest('post', '/api/v1/inventory/reconciliation', { body }),
    onSettled: () => client.invalidateQueries({ queryKey: reconciliationKeys.all }),
  });
}
