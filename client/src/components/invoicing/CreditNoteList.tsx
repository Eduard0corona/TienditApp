import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import {
  downloadCreditNoteFile,
  useCancelCreditNote,
  useIssueCreditNote,
  useRetryCreditNote,
  type CreditNote,
  type OrderAdjustment,
} from '../../api/invoicing';
import { useToast } from '../../hooks/useToast';
import { CfdiCancellationMotive, CreditNoteAdjustment, OrderInvoiceStatus } from '../../types';
import { CREDIT_NOTE_CANCELLATION_MOTIVES, invoiceStatusChip, invoiceStatusLabel, PAYMENT_FORMS, saveBlob } from '../../utils/cfdi';
import { formatDateTime } from '../../utils/dates';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import { getWriteOffReasonLabel } from '../../utils/writeOffReasons';

interface CreditNoteListProps {
  orderId: string;
  adjustments: OrderAdjustment[];
  /** Owners and admins cancel; whoever manages the orders issues, retries and downloads. */
  canCancel: boolean;
}

/** The toast that says how issuing or asking again for a credit note ended. */
function useCreditNoteOutcome() {
  const toast = useToast();
  return (note: CreditNote) => {
    if (note.status === OrderInvoiceStatus.Stamped) toast.success(`Nota de crédito ${note.number} timbrada.`);
    else if (note.status === OrderInvoiceStatus.Refused) toast.error(note.failureMessage ?? 'El PAC rechazó la nota de crédito.');
    else toast.info(`El PAC no respondió: TienditApp sigue pidiendo el timbre de ${note.number}.`);
  };
}

/** «Reembolso del 24 oct 2026, 5:00 p.m.» or «Condonación del …», and how an action names it: «del reembolso del …». */
function adjustmentNames(adjustment: OrderAdjustment): { name: string; ofIt: string } {
  const at = formatDateTime(adjustment.at);
  return adjustment.kind === CreditNoteAdjustment.Refund
    ? { name: `Reembolso del ${at}`, ofIt: `del reembolso del ${at}` }
    : { name: `Condonación del ${at}`, ofIt: `de la condonación del ${at}` };
}

/**
 * The refunds and write-offs of an invoiced order (Fase 28D) with their credit notes (CFDI de egreso): TienditApp issues
 * each one on its own once the invoice is stamped; from here it is issued before that («Emitir ahora»), again after a
 * refused or cancelled one, asked again of the PAC, cancelled or downloaded.
 */
export default function CreditNoteList({ orderId, adjustments, canCancel }: CreditNoteListProps) {
  const toast = useToast();
  const outcome = useCreditNoteOutcome();
  const issue = useIssueCreditNote(orderId);
  const retry = useRetryCreditNote(orderId);
  const [cancelling, setCancelling] = useState<CreditNote | null>(null);
  const [downloading, setDownloading] = useState<string | null>(null);

  const issueFor = async (adjustment: OrderAdjustment) => {
    try {
      outcome(await issue.mutateAsync({ kind: adjustment.kind, adjustmentId: adjustment.adjustmentId }));
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo emitir la nota de crédito.'));
    }
  };

  const askAgain = async (note: CreditNote) => {
    try {
      outcome(await retry.mutateAsync(note.id));
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo volver a pedir el timbre.'));
    }
  };

  const download = async (note: CreditNote, kind: 'xml' | 'pdf') => {
    setDownloading(`${note.id}-${kind}`);
    try {
      const file = await downloadCreditNoteFile(note.id, kind);
      saveBlob(file.blob, file.fileName);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, kind === 'pdf' ? 'No se pudo descargar el PDF.' : 'No se pudo descargar el XML.'));
    } finally {
      setDownloading(null);
    }
  };

  if (adjustments.length === 0) return null;

  return (
    <div className="mb-3">
      <h3 className="h6 mb-1">Notas de crédito</h3>
      <p className="small text-muted mb-2">
        La factura ampara la venta completa: cada reembolso y cada condonación lleva su nota de crédito, que TienditApp emite
        por su cuenta unos minutos después de que la factura quede timbrada.
      </p>
      <ul className="list-unstyled mb-0">
        {adjustments.map((adjustment) => {
          const { name, ofIt } = adjustmentNames(adjustment);
          const issuing = issue.isPending && issue.variables?.adjustmentId === adjustment.adjustmentId;
          const issueLabel = adjustment.creditNotes.length === 0 ? 'Emitir ahora' : 'Emitir otra vez';
          const detail = adjustment.kind === CreditNoteAdjustment.Refund
            ? (adjustment.method != null ? getPaymentMethodLabel(adjustment.method) : null)
            : getWriteOffReasonLabel(adjustment.writeOffReason);
          return (
            <li key={adjustment.adjustmentId} className="border rounded p-2 mb-2">
              <div className="d-flex flex-wrap align-items-center gap-2">
                <span className="fw-semibold">{name}</span>
                <span className="small text-muted">
                  {detail && <>{detail} · </>}<span className="ta-money">{formatMoney(adjustment.amount)}</span>
                </span>
                {adjustment.reversed && <span className="ta-chip ta-chip-done">Deshecha</span>}
              </div>
              {adjustment.notes && <p className="small text-muted mb-0 mt-1">{adjustment.notes}</p>}
              {adjustment.problem && <p className="small text-muted mb-0 mt-1">{adjustment.problem}</p>}
              {adjustment.awaitsAutomaticIssue && (
                <p className="small text-muted mb-0 mt-1">Sin nota de crédito todavía: TienditApp la emite en unos minutos.</p>
              )}

              {adjustment.creditNotes.length > 0 && (
                <ul className="list-unstyled mb-0 mt-2">
                  {adjustment.creditNotes.map((note) => (
                    <li key={note.id} className="border-start ps-2 mb-2">
                      <div className="d-flex flex-wrap align-items-center gap-2">
                        <span className="fw-semibold">{note.number}</span>
                        <span className={`ta-chip ${invoiceStatusChip(note.status)}`}>{invoiceStatusLabel(note.status)}</span>
                        <span className="small text-muted">
                          Por <span className="ta-money">{formatMoney(note.total)}</span> (base{' '}
                          <span className="ta-money">{formatMoney(note.subTotal)}</span> e IVA{' '}
                          <span className="ta-money">{formatMoney(note.taxTotal)}</span>)
                        </span>
                      </div>
                      <div className="small text-muted mt-1">
                        Forma {note.paymentForm}
                        {PAYMENT_FORMS[note.paymentForm] ? ` (${PAYMENT_FORMS[note.paymentForm]})` : ''}
                        {note.issuedAutomatically ? ' · la emitió TienditApp' : ''}
                        {note.uuid && <> · folio fiscal <span className="ta-money">{note.uuid}</span></>}
                        {note.stampedAt && <> · timbrada el {formatDateTime(note.stampedAt)}</>}
                      </div>
                      {note.status === OrderInvoiceStatus.Stamping && (
                        <p className="small text-muted mb-0 mt-1">
                          El PAC no respondió todavía. TienditApp lo vuelve a intentar con la misma solicitud: nunca timbra dos veces.
                        </p>
                      )}
                      {note.status === OrderInvoiceStatus.Cancelling && (
                        <p className="small text-muted mb-0 mt-1">El SAT pidió al cliente que acepte la cancelación.</p>
                      )}
                      {note.failureMessage && (
                        <p className={`small mb-0 mt-1 ${note.status === OrderInvoiceStatus.Refused ? 'text-danger' : 'text-warning-emphasis'}`}>
                          {note.failureMessage}
                        </p>
                      )}
                      <div className="d-flex flex-wrap gap-2 mt-2">
                        {note.hasXml && (
                          <>
                            <button
                              type="button"
                              className="btn btn-sm btn-outline-secondary"
                              disabled={downloading !== null}
                              aria-label={`Descargar XML de la nota de crédito ${note.number}`}
                              onClick={() => void download(note, 'xml')}
                            >
                              Descargar XML
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-outline-secondary"
                              disabled={downloading !== null}
                              aria-label={`Descargar PDF de la nota de crédito ${note.number}`}
                              onClick={() => void download(note, 'pdf')}
                            >
                              Descargar PDF
                            </button>
                          </>
                        )}
                        {note.status === OrderInvoiceStatus.Stamping && (
                          <button
                            type="button"
                            className="btn btn-sm btn-outline-secondary"
                            disabled={retry.isPending}
                            aria-label={`Volver a pedir el timbre de la nota de crédito ${note.number}`}
                            onClick={() => void askAgain(note)}
                          >
                            Volver a pedir el timbre
                          </button>
                        )}
                        {canCancel && note.status === OrderInvoiceStatus.Stamped && (
                          <button
                            type="button"
                            className="btn btn-sm btn-outline-danger"
                            aria-label={`Cancelar nota de crédito ${note.number}`}
                            onClick={() => setCancelling(note)}
                          >
                            Cancelar nota de crédito
                          </button>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
              )}

              {adjustment.canIssue && (
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary mt-2"
                  disabled={issue.isPending}
                  aria-label={`${issuing ? 'Emitiendo' : issueLabel} la nota de crédito ${ofIt}`}
                  onClick={() => void issueFor(adjustment)}
                >
                  {issuing ? 'Emitiendo…' : issueLabel}
                </button>
              )}
            </li>
          );
        })}
      </ul>
      {cancelling && <CancelCreditNoteModal orderId={orderId} note={cancelling} onClose={() => setCancelling(null)} />}
    </div>
  );
}

interface CancelCreditNoteModalProps {
  orderId: string;
  note: CreditNote;
  onClose: () => void;
}

/** Owners and admins ask the SAT to cancel a stamped credit note, with its reason. */
function CancelCreditNoteModal({ orderId, note, onClose }: CancelCreditNoteModalProps) {
  const titleId = useId();
  const toast = useToast();
  const cancel = useCancelCreditNote(orderId);
  const [motive, setMotive] = useState<CfdiCancellationMotive>(CfdiCancellationMotive.ErrorsWithoutReplacement);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      const result = await cancel.mutateAsync({ creditNoteId: note.id, motive });
      toast.success(result.status === OrderInvoiceStatus.Cancelled
        ? `Nota de crédito ${result.number} cancelada.`
        : `Cancelación de ${result.number} pedida: el SAT espera la respuesta del cliente.`);
      onClose();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cancelar la nota de crédito.'));
    }
  };

  return (
    <Modal show onHide={onClose} centered aria-labelledby={titleId}>
      <form onSubmit={submit}>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">Cancelar la nota de crédito {note.number}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="small">
            TienditApp le pide al SAT que la cancele; si el SAT le pide al cliente que lo acepte, sigue vigente mientras tanto.
            Con el motivo 02, después emites otra para el mismo ajuste.
          </p>
          <fieldset>
            <legend className="form-label small mb-1">Motivo</legend>
            {CREDIT_NOTE_CANCELLATION_MOTIVES.map(([value, label]) => (
              <div className="form-check" key={value}>
                <input
                  id={`credit-note-cancel-motive-${value}`}
                  type="radio"
                  name="credit-note-cancel-motive"
                  className="form-check-input"
                  checked={motive === value}
                  onChange={() => setMotive(value)}
                />
                <label className="form-check-label" htmlFor={`credit-note-cancel-motive-${value}`}>{label}</label>
              </div>
            ))}
          </fieldset>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose}>Volver</button>
          <button type="submit" className="btn btn-danger" disabled={cancel.isPending}>
            {cancel.isPending ? 'Cancelando…' : 'Cancelar nota de crédito'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
