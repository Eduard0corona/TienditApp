import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, type Schemas } from './typedClient';

// Configuración › Pagos en línea only (Fase 24I): kept out of queries.ts, which the first visit loads.

export type OnlinePaymentSettings = Schemas['OnlinePaymentSettingsDto'];

export const onlinePaymentSettingsKey = ['online-payments'] as const;

/** The business's Mercado Pago account and whether the menu offers to pay online (owners and admins). */
export function useOnlinePaymentSettings() {
  return useQuery({
    queryKey: onlinePaymentSettingsKey,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/online-payments', { signal }),
  });
}

/** Where to send the owner to authorize TienditApp in Mercado Pago; it comes back here with the code and the state. */
export function useStartOnlinePaymentConnection() {
  return useMutation({
    mutationFn: () => apiRequest('post', '/api/v1/online-payments/connect'),
  });
}

/** Each change answers the settings as they are now. */
function useSettingsChange<TVariables>(change: (variables: TVariables) => Promise<OnlinePaymentSettings>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: change,
    onSuccess: (settings) => client.setQueryData(onlinePaymentSettingsKey, settings),
    onError: () => client.invalidateQueries({ queryKey: onlinePaymentSettingsKey }),
  });
}

/** Finishes connecting the account with what Mercado Pago sent back: once, within ten minutes, by whoever started it. */
export function useCompleteOnlinePaymentConnection() {
  return useSettingsChange((body: { state: string; code: string }) =>
    apiRequest('post', '/api/v1/online-payments/connect/complete', { body }));
}

export function useSetOnlinePaymentsAccepted() {
  return useSettingsChange((accepts: boolean) =>
    apiRequest('put', '/api/v1/online-payments/accepts-payments', { body: { accepts } }));
}

/** Disconnects the account and deletes its keys; not while payments or refunds are in course. */
export function useDisconnectOnlinePayments() {
  return useSettingsChange(() => apiRequest('delete', '/api/v1/online-payments/account'));
}
