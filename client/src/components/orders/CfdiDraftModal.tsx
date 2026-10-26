import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import {
  useCfdiDraft,
  useIssueOrderInvoice,
  useOrderInvoicing,
  useSubstituteOrderInvoice,
  type CfdiReceiver,
  type OrderInvoice,
  type OrderInvoicing,
} from '../../api/invoicing';
import type { Schemas } from '../../api/typedClient';
import { useToast } from '../../hooks/useToast';
import { OrderInvoiceStatus } from '../../types';
import { EMPTY_RECEIVER, isPlainEmail, normalizeReceiver, PAYMENT_FORMS, PAYMENT_METHODS } from '../../utils/cfdi';
import { formatMoney } from '../../utils/money';
import CfdiReceiverFields from '../invoicing/CfdiReceiverFields';
import OrderInvoiceList from '../invoicing/OrderInvoiceList';
import CreditNoteList from '../invoicing/CreditNoteList';
import PaymentComplementList from '../invoicing/PaymentComplementList';

interface CfdiDraftModalProps {
  orderId: string;
  orderNumber: string;
  /** Owners and admins cancel the order's invoices. */
  canCancel: boolean;
  /** Who manages customers keeps the fiscal data in the customer (ManageCustomers, as Clientes). */
  canSaveToCustomer: boolean;
  /**
   * A cancelled order is not invoiced, but the invoices it already has stay here: to download them and to cancel a
   * stamped one before the SAT (motive 03, the sale did not happen).
   */
  orderCancelled: boolean;
  onClose: () => void;
}

type Transfer = Schemas['CfdiTransferDto'];

function formatTransfer(transfer: Transfer | null | undefined): string {
  if (!transfer) return 'No objeto';
  if (transfer.factorType === 'Exento') return 'Exento';
  return `${formatMoney(transfer.amount ?? 0)} (${((transfer.rateOrQuota ?? 0) * 100).toFixed(0)} %)`;
}

const LIVE: OrderInvoiceStatus[] = [OrderInvoiceStatus.Stamping, OrderInvoiceStatus.Stamped, OrderInvoiceStatus.Cancelling];

/**
 * The payment complements (Fase 28C) and credit notes (Fase 28D) in force of each invoice of the order, and the invoice an
 * invoice replaces while it stands (Fase 28G): the API refuses to cancel an invoice while they stand, so its screen says
 * which go first.
 */
function liveRelated(invoicing: OrderInvoicing): Record<string, string[]> {
  const related: Record<string, string[]> = {};
  const add = (invoiceId: string, number: string) => (related[invoiceId] ??= []).push(number);
  for (const payment of invoicing.payments)
    for (const complement of payment.complements)
      if (LIVE.includes(complement.status)) add(complement.orderInvoiceId, complement.number);
  for (const adjustment of invoicing.adjustments)
    for (const note of adjustment.creditNotes)
      if (LIVE.includes(note.status)) add(note.orderInvoiceId, note.number);
  for (const invoice of invoicing.invoices)
    if (invoice.replacedByInvoiceId && LIVE.includes(invoice.status)) add(invoice.replacedByInvoiceId, invoice.number);
  return related;
}

/**
 * The invoice of an order (Fase 28A): its invoices, and the CFDI 4.0 it would stamp (concepts with their SAT codes, IVA per
 * rate and what is still missing) for the customer's fiscal data. With the platform's PAC and the business's CSD, it is
 * stamped from here; without them it is only a preview. A sale still owed is invoiced in installments (PPD, Fase 28C), and
 * the complements of its payments show under its invoices, as do the credit notes of its refunds and write-offs (Fase 28D).
 * Its stamped invoice is replaced from here too (Fase 28G): the same form, with that invoice's data to correct.
 */
export default function CfdiDraftModal({ orderId, orderNumber, canCancel, canSaveToCustomer, orderCancelled, onClose }: CfdiDraftModalProps) {
  const titleId = useId();
  const invoicing = useOrderInvoicing(orderId);
  // What a previous opening left in the cache may be old (the order since paid, the customer's data changed): the body
  // waits for this opening's answer, and a failed one is said, never taken for invoicing not being available. Once read,
  // a read in the background that fails (every 30 s while an invoice waits, or after stamping) keeps what is on screen
  // and what the person typed, and says it.
  const [read, setRead] = useState(false);
  if (!read && invoicing.isFetchedAfterMount && invoicing.isSuccess) setRead(true);
  const data = read ? invoicing.data : undefined;
  const title = orderCancelled
    ? `Facturas de ${orderNumber}`
    : !data ? `Factura de ${orderNumber}` : data.available ? `Facturar ${orderNumber}` : `Factura de ${orderNumber} (vista previa)`;

  return (
    <Modal show onHide={onClose} size="xl" centered fullscreen="md-down" aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">{title}</Modal.Title>
      </Modal.Header>
      {!invoicing.isFetchedAfterMount ? (
        <Modal.Body><p className="text-muted" aria-busy="true">Cargando…</p></Modal.Body>
      ) : (
        <InvoiceBody
          // Read again after failing, it starts over with the proposed fiscal data.
          key={data ? 'read' : 'failed'}
          orderId={orderId}
          invoicing={data}
          canCancel={canCancel}
          canSaveToCustomer={canSaveToCustomer}
          orderCancelled={orderCancelled}
          onClose={onClose}
          loadError={invoicing.isError && (
            <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
              <span className="me-auto">
                {getApiErrorMessage(invoicing.error, data
                  ? 'No se pudieron leer de nuevo las facturas de este pedido: lo que ves puede no estar al día.'
                  : 'No se pudieron cargar las facturas de este pedido.')}
              </span>
              <button type="button" className="btn btn-sm btn-outline-danger" disabled={invoicing.isFetching} onClick={() => void invoicing.refetch()}>
                Reintentar
              </button>
            </div>
          )}
        />
      )}
    </Modal>
  );
}

interface InvoiceBodyProps {
  orderId: string;
  /** Missing when it could not be read: the preview still works. */
  invoicing: OrderInvoicing | undefined;
  canCancel: boolean;
  canSaveToCustomer: boolean;
  orderCancelled: boolean;
  onClose: () => void;
  /** Why the order's invoicing could not be read, with a way to try again. */
  loadError: ReactNode;
}

function InvoiceBody({ orderId, invoicing, canCancel, canSaveToCustomer, orderCancelled, onClose, loadError }: InvoiceBodyProps) {
  const toast = useToast();
  const issue = useIssueOrderInvoice(orderId);
  const proposed = invoicing?.proposedReceiver ?? null;
  // Whom the invoice goes to: the customer's fiscal data, or the order's last invoice, to correct it.
  const [form, setForm] = useState<CfdiReceiver>(proposed ?? EMPTY_RECEIVER);
  const [receiver, setReceiver] = useState<CfdiReceiver | undefined>(proposed ? normalizeReceiver(proposed) : undefined);
  const [showReceiver, setShowReceiver] = useState(invoicing?.available ?? false);
  const [saveToCustomer, setSaveToCustomer] = useState(true);
  // Where its XML and PDF go once stamped, and those of its complements and credit notes (Fase 28E): optional.
  const [email, setEmail] = useState(invoicing?.proposedEmail ?? '');
  const typedEmail = email.trim();
  const emailValid = !typedEmail || isPlainEmail(typedEmail);
  // Offered to who manages customers only: the API asks for ManageCustomers to keep it.
  const offerSaveToCustomer = canSaveToCustomer && !!invoicing?.customerId;
  const draft = useCfdiDraft(orderId, receiver, !orderCancelled);
  const cfdi = draft.data;
  // The stamped invoice being replaced (Fase 28G), as the order's invoicing reads it now.
  const substitute = useSubstituteOrderInvoice(orderId);
  const [substituting, setSubstituting] = useState<OrderInvoice | null>(null);
  const target = substituting ? invoicing?.invoices.find((invoice) => invoice.id === substituting.id) : undefined;
  const substitutingId = substituting?.id ?? null;
  // Gone from the order's current invoice meanwhile (cancelled, or replaced from elsewhere): it is not replaced from here.
  const targetGone = !!substituting && !substitute.isPending
    && (!target || (!target.canSubstitute && !target.substitutionProblem));
  // The focus goes to what the mode says, and when it ends back to what opened it, or to the invoices once replaced (the
  // invoice it opened from is no longer offered to be replaced).
  const notice = useRef<HTMLDivElement>(null);
  const invoicesHeading = useRef<HTMLHeadingElement>(null);
  const opener = useRef<HTMLElement | null>(null);
  const focusAfter = useRef<'opener' | 'invoices' | null>(null);
  useEffect(() => {
    if (substitutingId) {
      notice.current?.focus();
      return;
    }
    const back = focusAfter.current === 'opener' ? opener.current : null;
    if (focusAfter.current === null) return;
    focusAfter.current = null;
    if (back?.isConnected && !back.hasAttribute('disabled')) back.focus();
    else invoicesHeading.current?.focus();
  }, [substitutingId]);

  const available = invoicing?.available ?? false;
  // The email goes with an invoice that can be stamped now, to a customer with RFC.
  const showEmail = available && (!!invoicing?.canInvoice || !!substituting) && !!receiver;
  const edited = receiver === undefined || JSON.stringify(normalizeReceiver(form)) !== JSON.stringify(receiver);
  // Only on this opening's preview: while it is read again, what is on screen may be old.
  const readyToStamp = available && !!receiver && !edited && !!cfdi?.isReadyToStamp && !cfdi.isGeneralPublic && !draft.isFetching
    && !issue.isPending && !substitute.isPending && emailValid;
  const canStamp = readyToStamp && !substituting && !!invoicing?.canInvoice;
  const canReplace = readyToStamp && !!target?.canSubstitute;

  /** The form takes the invoice's data and email, to correct them. */
  const startSubstituting = (invoice: OrderInvoice) => {
    opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setSubstituting(invoice);
    setForm(invoice.receiver);
    setReceiver(normalizeReceiver(invoice.receiver));
    setShowReceiver(true);
    setEmail(invoice.receiverEmail ?? invoicing?.proposedEmail ?? '');
  };

  /** Back to what the order proposes, or, once replaced, to the data the new one went with. */
  const stopSubstituting = (kept?: { receiver: CfdiReceiver; email: string }) => {
    focusAfter.current = kept ? 'invoices' : 'opener';
    setSubstituting(null);
    const next = kept?.receiver ?? proposed;
    setForm(next ?? EMPTY_RECEIVER);
    setReceiver(next ? normalizeReceiver(next) : undefined);
    setEmail(kept ? kept.email : invoicing?.proposedEmail ?? '');
  };

  const applyReceiver = (event: React.FormEvent) => {
    event.preventDefault();
    setReceiver(normalizeReceiver(form));
  };

  const stamp = async () => {
    if (!receiver) return;
    try {
      const invoice = await issue.mutateAsync({
        receiver,
        // Only what the person saw: the box shows with the receiver's data.
        saveToCustomer: offerSaveToCustomer && showReceiver && saveToCustomer,
        receiverEmail: typedEmail || null,
      });
      if (invoice.status === OrderInvoiceStatus.Stamped) {
        toast.success(typedEmail ? `Factura ${invoice.number} timbrada: se está enviando a ${typedEmail}.` : `Factura ${invoice.number} timbrada.`);
      }
      else if (invoice.status === OrderInvoiceStatus.Refused) toast.error(invoice.failureMessage ?? 'El PAC rechazó la factura.');
      else toast.info(`El PAC no respondió: TienditApp sigue pidiendo el timbre de ${invoice.number}.`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo timbrar la factura.'));
    }
  };

  const replace = async () => {
    if (!receiver || !substituting) return;
    const replaced = substituting.number;
    try {
      const invoice = await substitute.mutateAsync({
        invoiceId: substituting.id,
        receiver,
        saveToCustomer: offerSaveToCustomer && showReceiver && saveToCustomer,
        receiverEmail: typedEmail || null,
      });
      // Refused, the old one is still the order's invoice: the form stays, to correct it again.
      if (invoice.status === OrderInvoiceStatus.Refused) {
        toast.error(`${invoice.failureMessage ?? 'El PAC rechazó la factura.'} La ${replaced} sigue vigente.`);
        return;
      }
      if (invoice.status === OrderInvoiceStatus.Stamped) {
        toast.success(typedEmail
          ? `Factura ${invoice.number} timbrada en lugar de la ${replaced}: se está enviando a ${typedEmail}.`
          : `Factura ${invoice.number} timbrada en lugar de la ${replaced}.`);
      } else {
        toast.info(`El PAC no respondió: TienditApp sigue pidiendo el timbre de ${invoice.number} y después cancela la ${replaced}.`);
      }
      stopSubstituting({ receiver, email: typedEmail });
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo sustituir la factura.'));
    }
  };

  return (
    <>
      <Modal.Body>
        {loadError}
        {invoicing && (
          <OrderInvoiceList
            orderId={orderId}
            invoices={invoicing.invoices}
            canCancel={canCancel}
            liveRelated={liveRelated(invoicing)}
            proposedEmail={invoicing.proposedEmail}
            onSubstitute={!orderCancelled && available ? startSubstituting : undefined}
            substitutingId={substitutingId}
            headingRef={invoicesHeading}
          />
        )}
        {invoicing && (
          <PaymentComplementList
            orderId={orderId}
            payments={invoicing.payments}
            inInstallments={invoicing.invoices.some((invoice) => invoice.paymentMethod === 'PPD'
              && invoice.status !== OrderInvoiceStatus.Refused && invoice.status !== OrderInvoiceStatus.Cancelled)}
            canCancel={canCancel}
            invoiceEmails={Object.fromEntries(invoicing.invoices.map((invoice) => [invoice.id, invoice.receiverEmail]))}
            proposedEmail={invoicing.proposedEmail}
          />
        )}
        {invoicing && (
          <CreditNoteList
            orderId={orderId}
            adjustments={invoicing.adjustments}
            invoices={invoicing.invoices}
            canCancel={canCancel}
            proposedEmail={invoicing.proposedEmail}
          />
        )}

        {orderCancelled && invoicing && (
          <p className="text-muted small mb-0">
            {invoicing.invoices.length === 0
              ? 'Este pedido está cancelado y no tiene facturas.'
              : 'El pedido está cancelado: si una de sus facturas sigue timbrada, cancélala ante el SAT con el motivo 03 (la venta no se llevó a cabo).'}
          </p>
        )}
        {!orderCancelled && (
          <>
          {!invoicing ? null : !available ? (
            <p className="text-muted small">
              Así quedaría el CFDI 4.0 de esta venta. Es una vista previa: la facturación todavía no está disponible en TienditApp.
            </p>
          ) : (
            <>
              {invoicing?.sandbox && (
                <div className="alert alert-warning py-2 small">Modo de pruebas: las facturas que timbres no tienen validez fiscal.</div>
              )}
              {invoicing?.problem && !invoicing.canInvoice && !substituting && (
                <div className="alert alert-info py-2 small" role="status">{invoicing.problem}</div>
              )}
            </>
          )}

          {substituting && (
            <div ref={notice} tabIndex={-1} role="group" className="alert alert-info py-2 small" aria-labelledby="cfdi-substituting-title">
              <div id="cfdi-substituting-title" className="fw-semibold">Sustituir la factura {substituting.number}</div>
              <p className="mb-1">
                Corrige los datos y timbra la nueva: relaciona la {substituting.number} como «Sustitución de los CFDI previos» (04).
                En cuanto quede timbrada, TienditApp le pide al SAT cancelar la {substituting.number} con relación (motivo 01); si el
                SAT todavía no recibe la nueva, lo vuelve a intentar durante 3 días.
              </p>
              {target && !target.canSubstitute && target.substitutionProblem && (
                <p className="mb-1 text-danger">{target.substitutionProblem}</p>
              )}
              {targetGone && (
                <p className="mb-1 text-danger">La {substituting.number} ya no es la factura vigente del pedido: no se sustituye.</p>
              )}
              <button type="button" className="btn btn-link btn-sm p-0" disabled={substitute.isPending} onClick={() => stopSubstituting()}>
                No sustituir
              </button>
            </div>
          )}

          <button
            type="button"
            className="btn btn-link btn-sm px-0 mb-2"
            onClick={() => setShowReceiver((value) => !value)}
            aria-expanded={showReceiver}
          >
            <i className={`${showReceiver ? 'ri-arrow-up-s-line' : 'ri-user-line'} me-1`} aria-hidden="true"></i>
            {receiver ? `Factura para ${receiver.rfc}` : 'Facturar a un cliente con RFC'}
          </button>
          {showReceiver && (
            <form className="border rounded p-2 mb-3" onSubmit={applyReceiver}>
              <CfdiReceiverFields idPrefix="cfdi" value={form} onChange={setForm} />
              {available && offerSaveToCustomer && (
                <div className="form-check mt-2">
                  <input
                    id="cfdi-save-to-customer"
                    type="checkbox"
                    className="form-check-input"
                    checked={saveToCustomer}
                    onChange={(event) => setSaveToCustomer(event.target.checked)}
                  />
                  <label className="form-check-label small" htmlFor="cfdi-save-to-customer">
                    Guardar estos datos{showEmail && typedEmail ? ' y el correo' : ''} en {invoicing.customerName ?? 'el cliente'} para sus próximas facturas
                  </label>
                </div>
              )}
              <div className="d-flex gap-2 mt-2">
                <button type="submit" className="btn btn-sm btn-outline-secondary">Ver con estos datos</button>
                {receiver && !substituting && (
                  <button type="button" className="btn btn-sm btn-light" onClick={() => setReceiver(undefined)}>
                    Público en general
                  </button>
                )}
              </div>
              {available && receiver && edited && (
                <p className="small text-muted mb-0 mt-2">Cambiaste los datos: revisa la factura con ellos antes de timbrarla.</p>
              )}
            </form>
          )}

          {showEmail && (
            <div className="mb-3">
              <label className="form-label small mb-1" htmlFor="cfdi-email">Correo del cliente (opcional)</label>
              <input
                id="cfdi-email"
                type="email"
                className={`form-control${emailValid ? '' : ' is-invalid'}`}
                maxLength={254}
                autoComplete="off"
                value={email}
                disabled={issue.isPending || substitute.isPending}
                onChange={(event) => setEmail(event.target.value)}
                aria-invalid={emailValid ? undefined : true}
                aria-describedby="cfdi-email-help"
              />
              <div id="cfdi-email-help" className={emailValid ? 'form-text' : 'invalid-feedback'}>
                {emailValid
                  ? 'Al timbrarse le llegan el XML y el PDF, y después los de sus complementos de pago y notas de crédito.'
                  : 'Escribe un correo válido, como nombre@ejemplo.com, o déjalo vacío.'}
              </div>
            </div>
          )}

          {draft.isPending && <p className="text-muted" aria-busy="true">Calculando…</p>}
          {draft.isError && (
            <div className="alert alert-danger">{getApiErrorMessage(draft.error, 'No se pudo calcular la factura.')}</div>
          )}

          {cfdi && (
            <>
              {cfdi.blockers.length > 0 ? (
                <div className="alert alert-danger">
                  <div className="fw-semibold mb-1">Falta para poder timbrarla:</div>
                  <ul className="mb-0">{cfdi.blockers.map((item) => <li key={item}>{item}</li>)}</ul>
                </div>
              ) : cfdi.isGeneralPublic ? (
                <div className="alert alert-secondary py-2 small">
                  Para timbrarla captura los datos fiscales del cliente: la venta a público en general va en la factura global del periodo
                  (Reportes › Factura global).
                </div>
              ) : (
                <div className="alert alert-success py-2">
                  <i className="ri-checkbox-circle-line me-1" aria-hidden="true"></i>Lista para timbrar.
                </div>
              )}
              {cfdi.warnings.length > 0 && (
                <div className="alert alert-warning">
                  <ul className="mb-0">{cfdi.warnings.map((item) => <li key={item}>{item}</li>)}</ul>
                </div>
              )}

              <div className="row g-3 mb-3 small">
                <div className="col-md-6">
                  <div className="fw-semibold">Emisor</div>
                  <div>{cfdi.issuer.name || <span className="text-danger">Sin razón social</span>}</div>
                  <div>RFC {cfdi.issuer.rfc || '—'} · Régimen {cfdi.issuer.fiscalRegime || '—'}</div>
                  <div>Lugar de expedición {cfdi.issuedAtPostalCode || '—'}</div>
                </div>
                <div className="col-md-6">
                  <div className="fw-semibold">Receptor</div>
                  <div>{cfdi.receiver.name}</div>
                  <div>RFC {cfdi.receiver.rfc} · Régimen {cfdi.receiver.fiscalRegime} · Uso {cfdi.receiver.cfdiUse}</div>
                  <div>
                    Pago {cfdi.paymentMethod} {PAYMENT_METHODS[cfdi.paymentMethod] ? `(${PAYMENT_METHODS[cfdi.paymentMethod].toLowerCase()})` : ''}
                    {' · '}Forma {cfdi.paymentForm} {PAYMENT_FORMS[cfdi.paymentForm] ? `(${PAYMENT_FORMS[cfdi.paymentForm]})` : ''}
                  </div>
                </div>
              </div>

              <div className="table-responsive">
                <table className="table table-sm align-middle">
                  <thead>
                    <tr>
                      <th>Clave</th>
                      <th>Unidad</th>
                      <th>Descripción</th>
                      <th className="text-end">Cantidad</th>
                      <th className="text-end">Valor unitario</th>
                      <th className="text-end">Importe</th>
                      <th className="text-end">IVA</th>
                    </tr>
                  </thead>
                  <tbody>
                    {cfdi.concepts.map((concept) => (
                      <tr key={concept.orderItemId}>
                        <td className="ta-money">{concept.productCode}</td>
                        <td>{concept.unitCode}</td>
                        <td>{concept.description}</td>
                        <td className="text-end ta-money">{concept.quantity.toLocaleString('es-MX', { maximumFractionDigits: 3 })}</td>
                        <td className="text-end ta-money">
                          {concept.unitValue.toLocaleString('es-MX', { minimumFractionDigits: 2, maximumFractionDigits: 6 })}
                        </td>
                        <td className="text-end ta-money">{formatMoney(concept.amount)}</td>
                        <td className="text-end ta-money small">{formatTransfer(concept.transfer as Transfer | null)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="ms-auto" style={{ maxWidth: '22rem' }}>
                <div className="d-flex justify-content-between"><span>Subtotal</span><span className="ta-money">{formatMoney(cfdi.subTotal)}</span></div>
                {cfdi.transfers.filter((transfer) => transfer.factorType !== 'Exento').map((transfer) => (
                  <div className="d-flex justify-content-between small text-muted" key={`${transfer.factorType}-${transfer.rateOrQuota}`}>
                    <span>IVA {((transfer.rateOrQuota ?? 0) * 100).toFixed(0)} % sobre {formatMoney(transfer.base)}</span>
                    <span className="ta-money">{formatMoney(transfer.amount ?? 0)}</span>
                  </div>
                ))}
                <div className="d-flex justify-content-between fw-bold border-top pt-1">
                  <span>Total</span><span className="ta-money">{formatMoney(cfdi.total)}</span>
                </div>
              </div>
            </>
          )}
          </>
        )}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose}>Cerrar</button>
        {substituting ? (
          <button type="button" className="btn btn-primary" disabled={!canReplace} onClick={() => void replace()}>
            {substitute.isPending ? 'Timbrando…' : `Timbrar y sustituir la ${substituting.number}`}
          </button>
        ) : !orderCancelled && available && invoicing?.canInvoice && (
          <button type="button" className="btn btn-primary" disabled={!canStamp} onClick={() => void stamp()}>
            {issue.isPending ? 'Timbrando…' : 'Timbrar factura'}
          </button>
        )}
      </Modal.Footer>
    </>
  );
}
