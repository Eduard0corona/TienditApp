import { useId } from 'react';
import { Modal } from 'react-bootstrap';
import { ordersApi } from '../../api/ordersApi';
import ChargePanel, { type ChargeBalance, type ChargeCredit, type ChargeResult } from '../../components/sales/ChargePanel';

interface ChargeModalProps {
  show: boolean;
  amountDue: number;
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
        {show && (
          <ChargePanel
            amountDue={props.amountDue}
            submitting={submitting}
            error={props.error}
            allowTip={props.allowTip}
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
