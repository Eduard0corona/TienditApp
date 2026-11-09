import { useId } from 'react';
import { Modal } from 'react-bootstrap';
import ChargePanel, { type ChargeResult } from '../../components/sales/ChargePanel';
import { ordersApi } from '../../api/ordersApi';
import type { OrderDto } from '../../types';
import { isDeliveryPlatform } from '../../utils/deliveryPlatforms';

interface OrderPaymentModalProps {
  /** The order to charge; none closes the modal. */
  order: OrderDto | null;
  submitting: boolean;
  error: string;
  onClose: () => void;
  onSubmit: (charge: ChargeResult) => void;
}

/** Charges what an order still owes from Pedidos, whole or in part; Pedidos registers each payment. */
export default function OrderPaymentModal({ order, submitting, error, onClose, onSubmit }: OrderPaymentModalProps) {
  const titleId = useId();
  return (
    <Modal show={!!order} onHide={onClose} centered size="lg" fullscreen="sm-down" aria-labelledby={titleId}>
      <Modal.Header closeButton={!submitting}>
        <Modal.Title id={titleId} as="h2" className="h5">
          Cobrar {order?.orderNumber}
          <span className="d-block small text-muted fw-normal">{order?.customerName}</span>
        </Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {order && (
          <ChargePanel
            amountDue={order.balance}
            allowPartial
            confirmLabel="Registrar"
            // The tip of a platform's order is in its app, for the courier.
            allowTip={!isDeliveryPlatform(order.channel)}
            submitting={submitting}
            error={error}
            onSubmit={onSubmit}
            onCancel={onClose}
            uploadEvidence={async (file) => {
              const { data } = await ordersApi.uploadPaymentEvidence(file);
              return { url: data.url, fileName: data.fileName };
            }}
          />
        )}
      </Modal.Body>
    </Modal>
  );
}
