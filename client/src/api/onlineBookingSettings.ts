import { useQuery } from '@tanstack/react-query';
import { useTenant } from '../hooks/useTenant';
import { TenantCapability, type TenantProfileDto } from '../types';
import { hasCapability } from '../utils/tenantCapabilities';
import { appointmentsApi } from './appointmentsApi';

/**
 * Whether the owner's business books appointments online (Fase 24D). Publishing or turning off the menu, or giving the
 * business its hours, changes whether customers book: asked again with each.
 */
export const onlineBookingSettingsKey = (tenant: TenantProfileDto | null | undefined) =>
  ['online-booking-settings', tenant?.onlineMenuMode, (tenant?.openingHours?.length ?? 0) > 0] as const;

/** The online booking settings, for owners and admins of a business with the agenda (no request without it). */
export function useOnlineBookingSettings() {
  const { tenant, capabilities } = useTenant();
  const withAgenda = hasCapability(capabilities, TenantCapability.Appointments);
  const query = useQuery({
    queryKey: onlineBookingSettingsKey(tenant),
    queryFn: ({ signal }) => appointmentsApi.getOnlineBooking(signal),
    enabled: !!tenant && withAgenda,
  });
  return { ...query, withAgenda };
}
