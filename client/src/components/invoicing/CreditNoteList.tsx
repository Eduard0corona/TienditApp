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
  type OrderInvoice,
} from '../../api/invoicing';
import { useToast } from '../../hooks/useToast';
import { CfdiCancellationMotive, CfdiEmailDocument, CreditNoteAdjustment, OrderInvoiceStatus } from '../../types';
import {
  creditNoteCancellationMotives, invoiceStatusChip, invoiceStatusLabel, isStuckStamping, PAYMENT_FORMS, saveBlob,
} from '../../utils/cfdi';
import { formatDateTime } from '../../utils/dates';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import { getWriteOffReasonLabel } from '../../utils/writeOffReasons';
import { CfdiEmailStatus, SendCfdiEmailButton } from './CfdiEmails';
import { CfdiWhatsAppStatus, SendCfdiWhatsAppButton } from './CfdiWhatsApps';
import StuckStampNote from './StuckStampNote';

interface CreditNoteListProps {
  orderId: string;
  adjustments: OrderAdjustment[];
  /** The order's invoices: a note of one that is no longer the stamped one says which it was. */
  invoices: OrderInvoice[];
  /** Owners and admins cancel; whoever manages the orders issues, retries and downloads. */
  canCancel: boolean;
  /** The email the order's invoicing proposes, to send a note whose invoice has none (Fase 28E). */
  proposedEmail?: string | null;
  /** The business's WhatsApp is connected (Fase 28M): the notes of its invoices can go by WhatsApp. */
  whatsAppConnected?: boolean;
  /** The WhatsApp the order's invoicing proposes, to send a note whose invoice has none. */
  proposedWhatsApp?: string | null;
  /** The global invoice that covers the sale now, whose credit notes its refunds (Fase 28I) and write-offs (Fase 28J) take. */
  globalInvoiceNumber?: string | null;
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

/**
 * «Reembolso del 24 oct 2026, 5:00 p.m.» or «Condonación del …», and how an action names it, with its amount: two refunds
 * of a voided sale share the minute («del reembolso de $20.00 del …»).
 */
function adjustmentNames(adjustment: OrderAdjustment): { name: string; ofIt: string } {
  const at = formatDateTime(adjustment.at);
  const amount = formatMoney(adjustment.amount);
  return adjustment.kind === CreditNoteAdjustment.Refund
    ? { name: `Reembolso del ${at}`, ofIt: `del reembolso de ${amount} del ${at}` }
    : { name: `Condonación del ${at}`, ofIt: `de la condonación de ${amount} del ${at}` };
}

interface Cancelling {
  note: CreditNote;
  kind: CreditNoteAdjustment;
}

/**
 * The refunds and write-offs of an invoiced order (Fase 28D) with their credit notes (CFDI de egreso): TienditApp issues
 * each one on its own a few minutes after it is recorded (or after the invoice is stamped); from here it is issued before
 * that («Emitir ahora»), again after a refused or cancelled one, asked again of the PAC, cancelled or downloaded. The
 * refunds (Fase 28I) and write-offs (Fase 28J) of a sale a global invoice covers take theirs of that global invoice, to the
 * general public: those never go by email.
 */
export default function CreditNoteList({
  orderId,
  adjustments,
  invoices,
  canCancel,
  proposedEmail,
  whatsAppConnected = false,
  proposedWhatsApp,
  globalInvoiceNumber,
}: CreditNoteListProps) {
  const toast = useToast();
  const outcome = useCreditNoteOutcome();
  const issue = useIssueCreditNote(orderId);
  const retry = useRetryCreditNote(orderId);
  const [cancelling, setCancelling] = useState<Cancelling | null>(null);
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

  const stamped = invoices.find((invoice) => invoice.status === OrderInvoiceStatus.Stamped);
  const invoiceNumbers = new Map(invoices.map((invoice) => [invoice.id, invoice.number]));
  const invoiceEmails = new Map(invoices.map((invoice) => [invoice.id, invoice.receiverEmail]));
  const invoiceWhatsApps = new Map(invoices.map((invoice) => [invoice.id, invoice.receiverWhatsApp]));

  return (
    <div className="mb-3">
      <h3 className="h6 mb-1">Notas de crédito</h3>
      <p className="small text-muted mb-2">
        {stamped
          ? `La factura ${stamped.number} ampara la venta completa: cada reembolso y cada condonación lleva su nota de crédito, `
            + 'que TienditApp emite por su cuenta unos minutos después de registrarlo (o de timbrar la factura, si fue antes). '
            + 'Si uno no la lleva, dice por qué.'
          : globalInvoiceNumber
            ? `La factura global ${globalInvoiceNumber} ampara esta venta: cada reembolso y cada condonación lleva su nota de `
              + 'crédito al público en general, relacionada con esa factura global, que TienditApp emite por su cuenta unos '
              + 'minutos después de registrarlo (o de timbrar la factura global, si fue antes). Si uno no la lleva, dice por qué.'
            : 'Lo que se devolvió o se condonó de este pedido. Sus notas de crédito se emiten con una factura timbrada.'}
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
                          Por <span className="ta-money">{formatMoney(note.total)}</span> (subtotal{' '}
                          <span className="ta-money">{formatMoney(note.subTotal)}</span> e IVA{' '}
                          <span className="ta-money">{formatMoney(note.taxTotal)}</span>)
                        </span>
                      </div>
                      <div className="small text-muted mt-1">
                        Forma {note.paymentForm}
                        {PAYMENT_FORMS[note.paymentForm] ? ` (${PAYMENT_FORMS[note.paymentForm]})` : ''}
                        {note.issuedAutomatically ? ' · la emitió TienditApp' : ''}
                        {note.orderInvoiceId != null && note.orderInvoiceId !== stamped?.id && invoiceNumbers.has(note.orderInvoiceId)
                          && ` · de la factura ${invoiceNumbers.get(note.orderInvoiceId)}`}
                        {note.globalInvoiceNumber != null && ` · al público en general, de la factura global ${note.globalInvoiceNumber}`}
                        {note.uuid && <> · folio fiscal <span className="ta-money">{note.uuid}</span></>}
                        {note.stampedAt && <> · timbrada el {formatDateTime(note.stampedAt)}</>}
                      </div>
                      {note.status === OrderInvoiceStatus.Stamping && !isStuckStamping(note) && (
                        <p className="small text-muted mb-0 mt-1">
                          El PAC no respondió todavía. TienditApp lo vuelve a intentar con la misma solicitud: nunca timbra dos veces.
                        </p>
                      )}
                      {isStuckStamping(note) && <StuckStampNote canReview={canCancel} />}
                      {note.status === OrderInvoiceStatus.Cancelling && (
                        <p className="small text-muted mb-0 mt-1">
                          {note.globalInvoiceId != null
                            ? 'El SAT está procesando la cancelación; TienditApp la vuelve a consultar.'
                            : 'El SAT pidió al cliente que acepte la cancelación.'}
                        </p>
                      )}
                      {note.failureMessage && (
                        <p className={`small mb-0 mt-1 ${note.status === OrderInvoiceStatus.Refused ? 'text-danger' : 'text-warning-emphasis'}`}>
                          {note.failureMessage}
                        </p>
                      )}
                      <CfdiEmailStatus emails={note.emails} />
                      <CfdiWhatsAppStatus messages={note.whatsApps} cancelled={note.status === OrderInvoiceStatus.Cancelled} />
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
                        {note.status === OrderInvoiceStatus.Stamped && note.orderInvoiceId != null && (
                          <SendCfdiEmailButton
                            orderId={orderId}
                            document={CfdiEmailDocument.CreditNote}
                            cfdiId={note.id}
                            number={note.number}
                            invoiceEmail={invoiceEmails.get(note.orderInvoiceId)}
                            latestEmail={note.emails[0]}
                            proposedEmail={proposedEmail}
                          />
                        )}
                        {whatsAppConnected && note.status === OrderInvoiceStatus.Stamped && note.orderInvoiceId != null && (
                          <SendCfdiWhatsAppButton
                            orderId={orderId}
                            document={CfdiEmailDocument.CreditNote}
                            cfdiId={note.id}
                            number={note.number}
                            invoiceWhatsApp={invoiceWhatsApps.get(note.orderInvoiceId)}
                            latestWhatsApp={note.whatsApps[0]}
                            proposedWhatsApp={proposedWhatsApp}
                          />
                        )}
                        {note.status === OrderInvoiceStatus.Stamping && !isStuckStamping(note) && (
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
                            onClick={() => setCancelling({ note, kind: adjustment.kind })}
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
      {cancelling && (
        <CancelCreditNoteModal orderId={orderId} note={cancelling.note} kind={cancelling.kind} onClose={() => setCancelling(null)} />
      )}
    </div>
  );
}

interface CancelCreditNoteModalProps {
  orderId: string;
  note: CreditNote;
  /** What it credits: the reasons and what comes after name it. */
  kind: CreditNoteAdjustment;
  onClose: () => void;
}

/** Owners and admins ask the SAT to cancel a stamped credit note, with its reason. */
function CancelCreditNoteModal({ orderId, note, kind, onClose }: CancelCreditNoteModalProps) {
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
        : note.globalInvoiceId != null
          ? `Cancelación de ${result.number} pedida: el SAT la está procesando.`
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
            {note.globalInvoiceId != null
              ? 'TienditApp le pide al SAT que la cancele; va al público en general, así que nadie tiene que aceptarla. '
              : 'TienditApp le pide al SAT que la cancele; si el SAT le pide al cliente que lo acepte, sigue vigente mientras tanto. '}
            {kind === CreditNoteAdjustment.Refund
              ? 'Un reembolso registrado no se deshace: su nota solo se cancela por errores (02), y después emites otra para el mismo reembolso.'
              : 'Con el motivo 02, después emites otra para la misma condonación; con el 03, la condonación se deshace después.'}
          </p>
          <fieldset>
            <legend className="form-label small mb-1">Motivo</legend>
            {creditNoteCancellationMotives(kind).map(([value, label]) => (
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
