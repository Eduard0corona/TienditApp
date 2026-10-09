import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { chatApi } from '../../api/chatApi';
import { getApiErrorMessage } from '../../api/errors';
import SaleTicket from '../../components/sales/SaleTicket';
import { useToast } from '../../hooks/useToast';
import { formatMoney } from '../../utils/money';
import { buildTicketText, buildWhatsAppTicketUrl, getTicketPaymentTotals, printTicket } from '../../utils/ticket';
import type { ChatOrigin, CompletedSale } from './sellModel';

interface CompletedSaleModalProps {
  sale: CompletedSale | null;
  businessName: string;
  /** The chat the sale was started from: the summary goes back to it. */
  chatOrigin: ChatOrigin | null;
  canChat: boolean;
  hasKitchen: boolean;
  onNewSale: () => void;
}

/** The sale just recorded: the change to hand over, the ticket and how to send it (print, WhatsApp or the chat). */
export default function CompletedSaleModal({ sale, businessName, chatOrigin, canChat, hasKitchen, onNewSale }: CompletedSaleModalProps) {
  const toast = useToast();
  const [sendingToChat, setSendingToChat] = useState(false);
  const ticketText = sale ? buildTicketText(sale.order, { businessName, provisional: sale.offline }) : '';
  const change = sale ? getTicketPaymentTotals(sale.order).change : 0;

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
    <Modal show={!!sale} onHide={onNewSale} centered fullscreen="sm-down">
      <Modal.Header closeButton>
        <Modal.Title as="h2" className="h5">
          <i className="ri-checkbox-circle-fill text-success me-1" aria-hidden="true"></i>
          {sale?.offline
            ? `Venta ${sale.order.orderNumber} guardada sin conexión`
            : sale?.unpaid
              ? `Pedido ${sale.order.orderNumber} guardado`
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
            {change > 0 && (
              <div className="charge-change has-change mb-3" aria-live="polite">
                <span>Cambio</span>
                <strong className="ta-money">{formatMoney(change)}</strong>
              </div>
            )}
            <SaleTicket order={sale.order} businessName={businessName} provisional={sale.offline} />
          </>
        )}
      </Modal.Body>
      <Modal.Footer className="flex-wrap">
        <button type="button" className="btn btn-light btn-touch" onClick={printTicket}>
          <i className="ri-printer-line me-1" aria-hidden="true"></i>Imprimir
        </button>
        {chatOrigin && canChat ? (
          <>
            <button type="button" className="btn btn-success btn-touch" onClick={() => void sendSummaryToChat()} disabled={sendingToChat}>
              <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>{sendingToChat ? 'Enviando…' : 'Enviar resumen al chat'}
            </button>
            <Link
              className="btn btn-light btn-touch"
              to={chatOrigin.conversationId ? `/whatsapp/chat?conversation=${chatOrigin.conversationId}` : '/whatsapp/chat'}
            >
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
