import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import {
  downloadInvoiceFile,
  useCancelOrderInvoice,
  useRetryOrderInvoice,
  type OrderInvoice,
} from '../../api/invoicing';
import { useToast } from '../../hooks/useToast';
import { CfdiCancellationMotive, OrderInvoiceStatus } from '../../types';
import { CANCELLATION_MOTIVES, invoiceStatusChip, invoiceStatusLabel, saveBlob } from '../../utils/cfdi';
import { formatDateTime } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

interface OrderInvoiceListProps {
  orderId: string;
  invoices: OrderInvoice[];
  /** Owners and admins cancel; whoever manages the orders stamps, retries and downloads. */
  canCancel: boolean;
  /**
   * The payment complements and credit notes in force of each invoice, by its id: the SAT does not cancel an invoice while
   * a related CFDI stands, so they are cancelled first.
   */
  liveRelated?: Partial<Record<string, string[]>>;
}

/** The invoices of an order (the newest first): their folio, UUID, files and what can be done with each one. */
export default function OrderInvoiceList({ orderId, invoices, canCancel, liveRelated = {} }: OrderInvoiceListProps) {
  const toast = useToast();
  const retry = useRetryOrderInvoice(orderId);
  const [cancelling, setCancelling] = useState<OrderInvoice | null>(null);
  const [downloading, setDownloading] = useState<string | null>(null);

  const download = async (invoice: OrderInvoice, kind: 'xml' | 'pdf') => {
    setDownloading(`${invoice.id}-${kind}`);
    try {
      const file = await downloadInvoiceFile(invoice.id, kind);
      saveBlob(file.blob, file.fileName);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, kind === 'pdf' ? 'No se pudo descargar el PDF.' : 'No se pudo descargar el XML.'));
    } finally {
      setDownloading(null);
    }
  };

  const askAgain = async (invoice: OrderInvoice) => {
    try {
      const result = await retry.mutateAsync(invoice.id);
      if (result.status === OrderInvoiceStatus.Stamped) toast.success(`Factura ${result.number} timbrada.`);
      else if (result.status === OrderInvoiceStatus.Refused) toast.error(result.failureMessage ?? 'El PAC rechazó la factura.');
      else toast.info('El PAC todavía no responde: TienditApp lo vuelve a intentar cada pocos minutos.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo volver a pedir el timbre.'));
    }
  };

  if (invoices.length === 0) return null;

  return (
    <div className="mb-3">
      <h3 className="h6">Facturas de este pedido</h3>
      <ul className="list-unstyled mb-0">
        {invoices.map((invoice) => (
          <li key={invoice.id} className="border rounded p-2 mb-2">
            <div className="d-flex flex-wrap align-items-center gap-2">
              <span className="fw-semibold">{invoice.number}</span>
              <span className={`ta-chip ${invoiceStatusChip(invoice.status)}`}>{invoiceStatusLabel(invoice.status)}</span>
              <span className="small text-muted">
                {invoice.receiver.rfc} · {invoice.receiver.name} · <span className="ta-money">{formatMoney(invoice.total)}</span>
                {invoice.paymentMethod === 'PPD' && ' · en parcialidades (PPD)'}
              </span>
            </div>
            {invoice.uuid && (
              <div className="small text-muted mt-1">
                Folio fiscal <span className="ta-money">{invoice.uuid}</span>
                {invoice.stampedAt && <> · timbrada el {formatDateTime(invoice.stampedAt)}</>}
              </div>
            )}
            {invoice.status === OrderInvoiceStatus.Stamping && (
              <p className="small text-muted mb-0 mt-1">
                El PAC no respondió todavía. TienditApp lo vuelve a intentar con la misma solicitud: nunca timbra dos veces.
              </p>
            )}
            {invoice.status === OrderInvoiceStatus.Cancelling && (
              <p className="small text-muted mb-0 mt-1">
                El SAT pidió al cliente que acepte la cancelación (tiene 3 días hábiles; si no responde, se cancela).
              </p>
            )}
            {invoice.failureMessage && (
              <p className={`small mb-0 mt-1 ${invoice.status === OrderInvoiceStatus.Refused ? 'text-danger' : 'text-warning-emphasis'}`}>
                {invoice.failureMessage}
              </p>
            )}
            <div className="d-flex flex-wrap gap-2 mt-2">
              {invoice.hasXml && (
                <>
                  <button
                    type="button"
                    className="btn btn-sm btn-outline-secondary"
                    disabled={downloading !== null}
                    aria-label={`Descargar XML de la factura ${invoice.number}`}
                    onClick={() => void download(invoice, 'xml')}
                  >
                    Descargar XML
                  </button>
                  <button
                    type="button"
                    className="btn btn-sm btn-outline-secondary"
                    disabled={downloading !== null}
                    aria-label={`Descargar PDF de la factura ${invoice.number}`}
                    onClick={() => void download(invoice, 'pdf')}
                  >
                    Descargar PDF
                  </button>
                </>
              )}
              {invoice.status === OrderInvoiceStatus.Stamping && (
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary"
                  disabled={retry.isPending}
                  aria-label={`Volver a pedir el timbre de la factura ${invoice.number}`}
                  onClick={() => void askAgain(invoice)}
                >
                  Volver a pedir el timbre
                </button>
              )}
              {canCancel && invoice.status === OrderInvoiceStatus.Stamped && !liveRelated[invoice.id]?.length && (
                <button
                  type="button"
                  className="btn btn-sm btn-outline-danger"
                  aria-label={`Cancelar factura ${invoice.number}`}
                  onClick={() => setCancelling(invoice)}
                >
                  Cancelar factura
                </button>
              )}
            </div>
            {canCancel && invoice.status === OrderInvoiceStatus.Stamped && !!liveRelated[invoice.id]?.length && (
              <p className="small text-muted mb-0 mt-2">Para cancelarla, primero cancela {liveRelated[invoice.id]!.join(', ')}.</p>
            )}
          </li>
        ))}
      </ul>
      {cancelling && <CancelInvoiceModal orderId={orderId} invoice={cancelling} onClose={() => setCancelling(null)} />}
    </div>
  );
}

interface CancelInvoiceModalProps {
  orderId: string;
  invoice: OrderInvoice;
  onClose: () => void;
}

/** Owners and admins ask the SAT to cancel a stamped invoice, with its reason. */
function CancelInvoiceModal({ orderId, invoice, onClose }: CancelInvoiceModalProps) {
  const titleId = useId();
  const toast = useToast();
  const cancel = useCancelOrderInvoice(orderId);
  const [motive, setMotive] = useState<CfdiCancellationMotive>(CfdiCancellationMotive.ErrorsWithoutReplacement);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      const result = await cancel.mutateAsync({ invoiceId: invoice.id, motive });
      toast.success(result.status === OrderInvoiceStatus.Cancelled
        ? `Factura ${result.number} cancelada.`
        : `Cancelación de ${result.number} pedida: el SAT espera la respuesta del cliente.`);
      onClose();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cancelar la factura.'));
    }
  };

  return (
    <Modal show onHide={onClose} centered aria-labelledby={titleId}>
      <form onSubmit={submit}>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">Cancelar la factura {invoice.number}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="small">
            TienditApp le pide al SAT que la cancele. Si pasa de $1,000 o de 24 horas, el SAT le pide al cliente que la acepte;
            mientras tanto sigue vigente. Después puedes volver a facturar el pedido.
          </p>
          <fieldset>
            <legend className="form-label small mb-1">Motivo</legend>
            {CANCELLATION_MOTIVES.map(([value, label]) => (
              <div className="form-check" key={value}>
                <input
                  id={`cancel-motive-${value}`}
                  type="radio"
                  name="cancel-motive"
                  className="form-check-input"
                  checked={motive === value}
                  onChange={() => setMotive(value)}
                />
                <label className="form-check-label" htmlFor={`cancel-motive-${value}`}>{label}</label>
              </div>
            ))}
          </fieldset>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose}>Volver</button>
          <button type="submit" className="btn btn-danger" disabled={cancel.isPending}>
            {cancel.isPending ? 'Cancelando…' : 'Cancelar factura'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
