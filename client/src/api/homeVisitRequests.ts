import { useQuery } from '@tanstack/react-query';
import { useTenant } from '../hooks/useTenant';
import { TenantCapability, type TenantProfileDto } from '../types';
import { hasCapability } from '../utils/tenantCapabilities';
import { appointmentsApi } from './appointmentsApi';

/**
 * Visits to quote of the owner's business (Fase 29A). Publishing or turning off the menu, or giving the business its
 * hours, changes whether it receives them: asked again with each.
 */
export const homeVisitRequestsKey = (tenant: TenantProfileDto | null | undefined) =>
  ['home-visit-requests', tenant?.onlineMenuMode, (tenant?.openingHours?.length ?? 0) > 0] as const;

/**
 * The visit settings, for owners and admins of a business with the agenda (null without it). What decides them also
 * changes on other screens (its zones, its catalog, its branches), so they are read again each time a screen opens.
 */
export function useHomeVisitRequests() {
  const { tenant, capabilities } = useTenant();
  const withAgenda = hasCapability(capabilities, TenantCapability.Appointments);
  const query = useQuery({
    queryKey: homeVisitRequestsKey(tenant),
    queryFn: ({ signal }) => appointmentsApi.getHomeVisitRequests(signal),
    enabled: !!tenant && withAgenda,
    staleTime: 0,
  });
  return { ...query, withAgenda };
}
