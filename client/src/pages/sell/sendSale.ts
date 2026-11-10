import { getApiErrorCode, getApiErrorMessage } from '../../api/errors';
import { ordersApi } from '../../api/ordersApi';
import type { ConfirmOptions } from '../../components/feedback/feedbackContexts';
import type { CreateOrderRequest, OrderDto } from '../../types';
import { getChannelLabel } from '../../utils/labels';

/**
 * Sends the sale. A folio of a platform already registered today, or its long id ever, asks first: short folios repeat,
 * so when it is another order it goes again with the same key, confirmed; null when the cashier goes to check instead.
 */
export async function sendSale(
  request: CreateOrderRequest,
  confirm: (options: ConfirmOptions) => Promise<boolean>,
): Promise<OrderDto | null> {
  try {
    const { data } = await ordersApi.create(request);
    return data;
  } catch (err: unknown) {
    if (getApiErrorCode(err) !== 'possible_duplicate_order' || request.confirmPossibleDuplicate) throw err;
    const channel = getChannelLabel(request.channel);
    const confirmed = await confirm({
      title: '¿Es otro pedido?',
      // With a long id either one can repeat, and the server's answer says which; without it, only today's folio.
      message: request.externalOrderId
        ? `${getApiErrorMessage(err, `Ya registraste un pedido de ${channel} con el mismo folio o ID largo.`)} `
          + 'Si no, búscalo en Pedidos.'
        : `Hoy ya registraste un pedido de ${channel} con el folio ${request.externalOrderNumber}. `
          + 'Los folios cortos se repiten: si es otro pedido, regístralo; si no, búscalo en Pedidos.',
      confirmLabel: 'Registrar de todos modos',
      cancelLabel: 'Revisar',
    });
    if (!confirmed) return null;
    const { data } = await ordersApi.create({ ...request, confirmPossibleDuplicate: true });
    return data;
  }
}
