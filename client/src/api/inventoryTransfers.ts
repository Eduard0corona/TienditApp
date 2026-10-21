import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { countKeys } from './inventoryCounts';
import { apiRequest, type Schemas } from './typedClient';

// Transferencias entre ubicaciones (Fase 27B, ADR 0038): the transfers of the business, one with its items, what an
// origin has to transfer, and making one. Kept out of queries.ts, which the first visit loads.

export type InventoryTransferListItem = Schemas['InventoryTransferListItemDto'];
export type InventoryTransfer = Schemas['InventoryTransferDto'];
export type InventoryTransferLine = Schemas['InventoryTransferLineDto'];
export type CreateInventoryTransferBody = Schemas['CreateInventoryTransferCommand'];
export type InventoryStock = Schemas['InventoryStockDto'];

export interface InventoryTransfersFilter {
  /** Those that left this location or entered it, or '' for all. */
  locationId: string;
  /** Folio or location. */
  search: string;
  page: number;
  pageSize: number;
}

/** The list as it opens: every transfer, the newest first. */
export const INITIAL_TRANSFERS_FILTER: InventoryTransfersFilter = { locationId: '', search: '', page: 1, pageSize: 10 };

/** Cache keys of the transfers: making one reads the list and what the locations have again. */
export const transferKeys = {
  all: ['inventory-transfers'] as const,
  list: (filter: InventoryTransfersFilter) => ['inventory-transfers', 'list', filter] as const,
  transfer: (id: string) => ['inventory-transfers', 'transfer', id] as const,
  originStock: (locationId: string, search: string) => ['inventory-transfers', 'origin-stock', locationId, search] as const,
};

/** The most items an origin offers at once: what the API pages at most. */
const ORIGIN_STOCK_PAGE = 500;

/** The transfers of the business, the newest first. */
export function useInventoryTransfers(filter: InventoryTransfersFilter) {
  return useQuery({
    queryKey: transferKeys.list(filter),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/inventory/transfers', {
      query: {
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

/** A transfer with each item, what it was worth and what each location had before and after. */
export function useInventoryTransfer(id: string) {
  return useQuery({
    queryKey: transferKeys.transfer(id),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/inventory/transfers/{id}', { path: { id }, signal }),
    retry: false,
  });
}

/**
 * What the origin has to transfer: its active items with stock, by name, searched by name, code, SKU or barcode in the
 * server. One page at most: `partial` says the origin has more, which a search finds. Only while the dialog is open, and
 * read again each time it opens, since a sale changes it.
 */
export function useOriginStock(locationId: string, search: string, enabled: boolean) {
  return useQuery({
    enabled: enabled && locationId !== '',
    queryKey: transferKeys.originStock(locationId, search),
    queryFn: async ({ signal }) => {
      const page = await apiRequest('get', '/api/v1/inventory/stocks', {
        query: { inventoryLocationId: locationId, search: search || undefined, pageNumber: 1, pageSize: ORIGIN_STOCK_PAGE },
        signal,
      });
      return {
        rows: page.items
          .filter((stock) => stock.isSupplyActive && stock.onHandQuantity > 0)
          .sort((a, b) => a.supplyItemName.localeCompare(b.supplyItemName, 'es')),
        partial: page.hasNextPage,
      };
    },
    // While another search loads, the previous one of the same origin; never what another origin has.
    placeholderData: (previous, previousQuery) => (previousQuery?.queryKey[2] === locationId ? previous : undefined),
    staleTime: 0,
  });
}

/**
 * Makes a transfer. The body carries the key of the dialog: sent again (a retry, a double tap) it answers the same
 * transfer. Whatever happened, the list, what the origin has and the open counts are read again; the transfer made is
 * already its detail.
 */
export function useCreateInventoryTransfer() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateInventoryTransferBody) => apiRequest('post', '/api/v1/inventory/transfers', { body }),
    onSuccess: (transfer) => client.setQueryData(transferKeys.transfer(transfer.id), transfer),
    onSettled: () => Promise.all([
      client.invalidateQueries({ queryKey: transferKeys.all, refetchType: 'active' }),
      client.invalidateQueries({ queryKey: countKeys.all }),
    ]),
  });
}
