import { useQuery } from '@tanstack/react-query';
import { platformApi } from '../api/platformApi';
import { useAuth } from './useAuth';
import { useTenant } from './useTenant';
import { getAccess } from '../utils/access';

export const mySubscriptionKey = ['me', 'subscription'] as const;

/** Plan and billing of the current business; only owners and admins read it. */
export function useMySubscription() {
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const canRead = getAccess(user, capabilities).businessSettings && !!tenant;
  return useQuery({
    queryKey: [...mySubscriptionKey, tenant?.id ?? null],
    queryFn: platformApi.getMySubscription,
    enabled: canRead,
    staleTime: 5 * 60 * 1000,
  });
}
