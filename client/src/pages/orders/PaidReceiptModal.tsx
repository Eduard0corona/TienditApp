import { useId } from 'react';
import { Modal } from 'react-bootstrap';
import SaleTicket from '../../components/sales/SaleTicket';
import { useOrderSelfInvoice } from '../../api/invoicing';
import { useTenant } from '../../hooks/useTenant';
import type { OrderDto } from '../../types';
import { isDeliveryPlatform } from '../../utils/deliveryPlatforms';
import { formatMoney } from '../../utils/money';
import { buildTicketText, buildWhatsAppTicketUrl, printTicket } from '../../utils/ticket';

interface PaidReceiptModalProps {
  /** The order just charged and the change of that charge; none closes the modal. */
  receipt: { order: OrderDto; change: number } | null;
  onClose: () => void;
}

/** After a charge in Pedidos: the change to give and the ticket, to print or send by WhatsApp. */
export default function PaidReceiptModal({ receipt, onClose }: PaidReceiptModalProps) {
  const titleId = useId();
  const { tenant } = useTenant();
  // Its ticket carries the folio and code to invoice it online while self-invoicing is on (Fase 28F).
  const { selfInvoice, preparing } = useOrderSelfInvoice(receipt?.order);

  return (
    <Modal show={!!receipt} onHide={onClose} centered fullscreen="sm-down" aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">
          <i className="ri-checkbox-circle-fill text-success me-1" aria-hidden="true"></i>
          Pago registrado en {receipt?.order.orderNumber}
        </Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {receipt && (
          <>
            {receipt.change > 0 && (
              <div className="charge-change has-change mb-3" aria-live="polite">
                <span>Cambio</span>
                <strong className="ta-money">{formatMoney(receipt.change)}</strong>
              </div>
            )}
            <SaleTicket
              order={receipt.order}
              businessName={tenant?.name ?? 'TienditApp'}
              logoUrl={tenant?.logoImages?.small ?? tenant?.logoUrl}
              selfInvoice={selfInvoice}
            />
          </>
        )}
      </Modal.Body>
      <Modal.Footer className="flex-wrap">
        <button type="button" className="btn btn-light btn-touch" onClick={printTicket} disabled={preparing}>
          <i className="ri-printer-line me-1" aria-hidden="true"></i>{preparing ? 'Preparando el ticket…' : 'Imprimir'}
        </button>
        {/* An order of a platform has no phone to send its ticket to. */}
        {receipt && !isDeliveryPlatform(receipt.order.channel) && (
          <a
            className="btn btn-light btn-touch"
            href={buildWhatsAppTicketUrl(
              buildTicketText(receipt.order, { businessName: tenant?.name ?? 'TienditApp' }),
              receipt.order.customerPhone,
            )}
            target="_blank"
            rel="noopener noreferrer"
          >
            <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Enviar por WhatsApp
          </a>
        )}
        <button type="button" className="btn btn-primary btn-touch flex-grow-1" onClick={onClose} autoFocus>
          Listo
        </button>
      </Modal.Footer>
    </Modal>
  );
}
