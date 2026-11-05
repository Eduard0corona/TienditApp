import { useAppointmentRequestsLive } from '../../hooks/useAppointmentRequests';

/**
 * Fase 29N: the live requests online of the business, for whoever answers them. The top bar loads it apart, after the first
 * screen: it draws nothing.
 */
export default function AppointmentRequestsLive({ tenantId }: { tenantId: string }) {
  useAppointmentRequestsLive(true, tenantId);
  return null;
}
