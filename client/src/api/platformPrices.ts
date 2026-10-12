import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { OrderChannel } from '../types';
import { queryKeys } from './queries';
import { apiRequest, type Schemas } from './typedClient';

// Catálogo › Precios en apps only: kept out of queries.ts, which the first visit loads.

// The largest page the API serves: the whole catalog of nearly every business in one read.
const PRICE_LIST_PAGE_SIZE = 500;

/**
 * Catálogo › Precios en apps: the products (no services) with what each costs in the menu and in the app of a platform,
 * and so their options, by category and name (whoever manages the catalog).
 */
export function usePlatformPrices(channel: OrderChannel, enabled = true) {
  return useQuery({
    queryKey: queryKeys.platformPrices(channel),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/delivery-platforms/{channel}/prices', {
      path: { channel },
      query: { pageNumber: 1, pageSize: PRICE_LIST_PAGE_SIZE },
      signal,
    }),
    enabled,
  });
}

/**
 * Saves prices in the app of a platform: each product listed takes its price (null: the menu's) and each option listed
 * what it adds (null: what it adds in the menu); the list of that platform is read again.
 */
export function useSavePlatformPrices() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ channel, body }: { channel: OrderChannel; body: Schemas['SavePlatformPricesRequest'] }) =>
      apiRequest('put', '/api/v1/delivery-platforms/{channel}/prices', { path: { channel }, body }),
    onSettled: (_result, _error, { channel }) => client.invalidateQueries({ queryKey: queryKeys.platformPrices(channel) }),
  });
}
