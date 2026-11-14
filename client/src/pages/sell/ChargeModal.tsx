import { useId } from 'react';
import { Modal } from 'react-bootstrap';
import { ordersApi } from '../../api/ordersApi';
import ChargePanel, { type ChargeBalance, type ChargeCredit, type ChargeResult } from '../../components/sales/ChargePanel';
import { formatMoney } from '../../utils/money';

interface ChargeModalProps {
  show: boolean;
  amountDue: number;
  /** What the customer already paid online for the appointment the sale charges (Fase 31O), out of the total; null otherwise. */
  paidOnline?: number | null;
  /** The sale's total: with a deposit paid online, the tip's percentages are of it, not of the rest. */
  total?: number;
  submitting: boolean;
  error: string;
  allowTip: boolean;
  credit?: ChargeCredit;
  balance?: ChargeBalance;
  onSubmit: (charge: ChargeResult) => void;
  onClose: () => void;
}

/** «Cobrar»: how the sale is paid. It does not close while the charge is on its way. */
export default function ChargeModal(props: ChargeModalProps) {
  const titleId = useId();
  const { show, submitting } = props;
  return (
    <Modal show={show} onHide={() => !submitting && props.onClose()} centered size="lg" fullscreen="sm-down" aria-labelledby={titleId}>
      <Modal.Header closeButton={!submitting}>
        <Modal.Title id={titleId} as="h2" className="h5">Cobrar</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {show && props.paidOnline != null && (
          <p className="small mb-3" role="note">
            <i className="ri-secure-payment-line me-1" aria-hidden="true"></i>
            El cliente ya pagó en línea un anticipo de <span className="ta-money">{formatMoney(props.paidOnline)}</span>: entra a la venta como
            pago «En línea» y aquí cobras el resto.
          </p>
        )}
        {show && (
          <ChargePanel
            amountDue={props.amountDue}
            submitting={submitting}
            error={props.error}
            allowTip={props.allowTip}
            tipBase={props.paidOnline != null ? props.total : undefined}
            credit={props.credit}
            balance={props.balance}
            onSubmit={props.onSubmit}
            onCancel={props.onClose}
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
