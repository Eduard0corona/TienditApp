import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import type { OrderChannel, PlatformSettlementStatus } from '../types';
import { apiRequest, type Schemas } from './typedClient';

// Reportes › Liquidaciones only: kept out of queries.ts, which the first visit loads.

export type PlatformSettlement = Schemas['PlatformSettlementDto'];
export type PlatformSettlementDetail = Schemas['PlatformSettlementDetailDto'];
export type PlatformSettlementOrder = Schemas['PlatformSettlementOrderDto'];
export type PlatformSettlementAdjustment = Schemas['PlatformSettlementAdjustmentDto'];
export type PlatformReceivable = Schemas['PlatformReceivableDto'];
export type CreatePlatformSettlementBody = Schemas['CreatePlatformSettlementRequest'];
export type UpdatePlatformSettlementBody = Schemas['UpdatePlatformSettlementRequest'];
export type PlatformStatementMatch = Schemas['PlatformStatementMatchDto'];
export type PlatformStatementFolio = Schemas['PlatformStatementFolioDto'];
export type PlatformStatementLine = Schemas['PlatformStatementLineBody'];
export type MatchPlatformStatementBody = Schemas['MatchPlatformStatementRequest'];

/** Cache keys of the settlements: every change reads the lists and what each platform owes again. */
export const settlementKeys = {
  all: ['platform-settlements'] as const,
  list: (filters: PlatformSettlementFilters, page: number, pageSize: number) =>
    ['platform-settlements', 'list', filters.channel ?? 'all', filters.status ?? 'all', filters.search, page, pageSize] as const,
  receivables: ['platform-settlements', 'receivable'] as const,
  pending: (channel: OrderChannel, from: string, to: string) => ['platform-settlements', 'pending', channel, from, to] as const,
  detail: (id: string) => ['platform-settlements', 'detail', id] as const,
};

/** What each platform still owes for its orders not settled yet, with the open settlement of each. */
export function usePlatformReceivables() {
  return useQuery({
    queryKey: settlementKeys.receivables,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/platform-settlements/receivable', { signal }),
  });
}

/** What the list of settlements shows: one platform or status, or a text of their reference or notes ('' for any). */
export interface PlatformSettlementFilters {
  channel: OrderChannel | null;
  status: PlatformSettlementStatus | null;
  search: string;
}

/** The settlements, the newest period first, with the filters chosen (searched on the server, over every page). */
export function usePlatformSettlements(filters: PlatformSettlementFilters, page: number, pageSize: number) {
  return useQuery({
    queryKey: settlementKeys.list(filters, page, pageSize),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/platform-settlements', {
      query: {
        channel: filters.channel ?? undefined,
        status: filters.status ?? undefined,
        search: filters.search || undefined,
        pageNumber: page,
        pageSize,
      },
      signal,
    }),
    // Another page or filter keeps this one on screen until it arrives.
    placeholderData: keepPreviousData,
  });
}

/**
 * The pending orders of a platform for a settlement of the period (yyyy-mm-dd), the oldest first: those of the period and,
 * in the room left of one settlement, the most recent of before; with the open settlement of that platform if there is
 * one. Another period loads its own orders from nothing: the ones of the period before would be saved with the new one.
 */
export function usePendingPlatformOrders(channel: OrderChannel, from: string, to: string, enabled = true) {
  return useQuery({
    queryKey: settlementKeys.pending(channel, from, to),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/platform-settlements/pending-orders', {
      query: { channel, from, to },
      signal,
    }),
    enabled,
  });
}

/**
 * A settlement with its orders and adjustments and, while open, the pending orders of its period it could add. Read when
 * the screen opens and then only when asked (after a save, or to see someone else's): the screen opens again with each
 * revision, and a read in the background would drop what the person is writing.
 */
export function usePlatformSettlement(id: string) {
  return useQuery({
    queryKey: settlementKeys.detail(id),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/platform-settlements/{id}', { path: { id }, signal }),
    staleTime: Infinity,
    refetchOnMount: 'always',
    refetchOnReconnect: false,
  });
}

/** The settlement as the server answered it replaces the one read; the lists and what is owed are read again. */
function settled(client: QueryClient, detail: PlatformSettlementDetail) {
  client.setQueryData(settlementKeys.detail(detail.settlement.id), detail);
  void client.invalidateQueries({
    queryKey: settlementKeys.all,
    predicate: (query) => query.queryKey[1] !== 'detail',
  });
}

/** Opens a settlement of a platform's deposit (owners and admins). */
export function useCreatePlatformSettlement() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: CreatePlatformSettlementBody) => apiRequest('post', '/api/v1/platform-settlements', { body }),
    onSuccess: (detail) => settled(client, detail),
  });
}

/** Saves an open settlement whole with the revision the person saw (409 when someone else saved it meanwhile). */
export function useUpdatePlatformSettlement() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdatePlatformSettlementBody }) =>
      apiRequest('put', '/api/v1/platform-settlements/{id}', { path: { id }, body }),
    onSuccess: (detail) => settled(client, detail),
  });
}

/**
 * Closes a settlement whose difference is $0. A 409 says someone saved it meanwhile or what an order brings changed (the
 * server saved the new figures): either way the settlement is read again.
 */
export function useClosePlatformSettlement() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, revision }: { id: string; revision: number }) =>
      apiRequest('post', '/api/v1/platform-settlements/{id}/close', { path: { id }, body: { revision } }),
    onSuccess: (detail) => settled(client, detail),
  });
}

/** Voids an open or closed settlement with a reason: it stays as history and its orders are pending again. */
export function useVoidPlatformSettlement() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, revision, reason }: { id: string; revision: number; reason: string }) =>
      apiRequest('post', '/api/v1/platform-settlements/{id}/void', { path: { id }, body: { revision, reason } }),
    onSuccess: (detail) => settled(client, detail),
  });
}

/**
 * A platform's statement against its orders (Fase 22H): read on demand, never kept, since it saves nothing and the person
 * applies it to what they are writing.
 */
export function useMatchPlatformStatement() {
  return useMutation({
    mutationFn: (body: MatchPlatformStatementBody) =>
      apiRequest('post', '/api/v1/platform-settlements/statement-matches', { body }),
  });
}
