import { useQuoteRepliesLive } from '../../hooks/useQuoteReplies';

/**
 * Fase 29R: the live answers to the quotes of the business, for whoever works them. The top bar loads it apart, after the
 * first screen: it draws nothing.
 */
export default function QuoteRepliesLive({ tenantId }: { tenantId: string }) {
  useQuoteRepliesLive(true, tenantId);
  return null;
}
