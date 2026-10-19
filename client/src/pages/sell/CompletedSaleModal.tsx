import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { chatApi } from '../../api/chatApi';
import { getApiErrorMessage } from '../../api/errors';
import SaleTicket from '../../components/sales/SaleTicket';
import { useToast } from '../../hooks/useToast';
import { PlatformCollectedBy } from '../../types';
import { getPlatformName, isDeliveryPlatform } from '../../utils/deliveryPlatforms';
import { formatMoney } from '../../utils/money';
import { buildTicketText, buildWhatsAppTicketUrl, getTicketPaymentTotals, printTicket } from '../../utils/ticket';
import type { ChatOrigin, CompletedSale } from './sellModel';

interface CompletedSaleModalProps {
  sale: CompletedSale | null;
  businessName: string;
  /** Printed above the name on the ticket. */
  logoUrl?: string | null;
  /** The chat the sale was started from: the summary goes back to it. */
  chatOrigin: ChatOrigin | null;
  canChat: boolean;
  /** Whoever cannot charge leaves what the courier pays to the cashier. */
  canCharge: boolean;
  hasKitchen: boolean;
  onNewSale: () => void;
}

/**
 * The sale just recorded: the change to hand over, the ticket and how to send it (print, WhatsApp or the chat). An
 * order of a delivery platform ends here too, charged or not: its ticket goes in the bag, and there is no phone to
 * send it to.
 */
export default function CompletedSaleModal({ sale, businessName, logoUrl, chatOrigin, canChat, canCharge, hasKitchen, onNewSale }: CompletedSaleModalProps) {
  const titleId = useId();
  const toast = useToast();
  const [sendingToChat, setSendingToChat] = useState(false);
  const ticketText = sale ? buildTicketText(sale.order, { businessName, provisional: sale.offline }) : '';
  const change = sale ? getTicketPaymentTotals(sale.order).change : 0;
  const platform = sale && isDeliveryPlatform(sale.order.channel) ? sale.order.channel : null;
  // When the courier pays the business: charged now, or still to collect in Pedidos.
  const businessCollects = sale?.order.platformOrder?.collectedBy === PlatformCollectedBy.Business;
  const leftToCollect = platform !== null && businessCollects && (sale?.order.balance ?? 0) > 0;

  const sendSummaryToChat = async () => {
    if (!sale || !chatOrigin || sendingToChat) return;
    setSendingToChat(true);
    try {
      await chatApi.sendMessage({
        phoneNumber: sale.order.customerPhone ?? chatOrigin.phone,
        message: ticketText,
        conversationId: chatOrigin.conversationId,
      });
      toast.success('Resumen enviado al chat.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo enviar el resumen al chat.'));
    } finally {
      setSendingToChat(false);
    }
  };

  return (
    <Modal show={!!sale} onHide={onNewSale} centered fullscreen="sm-down" aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">
          <i className="ri-checkbox-circle-fill text-success me-1" aria-hidden="true"></i>
          {platform
            ? `Pedido ${sale?.order.orderNumber} de ${getPlatformName(platform)} ${businessCollects && !leftToCollect ? 'cobrado' : 'registrado'}`
            : sale?.offline
              ? `Venta ${sale.order.orderNumber} guardada sin conexión`
              : sale?.unpaid
                ? `Pedido ${sale.order.orderNumber} guardado`
                : sale?.order.soldOnCredit
                  ? `Venta ${sale.order.orderNumber} fiada`
                  : `Venta ${sale?.order.orderNumber} cobrada`}
        </Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {sale && (
          <>
            {sale.offline && (
              <div className="alert alert-warning small" role="status">
                <i className="ri-wifi-off-line me-1" aria-hidden="true"></i>
                Sin conexión: la venta quedó guardada en este dispositivo y se envía sola cuando vuelva. Su folio definitivo
                aparecerá en Pedidos.
                {hasKitchen && ' La cocina no la verá hasta que se envíe: avísale o entrégale el ticket impreso.'}
              </div>
            )}
            {leftToCollect && (
              <div className="alert alert-info small" role="status">
                <i className="ri-e-bike-2-line me-1" aria-hidden="true"></i>
                {canCharge ? 'Cóbralo en Pedidos cuando el repartidor te pague.' : 'La caja lo cobra desde Pedidos cuando el repartidor pague.'}
              </div>
            )}
            {change > 0 && (
              <div className="charge-change has-change mb-3" aria-live="polite">
                <span>Cambio</span>
                <strong className="ta-money">{formatMoney(change)}</strong>
              </div>
            )}
            <SaleTicket order={sale.order} businessName={businessName} logoUrl={logoUrl} provisional={sale.offline} />
          </>
        )}
      </Modal.Body>
      <Modal.Footer className="flex-wrap">
        <button type="button" className="btn btn-light btn-touch" onClick={printTicket}>
          <i className="ri-printer-line me-1" aria-hidden="true"></i>{platform ? 'Imprimir para la bolsa' : 'Imprimir'}
        </button>
        {platform ? null : chatOrigin && canChat ? (
          <>
            <button type="button" className="btn btn-success btn-touch" onClick={() => void sendSummaryToChat()} disabled={sendingToChat}>
              <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>{sendingToChat ? 'Enviando…' : 'Enviar resumen al chat'}
            </button>
            <Link className="btn btn-light btn-touch" to={`/whatsapp/chat?conversation=${chatOrigin.conversationId}`}>
              Volver al chat
            </Link>
          </>
        ) : (
          <a
            className="btn btn-light btn-touch"
            href={buildWhatsAppTicketUrl(ticketText, sale?.order.customerPhone)}
            target="_blank"
            rel="noopener noreferrer"
          >
            <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Enviar por WhatsApp
          </a>
        )}
        <button type="button" className="btn btn-primary btn-touch flex-grow-1" onClick={onNewSale} autoFocus>
          Nueva venta
        </button>
      </Modal.Footer>
    </Modal>
  );
}
