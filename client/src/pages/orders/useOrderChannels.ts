import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '../../api/typedClient';

/**
 * Where the business receives orders (Fase 29Q), for the Channel filter of Pedidos: the counter, WhatsApp while it has a
 * number, the online menu while it takes orders, and the platforms it set up. The API says it: the client does not know
 * whether the business has a WhatsApp number. Read again each time Pedidos opens (it is cheap), so a number, a menu or a
 * platform set up a moment ago shows at once. It lives with Pedidos, out of the first visit.
 */
export function useOrderChannels(enabled = true) {
  return useQuery({
    queryKey: ['orders', 'channels'],
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/orders/channels', { signal }),
    enabled,
    staleTime: 0,
  });
}
