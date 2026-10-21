import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSyncExternalStore } from 'react';
import { getApiErrorStatus } from './errors';
import type { InventoryCountStatus } from './generated/enums';
import { apiRequest, type Schemas } from './typedClient';

// Conteo físico (Fase 27A, ADR 0037): the counts of a location, their items, and starting, counting, adding, posting and
// cancelling them. Kept out of queries.ts, which the first visit loads.

export type InventoryCountListItem = Schemas['InventoryCountListItemDto'];
export type InventoryCount = Schemas['InventoryCountDto'];
export type InventoryCountLine = Schemas['InventoryCountLineDto'];
export type InventoryCountLinesPage = Schemas['InventoryCountLinesPage'];
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

/** The list as it opens: every count, the newest first. */
export const INITIAL_COUNTS_FILTER: InventoryCountsFilter = { status: '', locationId: '', search: '', page: 1, pageSize: 10 };

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
  /** Every page of the items of a count, whatever its filter. */
  linesOf: (id: string) => ['inventory-counts', 'count-lines', id] as const,
  lines: (id: string, filter: InventoryCountLinesFilter) => ['inventory-counts', 'count-lines', id, filter] as const,
  /** The saves of a count in flight: the review waits for them. */
  record: (id: string) => ['inventory-counts', 'record', id] as const,
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

/** The items of a count by name, a page at a time, with the revision of the count they were read at. */
export function useInventoryCountLines(id: string, filter: InventoryCountLinesFilter, enabled = true) {
  return useQuery({
    enabled,
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
 * Saves what was counted of some items. The items it answers replace theirs in every page already read, without reading
 * the pages again, so the boxes being typed in do not jump; those pages are left stale, and the next one shown (the
 * review, or the sheet again) is read anew. The count is replaced only by a newer revision: an answer that arrives late
 * changes nothing and the pages are read again.
 */
export function useRecordInventoryCount(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationKey: countKeys.record(id),
    mutationFn: (lines: InventoryCountEntry[]) =>
      apiRequest('put', '/api/v1/inventory/counts/{id}/lines', { path: { id }, body: { lines } }),
    onSuccess: (recorded) => {
      const cached = client.getQueryData<InventoryCount>(countKeys.count(id));
      if (cached && cached.revision > recorded.count.revision) {
        void client.invalidateQueries({ queryKey: countKeys.linesOf(id) });
      } else {
        client.setQueryData(countKeys.count(id), recorded.count);
        const answered = new Map(recorded.lines.map((line) => [line.id, line]));
        client.setQueriesData<InventoryCountLinesPage>({ queryKey: countKeys.linesOf(id) }, (page) => page && {
          ...page,
          items: page.items.map((line) => answered.get(line.id) ?? line),
        });
        void client.invalidateQueries({ queryKey: countKeys.linesOf(id), refetchType: 'none' });
      }
      void client.invalidateQueries({ queryKey: ['inventory-counts', 'list'] });
    },
    // Posted or cancelled by someone else: reading the count again closes the sheet.
    onError: (err) => {
      if (getApiErrorStatus(err) === 409) void client.invalidateQueries({ queryKey: countKeys.count(id) });
    },
  });
}

/**
 * How many saves of the count are in flight: the review waits for them. What `useIsMutating` does, over the query client
 * the first visit already loads, so its code stays out of that visit.
 */
export function useSavingInventoryCount(id: string) {
  const client = useQueryClient();
  return useSyncExternalStore(
    (notify) => client.getMutationCache().subscribe(notify),
    () => client.isMutating({ mutationKey: countKeys.record(id) }),
  );
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
