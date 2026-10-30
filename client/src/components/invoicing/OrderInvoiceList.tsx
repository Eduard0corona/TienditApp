import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import {
  downloadInvoiceFile,
  useCancelOrderInvoice,
  useCancelReplacedOrderInvoice,
  useRetryOrderInvoice,
  type OrderInvoice,
} from '../../api/invoicing';
import { useToast } from '../../hooks/useToast';
import { CfdiCancellationMotive, CfdiEmailDocument, OrderInvoiceStatus } from '../../types';
import { CANCELLATION_MOTIVES, invoiceStatusChip, invoiceStatusLabel, isStuckStamping, saveBlob } from '../../utils/cfdi';
import { formatDateTime } from '../../utils/dates';
import { formatMoney } from '../../utils/money';
import { CfdiEmailStatus, SendCfdiEmailButton } from './CfdiEmails';
import { CfdiWhatsAppStatus, SendCfdiWhatsAppButton } from './CfdiWhatsApps';
import StuckStampNote from './StuckStampNote';

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
  /** The email the order's invoicing proposes, to send an invoice that has none (Fase 28E). */
  proposedEmail?: string | null;
  /** The business's WhatsApp is connected (Fase 28M): its invoices can go by WhatsApp. */
  whatsAppConnected?: boolean;
  /** The WhatsApp the order's invoicing proposes, to send an invoice that has none. */
  proposedWhatsApp?: string | null;
  /** «Sustituir factura» (Fase 28G): the order's screen opens the invoice's form to replace it. */
  onSubstitute?: (invoice: OrderInvoice) => void;
  /** The invoice being replaced on that form, if any. */
  substitutingId?: string | null;
  /** The list's heading, where the focus goes once an invoice is replaced. */
  headingRef?: React.Ref<HTMLHeadingElement>;
}

/**
 * The invoices of an order (the newest first): their folio, UUID, files, how their latest email to the customer went, which
 * one replaces which (Fase 28G) and what can be done with each one.
 */
export default function OrderInvoiceList({
  orderId, invoices, canCancel, liveRelated = {}, proposedEmail, whatsAppConnected = false, proposedWhatsApp, onSubstitute, substitutingId,
  headingRef,
}: OrderInvoiceListProps) {
  const toast = useToast();
  const retry = useRetryOrderInvoice(orderId);
  const cancelReplaced = useCancelReplacedOrderInvoice(orderId);
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

  const askToCancelReplaced = async (invoice: OrderInvoice) => {
    try {
      const result = await cancelReplaced.mutateAsync(invoice.id);
      toast.success(result.status === OrderInvoiceStatus.Cancelled
        ? `Factura ${result.number} cancelada.`
        : `Cancelación de ${result.number} pedida: el SAT espera la respuesta del cliente.`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo pedir la cancelación con relación.'));
    }
  };

  if (invoices.length === 0) return null;

  return (
    <div className="mb-3">
      <h3 className="h6" ref={headingRef} tabIndex={-1}>Facturas de este pedido</h3>
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
            {/* Fase 28F: the customer invoiced the ticket on the business's page. */}
            {invoice.requestedByCustomer && <div className="small text-muted mt-1">La pidió el cliente en línea con su ticket.</div>}
            {/* Fase 28G: which one replaces which, with the relation 04 and the cancellation with motive 01. */}
            {invoice.replacesNumber && <div className="small text-muted mt-1">Sustituye a la factura {invoice.replacesNumber}.</div>}
            {invoice.replacedByNumber && <div className="small text-muted mt-1">Sustituida por la factura {invoice.replacedByNumber}.</div>}
            {invoice.uuid && (
              <div className="small text-muted mt-1">
                Folio fiscal <span className="ta-money">{invoice.uuid}</span>
                {invoice.stampedAt && <> · timbrada el {formatDateTime(invoice.stampedAt)}</>}
              </div>
            )}
            {invoice.status === OrderInvoiceStatus.Stamping && !isStuckStamping(invoice) && (
              <p className="small text-muted mb-0 mt-1">
                El PAC no respondió todavía. TienditApp lo vuelve a intentar con la misma solicitud: nunca timbra dos veces.
              </p>
            )}
            {isStuckStamping(invoice) && <StuckStampNote canReview={canCancel} />}
            {invoice.awaitsReplacedCancellation && (
              <p className="small text-muted mb-0 mt-1">
                {replacementStamped(invoice, invoices)
                  ? `La ${invoice.replacedByNumber ?? 'nueva'} ya está timbrada: TienditApp le pide al SAT cancelar esta con relación (motivo 01) en su siguiente revisión, cada 30 minutos.`
                  : `En cuanto la ${invoice.replacedByNumber ?? 'nueva'} quede timbrada, TienditApp le pide al SAT cancelar esta con relación (motivo 01).`}
              </p>
            )}
            {/* The worker asks for 72 hours after the replacement was stamped; then a person does. */}
            {!invoice.awaitsReplacedCancellation && !invoice.failureMessage && invoice.status === OrderInvoiceStatus.Stamped
              && replacementStamped(invoice, invoices) && (
              <p className="small text-muted mb-0 mt-1">
                Pasaron 72 horas desde que se timbró la {invoice.replacedByNumber ?? 'nueva'} y TienditApp ya no lo pide solo: usa «Cancelar con relación» o cancélala sin relación (02).
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
            <CfdiEmailStatus emails={invoice.emails} />
            <CfdiWhatsAppStatus messages={invoice.whatsApps} />
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
              {/* One another replaces has errors: what goes to the customer is the new one. */}
              {invoice.status === OrderInvoiceStatus.Stamped && !invoice.replacedByInvoiceId && (
                <SendCfdiEmailButton
                  orderId={orderId}
                  document={CfdiEmailDocument.Invoice}
                  cfdiId={invoice.id}
                  number={invoice.number}
                  invoiceEmail={invoice.receiverEmail}
                  latestEmail={invoice.emails[0]}
                  proposedEmail={proposedEmail}
                />
              )}
              {whatsAppConnected && invoice.status === OrderInvoiceStatus.Stamped && !invoice.replacedByInvoiceId && (
                <SendCfdiWhatsAppButton
                  orderId={orderId}
                  document={CfdiEmailDocument.Invoice}
                  cfdiId={invoice.id}
                  number={invoice.number}
                  invoiceWhatsApp={invoice.receiverWhatsApp}
                  latestWhatsApp={invoice.whatsApps[0]}
                  proposedWhatsApp={proposedWhatsApp}
                />
              )}
              {invoice.status === OrderInvoiceStatus.Stamping && !isStuckStamping(invoice) && (
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
              {canCancel && onSubstitute && invoice.canSubstitute && (
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary"
                  disabled={substitutingId === invoice.id}
                  aria-label={`Sustituir factura ${invoice.number}`}
                  onClick={() => onSubstitute(invoice)}
                >
                  Sustituir factura
                </button>
              )}
              {canCancel && invoice.status === OrderInvoiceStatus.Stamped && replacementStamped(invoice, invoices) && (
                <button
                  type="button"
                  className="btn btn-sm btn-outline-danger"
                  disabled={cancelReplaced.isPending}
                  aria-label={`Cancelar con relación la factura ${invoice.number}`}
                  onClick={() => void askToCancelReplaced(invoice)}
                >
                  Cancelar con relación
                </button>
              )}
              {/* Without relation (02) only once the SAT did not take it with relation, the customer refused it, or TienditApp no longer asks (72 hours). */}
              {canCancel && invoice.status === OrderInvoiceStatus.Stamped && !liveRelated[invoice.id]?.length
                && !invoice.awaitsReplacedCancellation && (
                <button
                  type="button"
                  className="btn btn-sm btn-outline-danger"
                  disabled={substitutingId === invoice.id}
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
            {canCancel && onSubstitute && invoice.substitutionProblem && !liveRelated[invoice.id]?.length
              && substitutingId !== invoice.id && (
              <p className="small text-muted mb-0 mt-2">No se puede sustituir. {invoice.substitutionProblem}</p>
            )}
          </li>
        ))}
      </ul>
      {cancelling && <CancelInvoiceModal orderId={orderId} invoice={cancelling} onClose={() => setCancelling(null)} />}
    </div>
  );
}

/** The invoice that replaces this one is stamped: the SAT can relate them, so it is cancelled with motive 01. */
function replacementStamped(invoice: OrderInvoice, invoices: OrderInvoice[]): boolean {
  return !!invoice.replacedByInvoiceId
    && invoices.some((other) => other.id === invoice.replacedByInvoiceId && other.status === OrderInvoiceStatus.Stamped);
}

interface CancelInvoiceModalProps {
  orderId: string;
  invoice: OrderInvoice;
  onClose: () => void;
}

/**
 * Owners and admins ask the SAT to cancel a stamped invoice, with its reason. One that another replaces is cancelled with
 * relation (01) on its own; when the SAT does not relate them, it says to cancel it without relation (02).
 */
function CancelInvoiceModal({ orderId, invoice, onClose }: CancelInvoiceModalProps) {
  const titleId = useId();
  const toast = useToast();
  const cancel = useCancelOrderInvoice(orderId);
  const [motive, setMotive] = useState<CfdiCancellationMotive>(CfdiCancellationMotive.ErrorsWithoutReplacement);
  const motives = invoice.replacedByNumber
    ? CANCELLATION_MOTIVES.map(([value, label]): [CfdiCancellationMotive, string] => value === CfdiCancellationMotive.ErrorsWithoutReplacement
      ? [value, `02 · Tiene errores, sin relacionarla con la ${invoice.replacedByNumber}`]
      : [value, label])
    : CANCELLATION_MOTIVES;

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
            mientras tanto sigue vigente. {invoice.replacedByNumber
              ? `La ${invoice.replacedByNumber} la sustituye: cancélala así solo si el SAT no la cancela con relación (01).`
              : 'Después puedes volver a facturar el pedido.'}
          </p>
          <fieldset>
            <legend className="form-label small mb-1">Motivo</legend>
            {motives.map(([value, label]) => (
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
