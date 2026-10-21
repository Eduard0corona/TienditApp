import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { InventoryCountStatus } from './generated/enums';
import { apiRequest, type Schemas } from './typedClient';

// Conteo físico (Fase 27A, ADR 0037): the counts of a location, their items, and starting, counting, adding, posting and
// cancelling them. Kept out of queries.ts, which the first visit loads.

export type InventoryCountListItem = Schemas['InventoryCountListItemDto'];
export type InventoryCount = Schemas['InventoryCountDto'];
export type InventoryCountLine = Schemas['InventoryCountLineDto'];
export type InventoryCountEntry = Schemas['InventoryCountEntry'];
export type RecordedInventoryCount = Schemas['RecordedInventoryCountDto'];
export type StartInventoryCountBody = Schemas['StartInventoryCountCommand'];

export interface InventoryCountsFilter {
  /** A status, or '' for all. */
  status: InventoryCountStatus | '';
  /** A location, or '' for all. */
  locationId: string;
  /** Folio or location. */
  search: string;
  page: number;
  pageSize: number;
}

export interface InventoryCountLinesFilter {
  /** Name, code, SKU or barcode: a scanner types it. */
  search: string;
  /** Only the items not counted yet. */
  pending: boolean;
  /** Only the counted items whose quantity is not what the system had. */
  withDifference: boolean;
  page: number;
  pageSize: number;
}

/** Cache keys of the counts: what changes a count reads the list, the count and its items again. */
export const countKeys = {
  all: ['inventory-counts'] as const,
  list: (filter: InventoryCountsFilter) => ['inventory-counts', 'list', filter] as const,
  count: (id: string) => ['inventory-counts', 'count', id] as const,
  lines: (id: string, filter: InventoryCountLinesFilter) => ['inventory-counts', 'count-lines', id, filter] as const,
};

/** The counts of the business, the newest first. */
export function useInventoryCounts(filter: InventoryCountsFilter) {
  return useQuery({
    queryKey: countKeys.list(filter),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/inventory/counts', {
      query: {
        status: filter.status === '' ? undefined : filter.status,
        inventoryLocationId: filter.locationId || undefined,
        search: filter.search || undefined,
        pageNumber: filter.page,
        pageSize: filter.pageSize,
      },
      signal,
    }),
    placeholderData: keepPreviousData,
  });
}

/** A count with how many items were counted, how many differ and what the differences are worth. */
export function useInventoryCount(id: string) {
  return useQuery({
    queryKey: countKeys.count(id),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/inventory/counts/{id}', { path: { id }, signal }),
    retry: false,
  });
}

/** The items of a count by name, a page at a time. */
export function useInventoryCountLines(id: string, filter: InventoryCountLinesFilter) {
  return useQuery({
    queryKey: countKeys.lines(id, filter),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/inventory/counts/{id}/lines', {
      path: { id },
      query: {
        search: filter.search || undefined,
        pending: filter.pending || undefined,
        withDifference: filter.withDifference || undefined,
        pageNumber: filter.page,
        pageSize: filter.pageSize,
      },
      signal,
    }),
    placeholderData: keepPreviousData,
  });
}

function useRefreshCounts() {
  const client = useQueryClient();
  return () => client.invalidateQueries({ queryKey: countKeys.all });
}

/** Starts the count of a location: what it has stock of (all or one category), or the items chosen. */
export function useStartInventoryCount() {
  const refresh = useRefreshCounts();
  return useMutation({
    mutationFn: (body: StartInventoryCountBody) => apiRequest('post', '/api/v1/inventory/counts', { body }),
    onSuccess: refresh,
  });
}

/**
 * Saves what was counted of some items, each with what the system has of it right now. The count it answers replaces the
 * one in the cache; the lists of items are left as they are, so the inputs being typed in do not jump.
 */
export function useRecordInventoryCount(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (lines: InventoryCountEntry[]) =>
      apiRequest('put', '/api/v1/inventory/counts/{id}/lines', { path: { id }, body: { lines } }),
    onSuccess: (recorded) => {
      client.setQueryData(countKeys.count(id), recorded.count);
      void client.invalidateQueries({ queryKey: ['inventory-counts', 'list'] });
    },
  });
}

/** Adds what turned up on the shelf; an item the count already has stays as it is. */
export function useAddInventoryCountItems(id: string) {
  const refresh = useRefreshCounts();
  return useMutation({
    mutationFn: (supplyItemIds: string[]) =>
      apiRequest('post', '/api/v1/inventory/counts/{id}/items', { path: { id }, body: { supplyItemIds } }),
    onSuccess: refresh,
  });
}

/** Posts the count reviewed at this revision: each difference moves the stock of now. */
export function usePostInventoryCount(id: string) {
  const refresh = useRefreshCounts();
  return useMutation({
    mutationFn: (expectedRevision: number) =>
      apiRequest('post', '/api/v1/inventory/counts/{id}/post', { path: { id }, body: { expectedRevision } }),
    // Refused because the count changed, it is read again too.
    onSettled: refresh,
  });
}

/** Cancels an open count: nothing moves. */
export function useCancelInventoryCount(id: string) {
  const refresh = useRefreshCounts();
  return useMutation({
    mutationFn: (reason: string) =>
      apiRequest('post', '/api/v1/inventory/counts/{id}/cancel', { path: { id }, body: { reason: reason || null } }),
    onSettled: refresh,
  });
}
