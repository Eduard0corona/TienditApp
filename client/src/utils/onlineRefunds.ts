import { OnlinePaymentDispute, OnlinePaymentRefundReason, OnlinePaymentRefundStatus } from '../api/generated/enums';

/**
 * What became of an «En línea» refund (Fase 24I): Mercado Pago gives it back from the queue, and may refuse it, in which
 * case the business gives the money back from its Mercado Pago account. Since the Fase 24K a refund may also have been made
 * in Mercado Pago itself: from the business's account, by Mercado Pago for a claim, or by the customer's bank (a chargeback).
 */
export function describeOnlineRefund(
  status: OnlinePaymentRefundStatus | null | undefined,
  failure?: string | null,
  reason?: OnlinePaymentRefundReason | null,
): { text: string; tone: 'muted' | 'success' | 'warning' | 'danger' } {
  switch (reason) {
    case OnlinePaymentRefundReason.InMercadoPago:
      return { text: 'Se hizo en Mercado Pago.', tone: 'success' };
    case OnlinePaymentRefundReason.Claim:
      return { text: 'Mercado Pago lo devolvió por una reclamación.', tone: 'warning' };
    case OnlinePaymentRefundReason.Chargeback:
      return { text: 'El banco del cliente lo regresó (contracargo).', tone: 'danger' };
  }
  switch (status) {
    case OnlinePaymentRefundStatus.Completed:
      return { text: 'Mercado Pago ya lo devolvió.', tone: 'success' };
    case OnlinePaymentRefundStatus.DoneInMercadoPago:
      return { text: 'Mercado Pago no lo devolvió; lo devolviste desde tu cuenta de Mercado Pago.', tone: 'success' };
    case OnlinePaymentRefundStatus.Failed:
      return {
        text: `${failure?.trim() || 'Mercado Pago no lo devolvió.'} Devuélvelo desde tu cuenta de Mercado Pago.`,
        tone: 'danger',
      };
    default:
      return { text: 'Mercado Pago lo está devolviendo.', tone: 'muted' };
  }
}

/**
 * The chargeback or claim on the payment of an order paid online (Fase 24K), as Mercado Pago last reported it: while it is
 * open Mercado Pago holds the money; lost, the money went back to the customer; won, it stays with the business.
 */
export function describeOnlineDispute(
  dispute: OnlinePaymentDispute | null | undefined,
): { title: string; text: string; variant: 'warning' | 'danger' | 'success' } | null {
  switch (dispute) {
    case OnlinePaymentDispute.Open:
      return {
        title: 'Pago en disputa',
        text: 'El cliente reclamó el pago o su banco lo desconoció. Mercado Pago retiene el dinero mientras decide; atiende la disputa en tu cuenta de Mercado Pago.',
        variant: 'warning',
      };
    case OnlinePaymentDispute.Lost:
      return {
        title: 'Disputa perdida',
        text: 'Mercado Pago le regresó el dinero al cliente: aparece en los reembolsos del pedido.',
        variant: 'danger',
      };
    case OnlinePaymentDispute.Won:
      return {
        title: 'Disputa ganada',
        text: 'Mercado Pago resolvió a tu favor: el dinero se queda contigo.',
        variant: 'success',
      };
    default:
      return null;
  }
}
