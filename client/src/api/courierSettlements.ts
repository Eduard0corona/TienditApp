import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { DeliveryCourier } from '../types';
import { apiRequest, type Schemas } from './typedClient';

// Caja › Repartidores only: kept out of queries.ts, which the first visit loads.

export type CourierReceivable = Schemas['CourierReceivableDto'];
export type CourierOrder = Schemas['CourierOrderDto'];
export type PendingCourierOrders = Schemas['PendingCourierOrdersDto'];
export type CourierSettlement = Schemas['CourierSettlementDto'];
export type CourierSettlementDetail = Schemas['CourierSettlementDetailDto'];
export type CreateCourierSettlementBody = Schemas['CreateCourierSettlementRequest'];
export type CourierSettlementAdjustmentBody = Schemas['CourierSettlementAdjustmentRequest'];

/** Cache keys of what the couriers owe: a settlement reads all of them again. */
export const courierKeys = {
  all: ['courier-settlements'] as const,
  receivables: ['courier-settlements', 'receivable'] as const,
  pending: (courier: DeliveryCourier) => ['courier-settlements', 'pending', courier] as const,
  list: (page: number, pageSize: number) => ['courier-settlements', 'list', page, pageSize] as const,
  detail: (id: string) => ['courier-settlements', 'detail', id] as const,
};

/** What each courier owes for the orders it collected and delivered, and what it carries on its way. */
export function useCourierReceivables(enabled = true) {
  return useQuery({
    queryKey: courierKeys.receivables,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/courier-settlements/receivable', { signal }),
    enabled,
  });
}

/** The orders a courier owes, the oldest first and as many as one settlement takes, and those on their way. */
export function usePendingCourierOrders(courier: DeliveryCourier, enabled = true) {
  return useQuery({
    queryKey: courierKeys.pending(courier),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/courier-settlements/pending-orders', { query: { courier }, signal }),
    enabled,
  });
}

/** The settlements, the newest first. */
export function useCourierSettlements(page: number, pageSize: number, enabled = true) {
  return useQuery({
    queryKey: courierKeys.list(page, pageSize),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/courier-settlements', {
      query: { pageNumber: page, pageSize },
      signal,
    }),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/** A settlement with the orders it paid and its adjustments. */
export function useCourierSettlement(id: string | null) {
  return useQuery({
    queryKey: courierKeys.detail(id ?? ''),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/courier-settlements/{id}', { path: { id: id! }, signal }),
    enabled: !!id,
  });
}

/**
 * The courier hands over the cash: the orders are charged in the drawer of whoever receives it and the adjustments enter it
 * as movements. A 409 says an order is no longer owed (read the list again).
 */
export function useCreateCourierSettlement() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateCourierSettlementBody) => apiRequest('post', '/api/v1/courier-settlements', { body }),
    onSuccess: (detail) => {
      client.setQueryData(courierKeys.detail(detail.settlement.id), detail);
      void client.invalidateQueries({ queryKey: courierKeys.all, predicate: (query) => query.queryKey[1] !== 'detail' });
    },
    // A conflict means the list changed: it is read again either way.
    onError: () => void client.invalidateQueries({ queryKey: courierKeys.all, predicate: (query) => query.queryKey[1] !== 'detail' }),
  });
}
