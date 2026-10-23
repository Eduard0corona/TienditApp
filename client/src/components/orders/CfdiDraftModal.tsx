import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useIssueOrderInvoice, useOrderInvoicing, type OrderInvoicing } from '../../api/invoicing';
import { useCfdiDraft, type CfdiReceiver } from '../../api/queries';
import type { Schemas } from '../../api/typedClient';
import { useToast } from '../../hooks/useToast';
import { OrderInvoiceStatus } from '../../types';
import { EMPTY_RECEIVER, normalizeReceiver, PAYMENT_FORMS } from '../../utils/cfdi';
import { formatMoney } from '../../utils/money';
import CfdiReceiverFields from '../invoicing/CfdiReceiverFields';
import OrderInvoiceList from '../invoicing/OrderInvoiceList';

interface CfdiDraftModalProps {
  orderId: string;
  orderNumber: string;
  /** Owners and admins cancel the order's invoices. */
  canCancel: boolean;
  onClose: () => void;
}

type Transfer = Schemas['CfdiTransferDto'];

function formatTransfer(transfer: Transfer | null | undefined): string {
  if (!transfer) return 'No objeto';
  if (transfer.factorType === 'Exento') return 'Exento';
  return `${formatMoney(transfer.amount ?? 0)} (${((transfer.rateOrQuota ?? 0) * 100).toFixed(0)} %)`;
}

/**
 * The invoice of an order (Fase 28A): its invoices, and the CFDI 4.0 it would stamp (concepts with their SAT codes, IVA per
 * rate and what is still missing) for the customer's fiscal data. With the platform's PAC and the business's CSD, it is
 * stamped from here; without them it is only a preview.
 */
export default function CfdiDraftModal({ orderId, orderNumber, canCancel, onClose }: CfdiDraftModalProps) {
  const titleId = useId();
  const invoicing = useOrderInvoicing(orderId);
  const available = invoicing.data?.available ?? false;

  return (
    <Modal show onHide={onClose} size="xl" centered fullscreen="md-down" aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">
          {available ? `Facturar ${orderNumber}` : `Factura de ${orderNumber} (vista previa)`}
        </Modal.Title>
      </Modal.Header>
      {invoicing.isPending ? (
        <Modal.Body><p className="text-muted" aria-busy="true">Cargando…</p></Modal.Body>
      ) : (
        <InvoiceBody orderId={orderId} invoicing={invoicing.data} canCancel={canCancel} onClose={onClose} />
      )}
    </Modal>
  );
}

interface InvoiceBodyProps {
  orderId: string;
  /** Missing when it could not be read: the preview still works. */
  invoicing: OrderInvoicing | undefined;
  canCancel: boolean;
  onClose: () => void;
}

function InvoiceBody({ orderId, invoicing, canCancel, onClose }: InvoiceBodyProps) {
  const toast = useToast();
  const issue = useIssueOrderInvoice(orderId);
  const proposed = invoicing?.proposedReceiver ?? null;
  // Whom the invoice goes to: the customer's fiscal data, or the order's last invoice, to correct it.
  const [form, setForm] = useState<CfdiReceiver>(proposed ?? EMPTY_RECEIVER);
  const [receiver, setReceiver] = useState<CfdiReceiver | undefined>(proposed ? normalizeReceiver(proposed) : undefined);
  const [showReceiver, setShowReceiver] = useState(invoicing?.available ?? false);
  const [saveToCustomer, setSaveToCustomer] = useState(true);
  const draft = useCfdiDraft(orderId, receiver);
  const cfdi = draft.data;

  const available = invoicing?.available ?? false;
  const edited = receiver === undefined || JSON.stringify(normalizeReceiver(form)) !== JSON.stringify(receiver);
  const canStamp = available && !!invoicing?.canInvoice && !!receiver && !edited && !!cfdi?.isReadyToStamp
    && !cfdi.isGeneralPublic && !issue.isPending;

  const applyReceiver = (event: React.FormEvent) => {
    event.preventDefault();
    setReceiver(normalizeReceiver(form));
  };

  const stamp = async () => {
    if (!receiver) return;
    try {
      const invoice = await issue.mutateAsync({ receiver, saveToCustomer: !!invoicing?.customerId && saveToCustomer });
      if (invoice.status === OrderInvoiceStatus.Stamped) toast.success(`Factura ${invoice.number} timbrada.`);
      else if (invoice.status === OrderInvoiceStatus.Refused) toast.error(invoice.failureMessage ?? 'El PAC rechazó la factura.');
      else toast.info(`El PAC no respondió: TienditApp sigue pidiendo el timbre de ${invoice.number}.`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo timbrar la factura.'));
    }
  };

  return (
    <>
      <Modal.Body>
        {invoicing && <OrderInvoiceList orderId={orderId} invoices={invoicing.invoices} canCancel={canCancel} />}

        {!available ? (
          <p className="text-muted small">
            Así quedaría el CFDI 4.0 de esta venta. Es una vista previa: la facturación todavía no está disponible en TienditApp.
          </p>
        ) : (
          <>
            {invoicing?.sandbox && (
              <div className="alert alert-warning py-2 small">Modo de pruebas: las facturas que timbres no tienen validez fiscal.</div>
            )}
            {invoicing?.problem && !invoicing.canInvoice && (
              <div className="alert alert-info py-2 small" role="status">{invoicing.problem}</div>
            )}
          </>
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
            {available && invoicing?.customerId && (
              <div className="form-check mt-2">
                <input
                  id="cfdi-save-to-customer"
                  type="checkbox"
                  className="form-check-input"
                  checked={saveToCustomer}
                  onChange={(event) => setSaveToCustomer(event.target.checked)}
                />
                <label className="form-check-label small" htmlFor="cfdi-save-to-customer">
                  Guardar estos datos en {invoicing.customerName ?? 'el cliente'} para sus próximas facturas
                </label>
              </div>
            )}
            <div className="d-flex gap-2 mt-2">
              <button type="submit" className="btn btn-sm btn-outline-secondary">Ver con estos datos</button>
              {receiver && (
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
                Para timbrarla captura los datos fiscales del cliente: la venta a público en general va en la factura global.
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
                  Pago {cfdi.paymentMethod} · Forma {cfdi.paymentForm} {PAYMENT_FORMS[cfdi.paymentForm] ? `(${PAYMENT_FORMS[cfdi.paymentForm]})` : ''}
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
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose}>Cerrar</button>
        {available && invoicing?.canInvoice && (
          <button type="button" className="btn btn-primary" disabled={!canStamp} onClick={() => void stamp()}>
            {issue.isPending ? 'Timbrando…' : 'Timbrar factura'}
          </button>
        )}
      </Modal.Footer>
    </>
  );
}
