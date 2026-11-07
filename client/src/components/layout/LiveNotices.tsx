import { useAppointmentRequestsLive } from '../../hooks/useAppointmentRequests';
import { useQuoteRepliesLive } from '../../hooks/useQuoteReplies';

/**
 * The live notices of the business, for whoever handles each one: the requests online of the agenda (Fase 29N) and the
 * answers to quotes (Fase 29R). The top bar loads it apart, after the first screen: it draws nothing.
 */
export default function LiveNotices({ tenantId, requests, quotes }: { tenantId: string; requests: boolean; quotes: boolean }) {
  useAppointmentRequestsLive(requests, tenantId);
  useQuoteRepliesLive(quotes, tenantId);
  return null;
}
