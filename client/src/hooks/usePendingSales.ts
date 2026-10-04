import { useCallback, useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ordersApi } from '../api/ordersApi';
import { listPendingSales, PENDING_SALES_EVENT, syncPendingSales, type PendingSale, type SyncResult } from '../offline/saleQueue';
import { useAuth } from './useAuth';
import { useTenant } from './useTenant';

const PENDING_SALES_KEY = ['offline', 'pendingSales'] as const;
const NO_SALES: PendingSale[] = [];

/** Sales this cashier charged without connection in the current business, and the way to send them. */
export function usePendingSales() {
  const { user } = useAuth();
  const { tenant } = useTenant();
  const queryClient = useQueryClient();
  const tenantId = tenant?.id;
  const userId = user?.id;

  const { data: sales = NO_SALES } = useQuery({
    queryKey: [...PENDING_SALES_KEY, tenantId, userId],
    queryFn: () => listPendingSales(tenantId!, userId!),
    enabled: !!tenantId && !!userId,
    // They live in this device's IndexedDB: reading them must not wait for the connection.
    networkMode: 'always',
    staleTime: 0,
  });

  useEffect(() => {
    const handler = () => void queryClient.invalidateQueries({ queryKey: PENDING_SALES_KEY });
    window.addEventListener(PENDING_SALES_EVENT, handler);
    return () => window.removeEventListener(PENDING_SALES_EVENT, handler);
  }, [queryClient]);

  const sync = useCallback(async (): Promise<SyncResult | null> => {
    if (!tenantId || !userId || !navigator.onLine) return null;
    return syncPendingSales(tenantId, userId, async (request) => (await ordersApi.create(request)).data);
  }, [tenantId, userId]);

  return {
    sales,
    pendingCount: sales.filter((sale) => sale.status === 'pending').length,
    failedCount: sales.filter((sale) => sale.status === 'failed').length,
    sync,
    tenantId,
    userId,
  };
}
