import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import {
  downloadPaymentComplementFile,
  useCancelPaymentComplement,
  useIssuePaymentComplement,
  useRetryPaymentComplement,
  type OrderPaymentComplements,
  type PaymentComplement,
} from '../../api/invoicing';
import { useToast } from '../../hooks/useToast';
import { CfdiCancellationMotive, OrderInvoiceStatus } from '../../types';
import {
  COMPLEMENT_CANCELLATION_MOTIVES,
  complementStatusLabel,
  invoiceStatusChip,
  PAYMENT_FORMS,
  saveBlob,
} from '../../utils/cfdi';
import { formatCalendarDate, formatDateTime, getBusinessToday } from '../../utils/dates';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';

interface PaymentComplementListProps {
  orderId: string;
  payments: OrderPaymentComplements[];
  /** The order's live invoice is in installments (PPD): its payments get their complements. */
  inInstallments: boolean;
  /** Owners and admins cancel; whoever manages the orders issues, retries and downloads. */
  canCancel: boolean;
}

/** The toast that says how issuing or asking again for a complement ended. */
function useComplementOutcome() {
  const toast = useToast();
  return (complement: PaymentComplement) => {
    if (complement.status === OrderInvoiceStatus.Stamped) toast.success(`Complemento ${complement.number} timbrado.`);
    else if (complement.status === OrderInvoiceStatus.Refused) toast.error(complement.failureMessage ?? 'El PAC rechazó el complemento.');
    else toast.info(`El PAC no respondió: TienditApp sigue pidiendo el timbre de ${complement.number}.`);
  };
}

/**
 * The payments of an order invoiced in installments (PPD, Fase 28C) with their payment complements (CFDI tipo P):
 * TienditApp issues each one on its own once the invoice is stamped and the payment registered; from here it is issued
 * before that («Emitir ahora»), again after a refused or cancelled one, asked again of the PAC, cancelled or downloaded.
 */
export default function PaymentComplementList({ orderId, payments, inInstallments, canCancel }: PaymentComplementListProps) {
  const toast = useToast();
  const outcome = useComplementOutcome();
  const issue = useIssuePaymentComplement(orderId);
  const retry = useRetryPaymentComplement(orderId);
  const [cancelling, setCancelling] = useState<PaymentComplement | null>(null);
  const [downloading, setDownloading] = useState<string | null>(null);
  const today = getBusinessToday();

  const issueFor = async (payment: OrderPaymentComplements) => {
    try {
      outcome(await issue.mutateAsync(payment.orderPaymentId));
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo emitir el complemento de pago.'));
    }
  };

  const askAgain = async (complement: PaymentComplement) => {
    try {
      outcome(await retry.mutateAsync(complement.id));
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo volver a pedir el timbre.'));
    }
  };

  const download = async (complement: PaymentComplement, kind: 'xml' | 'pdf') => {
    setDownloading(`${complement.id}-${kind}`);
    try {
      const file = await downloadPaymentComplementFile(complement.id, kind);
      saveBlob(file.blob, file.fileName);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, kind === 'pdf' ? 'No se pudo descargar el PDF.' : 'No se pudo descargar el XML.'));
    } finally {
      setDownloading(null);
    }
  };

  if (payments.length === 0 && !inInstallments) return null;

  return (
    <div className="mb-3">
      <h3 className="h6 mb-1">Complementos de pago</h3>
      <p className="small text-muted mb-2">
        {inInstallments
          ? 'La factura va en parcialidades: cada cobro lleva su complemento de pago, que TienditApp emite por su cuenta unos '
            + 'minutos después de registrarlo. El SAT lo pide a más tardar el día 5 del mes siguiente al cobro.'
          : 'Los complementos de los cobros de una factura en parcialidades anterior de este pedido.'}
      </p>
      {payments.length === 0 && (
        <p className="small mb-0">Todavía no hay cobros: cada cobro que registres en el pedido lleva su complemento.</p>
      )}
      <ul className="list-unstyled mb-0">
        {payments.map((payment) => {
          const paidAt = formatDateTime(payment.paidAt);
          const live = payment.complements.find((complement) => complement.status !== OrderInvoiceStatus.Refused
            && complement.status !== OrderInvoiceStatus.Cancelled);
          const overdue = !live && payment.dueBy < today;
          const dueBy = formatCalendarDate(payment.dueBy);
          const issuing = issue.isPending && issue.variables === payment.orderPaymentId;
          const issueLabel = payment.complements.length === 0 ? 'Emitir ahora' : 'Emitir otra vez';
          return (
            <li key={payment.orderPaymentId} className="border rounded p-2 mb-2">
              <div className="d-flex flex-wrap align-items-center gap-2">
                <span className="fw-semibold">Cobro del {paidAt}</span>
                <span className="small text-muted">
                  {getPaymentMethodLabel(payment.method)} · <span className="ta-money">{formatMoney(payment.amount)}</span>
                </span>
              </div>
              {payment.problem && <p className="small text-muted mb-0 mt-1">{payment.problem}</p>}
              {!live && (
                <p className={`small mb-0 mt-1 ${overdue ? 'text-danger' : 'text-muted'}`}>
                  {payment.awaitsAutomaticIssue && 'Sin complemento todavía: TienditApp lo emite en unos minutos. '}
                  {overdue ? `El plazo del SAT para su complemento venció el ${dueBy}.` : `El SAT lo pide a más tardar el ${dueBy}.`}
                </p>
              )}

              {payment.complements.length > 0 && (
                <ul className="list-unstyled mb-0 mt-2">
                  {payment.complements.map((complement) => (
                    <li key={complement.id} className="border-start ps-2 mb-2">
                      <div className="d-flex flex-wrap align-items-center gap-2">
                        <span className="fw-semibold">{complement.number}</span>
                        <span className={`ta-chip ${invoiceStatusChip(complement.status)}`}>{complementStatusLabel(complement.status)}</span>
                        <span className="small text-muted">
                          Parcialidad {complement.installment} · pagó <span className="ta-money">{formatMoney(complement.amount)}</span> de{' '}
                          <span className="ta-money">{formatMoney(complement.previousBalance)}</span> · queda{' '}
                          <span className="ta-money">{formatMoney(complement.remainingBalance)}</span>
                        </span>
                      </div>
                      <div className="small text-muted mt-1">
                        Forma {complement.paymentForm}
                        {PAYMENT_FORMS[complement.paymentForm] ? ` (${PAYMENT_FORMS[complement.paymentForm]})` : ''}
                        {complement.issuedAutomatically ? ' · lo emitió TienditApp' : ''}
                        {complement.uuid && <> · folio fiscal <span className="ta-money">{complement.uuid}</span></>}
                        {complement.stampedAt && <> · timbrado el {formatDateTime(complement.stampedAt)}</>}
                      </div>
                      {complement.status === OrderInvoiceStatus.Stamping && (
                        <p className="small text-muted mb-0 mt-1">
                          El PAC no respondió todavía. TienditApp lo vuelve a intentar con la misma solicitud: nunca timbra dos veces.
                        </p>
                      )}
                      {complement.status === OrderInvoiceStatus.Cancelling && (
                        <p className="small text-muted mb-0 mt-1">El SAT pidió al cliente que acepte la cancelación.</p>
                      )}
                      {complement.failureMessage && (
                        <p className={`small mb-0 mt-1 ${complement.status === OrderInvoiceStatus.Refused ? 'text-danger' : 'text-warning-emphasis'}`}>
                          {complement.failureMessage}
                        </p>
                      )}
                      <div className="d-flex flex-wrap gap-2 mt-2">
                        {complement.hasXml && (
                          <>
                            <button
                              type="button"
                              className="btn btn-sm btn-outline-secondary"
                              disabled={downloading !== null}
                              aria-label={`Descargar XML del complemento ${complement.number}`}
                              onClick={() => void download(complement, 'xml')}
                            >
                              Descargar XML
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-outline-secondary"
                              disabled={downloading !== null}
                              aria-label={`Descargar PDF del complemento ${complement.number}`}
                              onClick={() => void download(complement, 'pdf')}
                            >
                              Descargar PDF
                            </button>
                          </>
                        )}
                        {complement.status === OrderInvoiceStatus.Stamping && (
                          <button
                            type="button"
                            className="btn btn-sm btn-outline-secondary"
                            disabled={retry.isPending}
                            aria-label={`Volver a pedir el timbre del complemento ${complement.number}`}
                            onClick={() => void askAgain(complement)}
                          >
                            Volver a pedir el timbre
                          </button>
                        )}
                        {canCancel && complement.status === OrderInvoiceStatus.Stamped && (
                          <button
                            type="button"
                            className="btn btn-sm btn-outline-danger"
                            aria-label={`Cancelar complemento ${complement.number}`}
                            onClick={() => setCancelling(complement)}
                          >
                            Cancelar complemento
                          </button>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
              )}

              {payment.canIssue && (
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary mt-2"
                  disabled={issue.isPending}
                  aria-label={`${issuing ? 'Emitiendo' : issueLabel} el complemento del cobro del ${paidAt}`}
                  onClick={() => void issueFor(payment)}
                >
                  {issuing ? 'Emitiendo…' : issueLabel}
                </button>
              )}
            </li>
          );
        })}
      </ul>
      {cancelling && <CancelComplementModal orderId={orderId} complement={cancelling} onClose={() => setCancelling(null)} />}
    </div>
  );
}

interface CancelComplementModalProps {
  orderId: string;
  complement: PaymentComplement;
  onClose: () => void;
}

/** Owners and admins ask the SAT to cancel a stamped payment complement, with its reason. */
function CancelComplementModal({ orderId, complement, onClose }: CancelComplementModalProps) {
  const titleId = useId();
  const toast = useToast();
  const cancel = useCancelPaymentComplement(orderId);
  const [motive, setMotive] = useState<CfdiCancellationMotive>(CfdiCancellationMotive.ErrorsWithoutReplacement);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      const result = await cancel.mutateAsync({ complementId: complement.id, motive });
      toast.success(result.status === OrderInvoiceStatus.Cancelled
        ? `Complemento ${result.number} cancelado.`
        : `Cancelación de ${result.number} pedida: el SAT espera la respuesta del cliente.`);
      onClose();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cancelar el complemento.'));
    }
  };

  return (
    <Modal show onHide={onClose} centered aria-labelledby={titleId}>
      <form onSubmit={submit}>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">Cancelar el complemento {complement.number}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="small">
            TienditApp le pide al SAT que lo cancele; si el SAT le pide al cliente que lo acepte, sigue vigente mientras tanto.
            Con el motivo 02, después emites otro para el mismo cobro.
          </p>
          <fieldset>
            <legend className="form-label small mb-1">Motivo</legend>
            {COMPLEMENT_CANCELLATION_MOTIVES.map(([value, label]) => (
              <div className="form-check" key={value}>
                <input
                  id={`complement-cancel-motive-${value}`}
                  type="radio"
                  name="complement-cancel-motive"
                  className="form-check-input"
                  checked={motive === value}
                  onChange={() => setMotive(value)}
                />
                <label className="form-check-label" htmlFor={`complement-cancel-motive-${value}`}>{label}</label>
              </div>
            ))}
          </fieldset>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose}>Volver</button>
          <button type="submit" className="btn btn-danger" disabled={cancel.isPending}>
            {cancel.isPending ? 'Cancelando…' : 'Cancelar complemento'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
