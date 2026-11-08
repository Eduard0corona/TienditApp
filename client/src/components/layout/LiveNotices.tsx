import { useAppointmentRequestsLive } from '../../hooks/useAppointmentRequests';
import { useOnlineOrdersWaitingLive } from '../../hooks/useOnlineOrdersWaiting';
import { useQuoteRepliesLive } from '../../hooks/useQuoteReplies';

/**
 * The live notices of the business, for whoever handles each one: the requests online of the agenda (Fase 29N), the
 * answers to quotes (Fase 29R) and the online orders waiting 15 minutes (Fase 29X). The top bar loads it apart, after the
 * first screen: it draws nothing.
 */
export default function LiveNotices({
  tenantId,
  requests,
  quotes,
  orders,
}: {
  tenantId: string;
  requests: boolean;
  quotes: boolean;
  orders: boolean;
}) {
  useAppointmentRequestsLive(requests, tenantId);
  useQuoteRepliesLive(quotes, tenantId);
  useOnlineOrdersWaitingLive(orders);
  return null;
}
