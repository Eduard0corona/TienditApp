import { OnlinePaymentRefundStatus } from '../api/generated/enums';

/**
 * What became of an «En línea» refund (Fase 24I): Mercado Pago gives it back from the queue, and may refuse it, in which
 * case the business gives the money back from its Mercado Pago account.
 */
export function describeOnlineRefund(
  status: OnlinePaymentRefundStatus | null | undefined,
  failure?: string | null,
): { text: string; tone: 'muted' | 'success' | 'danger' } {
  switch (status) {
    case OnlinePaymentRefundStatus.Completed:
      return { text: 'Mercado Pago ya lo devolvió.', tone: 'success' };
    case OnlinePaymentRefundStatus.Failed:
      return {
        text: `${failure?.trim() || 'Mercado Pago no lo devolvió.'} Devuélvelo desde tu cuenta de Mercado Pago.`,
        tone: 'danger',
      };
    default:
      return { text: 'Mercado Pago lo está devolviendo.', tone: 'muted' };
  }
}
