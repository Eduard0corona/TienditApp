import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
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
};

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
