import { OnlinePaymentDispute, OnlinePaymentRefundReason, OnlinePaymentRefundStatus } from '../api/generated/enums';

/**
 * What became of an «En línea» refund (Fase 24I): Mercado Pago gives it back from the queue, and may refuse it, in which
 * case the business gives the money back from its Mercado Pago account. Since the Fase 24K a refund may also have been made
 * in Mercado Pago itself: from the business's account, by Mercado Pago for a claim, or by the customer's bank (a chargeback).
 * Null when what became of it is not known (the order as the list gives it): nothing is said rather than a guess.
 */
export function describeOnlineRefund(
  status: OnlinePaymentRefundStatus | null | undefined,
  failure?: string | null,
  reason?: OnlinePaymentRefundReason | null,
): { text: string; tone: 'muted' | 'success' | 'warning' | 'danger' } | null {
  switch (reason) {
    case OnlinePaymentRefundReason.InMercadoPago:
      return { text: 'Se hizo en Mercado Pago.', tone: 'success' };
    case OnlinePaymentRefundReason.Claim:
      return { text: 'Mercado Pago lo devolvió por una reclamación.', tone: 'warning' };
    case OnlinePaymentRefundReason.Chargeback:
      return { text: 'El banco se lo regresó al cliente (contracargo).', tone: 'danger' };
  }
  switch (status) {
    case OnlinePaymentRefundStatus.Requested:
      return { text: 'Mercado Pago lo está devolviendo.', tone: 'muted' };
    case OnlinePaymentRefundStatus.Completed:
      return { text: 'Mercado Pago ya lo devolvió.', tone: 'success' };
    case OnlinePaymentRefundStatus.DoneInMercadoPago:
      return { text: 'Mercado Pago no lo devolvió al pedirlo; después se devolvió en tu cuenta de Mercado Pago.', tone: 'success' };
    case OnlinePaymentRefundStatus.Failed:
      return {
        text: `${failure?.trim() || 'Mercado Pago no lo devolvió.'} Devuélvelo desde tu cuenta de Mercado Pago.`,
        tone: 'danger',
      };
    default:
      return null;
  }
}

/** Whether a refund of the order is the money a claim or a chargeback took back. */
export function isDisputeRefund(reason: OnlinePaymentRefundReason | null | undefined): boolean {
  return reason === OnlinePaymentRefundReason.Claim || reason === OnlinePaymentRefundReason.Chargeback;
}

/**
 * The chargeback or claim on the payment of an order paid online (Fase 24K), as Mercado Pago last reported it: while it is
 * open Mercado Pago holds the money; lost, the money went back to the customer; won, it stays with the business.
 * `refunded` says whether the order has the refund of a claim or a chargeback: a lost one may have none (the business had
 * already given everything back), and a won one may keep the one recorded when it was lost.
 */
export function describeOnlineDispute(
  dispute: OnlinePaymentDispute | null | undefined,
  refunded = false,
): { title: string; text: string; variant: 'warning' | 'danger' | 'success' } | null {
  switch (dispute) {
    case OnlinePaymentDispute.Open:
      return {
        title: 'Pago en disputa',
        text: 'El cliente reclamó el pago o su banco lo desconoció. Mercado Pago retiene el dinero mientras se resuelve; atiende la disputa en tu cuenta de Mercado Pago.',
        variant: 'warning',
      };
    case OnlinePaymentDispute.Lost:
      return {
        title: 'Disputa perdida',
        text: refunded
          ? 'Se resolvió a favor del cliente: el dinero regresó al cliente y salió de tu cuenta de Mercado Pago. Lo ves en los reembolsos del pedido.'
          : 'Se resolvió a favor del cliente: el dinero regresó al cliente y salió de tu cuenta de Mercado Pago. Revisa el detalle en tu cuenta de Mercado Pago.',
        variant: 'danger',
      };
    case OnlinePaymentDispute.Won:
      return {
        title: 'Disputa ganada',
        text: refunded
          ? 'Se resolvió a tu favor, pero el reembolso de la disputa sigue anotado en el pedido. Confirma en tu cuenta de Mercado Pago que el dinero te llegó.'
          : 'Se resolvió a tu favor: el dinero se queda contigo.',
        variant: 'success',
      };
    default:
      return null;
  }
}
