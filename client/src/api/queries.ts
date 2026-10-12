import { useCallback } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { OrderChannel } from '../types';
import { apiRequest, type Schemas } from './typedClient';

/** Cache keys of the server state read through TanStack Query. */
export const queryKeys = {
  myAccess: ['me', 'access'] as const,
  customerCard: (phone?: string, customerId?: string) => ['customers', 'card', phone ?? '', customerId ?? ''] as const,
  campaigns: (page: number) => ['messaging', 'campaigns', page] as const,
  campaignMessages: (campaignId: string, page: number) => ['messaging', 'campaigns', campaignId, 'messages', page] as const,
  messagingSettings: ['messaging', 'settings'] as const,
  reminderHistory: ['messaging', 'reminders', 'history'] as const,
  messagingTemplates: (purpose?: number) => ['messaging', 'templates', purpose ?? 'all'] as const,
  cfdiDraft: (orderId: string, receiver?: CfdiReceiver) => ['orders', orderId, 'cfdi-draft', receiver ?? null] as const,
  attention: ['attention'] as const,
  currentCashSession: ['cash', 'current-session'] as const,
  specialDays: ['special-days'] as const,
  deliveryPlatforms: ['delivery-platforms'] as const,
  deliveryPlatformChannels: ['delivery-platforms', 'channels'] as const,
  platformPrices: (channel: OrderChannel) => ['delivery-platforms', channel, 'prices'] as const,
};

// The bell and the drawer chip stay current without a live connection of their own.
const ATTENTION_REFRESH_MS = 60_000;

/** Fiscal data of the customer an invoice is for (CFDI 4.0 receptor). */
export interface CfdiReceiver {
  rfc: string;
  name: string;
  postalCode: string;
  fiscalRegime: string;
  cfdiUse: string;
}

/** Preview of the invoice of an order; without a receiver it goes to the general public. */
export function useCfdiDraft(orderId: string, receiver?: CfdiReceiver) {
  return useQuery({
    queryKey: queryKeys.cfdiDraft(orderId, receiver),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/orders/{id}/cfdi-draft', {
      path: { id: orderId },
      query: receiver,
      signal,
    }),
    retry: false,
  });
}

/** What needs someone now, counted by the API only for what the person may handle (the bell and Inicio). */
export function useAttention(enabled = true) {
  return useQuery({
    queryKey: queryKeys.attention,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/dashboard/attention', { signal }),
    enabled,
    refetchInterval: ATTENTION_REFRESH_MS,
  });
}

/** The person's open drawer, or null: the chip in the top bar. */
export function useCurrentCashSession(enabled = true) {
  return useQuery({
    queryKey: queryKeys.currentCashSession,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/cash/current-session', { signal }),
    enabled,
    refetchInterval: ATTENTION_REFRESH_MS,
  });
}

/** After a drawer opens, closes or moves money: the drawer chip and the bell read it again. */
export function useRefreshCashState() {
  const queryClient = useQueryClient();
  return useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.currentCashSession });
    void queryClient.invalidateQueries({ queryKey: queryKeys.attention });
  }, [queryClient]);
}

/**
 * The delivery platforms the business set up (Rappi, Uber Eats, DiDi Food), active or not, without their rates: Vender
 * offers the active ones and Pedidos filters by them. Whoever takes orders or reads reports may ask.
 */
export function useDeliveryPlatformChannels(enabled = true) {
  return useQuery({
    queryKey: queryKeys.deliveryPlatformChannels,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/delivery-platforms/channels', { signal }),
    enabled,
  });
}

/**
 * The three delivery platforms with the terms of their contracts and an example worked out by the server (owners and
 * admins): the ones never saved come without a commission.
 */
export function useDeliveryPlatforms(enabled = true) {
  return useQuery({
    queryKey: queryKeys.deliveryPlatforms,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/delivery-platforms', { signal }),
    enabled,
  });
}

/**
 * Saves one platform with the revision read (409 when someone else saved it meanwhile). The answer is the whole list:
 * it replaces the one read, and Vender and Pedidos read again which platforms are active.
 */
export function useSaveDeliveryPlatform() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ channel, body }: { channel: OrderChannel; body: Schemas['SaveDeliveryPlatformRequest'] }) =>
      apiRequest('put', '/api/v1/delivery-platforms/{channel}', { path: { channel }, body }),
    onSuccess: (platforms) => {
      client.setQueryData(queryKeys.deliveryPlatforms, platforms);
      void client.invalidateQueries({ queryKey: queryKeys.deliveryPlatformChannels });
    },
  });
}

/** What the server says the user can do now (fresher than the token's claims). */
export function useMyAccess(enabled = true) {
  return useQuery({
    queryKey: queryKeys.myAccess,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/me/access', { signal }),
    enabled,
    staleTime: 60_000,
  });
}

export function useCustomerCard(phone?: string, customerId?: string) {
  return useQuery({
    queryKey: queryKeys.customerCard(phone, customerId),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/customers/card', { query: { phone, customerId }, signal }),
    enabled: Boolean(phone || customerId),
  });
}

export function useCampaigns(page: number, pageSize: number) {
  return useQuery({
    queryKey: queryKeys.campaigns(page),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/messaging/campaigns', { query: { pageNumber: page, pageSize }, signal }),
    // Campaigns being sent update by themselves.
    refetchInterval: (query) => query.state.data?.items?.some((campaign) => campaign.status === 0 || campaign.status === 1)
      ? 15_000
      : false,
  });
}

export function useCampaignMessages(campaignId: string, page: number) {
  return useQuery({
    queryKey: queryKeys.campaignMessages(campaignId, page),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/messaging/messages', {
      query: { campaignId, pageNumber: page, pageSize: 20 },
      signal,
    }),
  });
}

export function useCancelCampaign() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (campaignId: string) => apiRequest('post', '/api/v1/messaging/campaigns/{id}/cancel', { path: { id: campaignId } }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['messaging', 'campaigns'] }),
  });
}

export function useCreateCampaign() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: Schemas['CreateCampaignCommand']) => apiRequest('post', '/api/v1/messaging/campaigns', { body }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['messaging', 'campaigns'] }),
  });
}

/** The special days of the business (closed, or open with other hours) and the holidays it could mark (owners and admins). */
export function useSpecialDays(enabled = true) {
  return useQuery({
    queryKey: queryKeys.specialDays,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/auth/my-tenant/special-days', { signal }),
    enabled,
  });
}

// A change answers the whole list again, with its suggestions: it replaces the one read.

export function useSaveSpecialDay() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: Schemas['SaveSpecialDayRequest']) => apiRequest('post', '/api/v1/auth/my-tenant/special-days', { body }),
    onSuccess: (specialDays) => client.setQueryData(queryKeys.specialDays, specialDays),
  });
}

export function useUpdateSpecialDay() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: Schemas['UpdateSpecialDayRequest'] }) =>
      apiRequest('put', '/api/v1/auth/my-tenant/special-days/{id}', { path: { id }, body }),
    onSuccess: (specialDays) => client.setQueryData(queryKeys.specialDays, specialDays),
  });
}

/** The revision is the one read: when someone else changed the special day meanwhile, the API answers 409. */
export function useRemoveSpecialDay() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, revision }: { id: string; revision: number }) =>
      apiRequest('delete', '/api/v1/auth/my-tenant/special-days/{id}', { path: { id }, query: { revision } }),
    onSuccess: (specialDays) => client.setQueryData(queryKeys.specialDays, specialDays),
  });
}
