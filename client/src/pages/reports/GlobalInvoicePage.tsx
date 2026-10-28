import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { Link, useParams } from 'react-router-dom';
import { getApiErrorMessage, getApiErrorStatus } from '../../api/errors';
import {
  downloadGlobalInvoiceFile,
  useCancelGlobalInvoice,
  useGlobalInvoice,
  useRetryGlobalInvoice,
  type GlobalInvoice,
  type GlobalInvoiceDetail,
} from '../../api/invoicing';
import PageTitle from '../../components/common/PageTitle';
import { globalPeriodLabel } from '../../components/invoicing/globalPeriod';
import { useToast } from '../../hooks/useToast';
import { CfdiCancellationMotive, OrderInvoiceStatus } from '../../types';
import { GLOBAL_CANCELLATION_MOTIVES, PAYMENT_FORMS, invoiceStatusChip, invoiceStatusLabel, saveBlob } from '../../utils/cfdi';
import { formatDateTime } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

const PARENT = { label: 'Factura global', to: '/reports/global-invoices' };

// On a phone a credit note's status goes under its folio, so the sale and the total fit; its own column from a tablet up.
const FROM_TABLET = 'd-none d-sm-table-cell';
const LIVE: OrderInvoiceStatus[] = [OrderInvoiceStatus.Stamping, OrderInvoiceStatus.Stamped, OrderInvoiceStatus.Cancelling];

/**
 * Reportes › Factura global › una: the global invoice with its sales, its files, and what can be done with it (ask the PAC
 * again while it stamps; cancel it once stamped), and the credit notes of its sales' refunds (Fase 28I): while one stands,
 * the SAT does not cancel it, so the screen says which go first.
 */
export default function GlobalInvoicePage() {
  const { id = '' } = useParams<{ id: string }>();
  const invoice = useGlobalInvoice(id);

  if (invoice.isPending) {
    return (
      <>
        <PageTitle title="Factura global" parent={PARENT} />
        <p className="text-muted" aria-busy="true">Cargando…</p>
      </>
    );
  }
  // Only without the invoice: a background read that fails (while it stamps, after a change) keeps it on screen.
  if (!invoice.data) {
    const missing = getApiErrorStatus(invoice.error) === 404;
    return (
      <>
        <PageTitle title="Factura global" parent={PARENT} />
        <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
          <span className="me-auto">
            {missing ? 'No encontramos esta factura global.' : getApiErrorMessage(invoice.error, 'No se pudo cargar la factura global.')}
          </span>
          {!missing && <button type="button" className="btn btn-sm btn-light" onClick={() => void invoice.refetch()}>Reintentar</button>}
        </div>
      </>
    );
  }
  return (
    <GlobalInvoiceScreen
      invoice={invoice.data}
      refreshError={invoice.isError ? getApiErrorMessage(invoice.error, 'No se pudo actualizar la factura global.') : null}
      onRefresh={() => void invoice.refetch()}
    />
  );
}

interface GlobalInvoiceScreenProps {
  invoice: GlobalInvoiceDetail;
  /** Why the last read failed, while what was read before stays on screen. */
  refreshError: string | null;
  onRefresh: () => void;
}

function GlobalInvoiceScreen({ invoice, refreshError, onRefresh }: GlobalInvoiceScreenProps) {
  const id = useId();
  const toast = useToast();
  const retry = useRetryGlobalInvoice();
  const [cancelling, setCancelling] = useState(false);
  const [downloading, setDownloading] = useState<'xml' | 'pdf' | null>(null);
  const paymentForm = `${invoice.paymentForm} · ${PAYMENT_FORMS[invoice.paymentForm] ?? 'Otra'}`;
  const creditNotes = invoice.orders.flatMap((order) => order.creditNotes.map((note) => ({ ...note, orderNumber: order.orderNumber })));
  const notesIn = (statuses: OrderInvoiceStatus[]) => creditNotes
    .filter((note) => statuses.includes(note.status))
    .map((note) => note.number)
    .sort((a, b) => a.localeCompare(b, 'es', { numeric: true }));
  // A stamped one is cancelled in its order; one that is stamping or cancelling has to finish first.
  const stampedNotes = notesIn([OrderInvoiceStatus.Stamped]);
  const waitingNotes = notesIn([OrderInvoiceStatus.Stamping, OrderInvoiceStatus.Cancelling]);
  const hasLiveNotes = creditNotes.some((note) => LIVE.includes(note.status));

  const download = async (kind: 'xml' | 'pdf') => {
    setDownloading(kind);
    try {
      const file = await downloadGlobalInvoiceFile(invoice.id, kind);
      saveBlob(file.blob, file.fileName);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, kind === 'pdf' ? 'No se pudo descargar el PDF.' : 'No se pudo descargar el XML.'));
    } finally {
      setDownloading(null);
    }
  };

  const askAgain = async () => {
    try {
      const result = await retry.mutateAsync(invoice.id);
      if (result.status === OrderInvoiceStatus.Stamped) toast.success(`Factura global ${result.number} timbrada.`);
      else if (result.status === OrderInvoiceStatus.Refused) toast.error(result.failureMessage ?? 'El PAC rechazó la factura global.');
      else toast.info('El PAC todavía no responde: TienditApp lo vuelve a intentar cada pocos minutos.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo volver a pedir el timbre.'));
    }
  };

  return (
    <>
      <PageTitle title={`Factura global ${invoice.number}`} parent={PARENT} />
      {refreshError && (
        <div className="alert alert-warning d-flex flex-wrap align-items-center gap-2" role="alert">
          <span className="me-auto">{refreshError} Esto es lo último que se leyó.</span>
          <button type="button" className="btn btn-sm btn-light" onClick={onRefresh}>Reintentar</button>
        </div>
      )}
      <section className="card" aria-labelledby={`${id}-title`}>
        <div className="card-body">
          <div className="d-flex flex-wrap align-items-center gap-2 mb-1">
            <h2 id={`${id}-title`} className="h5 mb-0">{globalPeriodLabel(invoice)}</h2>
            <span className={`ta-chip ${invoiceStatusChip(invoice.status)}`}>{invoiceStatusLabel(invoice.status)}</span>
          </div>
          <p className="small text-muted">
            Al público en general (XAXX010101000) · Meses {invoice.months} · Año {invoice.year}
          </p>

          {invoice.status === OrderInvoiceStatus.Stamping && (
            <p className="small text-muted">
              El PAC no respondió todavía. TienditApp lo vuelve a intentar con la misma solicitud: nunca timbra dos veces.
            </p>
          )}
          {invoice.status === OrderInvoiceStatus.Cancelling && (
            <p className="small text-muted">El SAT está procesando la cancelación; TienditApp la vuelve a consultar.</p>
          )}
          {invoice.status === OrderInvoiceStatus.Refused && (
            <p className="small text-muted">Sus ventas quedaron libres: emite otra vez la factura global del periodo.</p>
          )}
          {invoice.status === OrderInvoiceStatus.Cancelled && (
            <p className="small text-muted">
              Sus ventas quedaron libres: ya se pueden facturar solas o entrar en otra factura global del periodo.
            </p>
          )}
          {invoice.failureMessage && (
            <p className={`small ${invoice.status === OrderInvoiceStatus.Refused ? 'text-danger' : 'text-warning-emphasis'}`}>
              {invoice.failureMessage}
            </p>
          )}

          <dl className="row mb-3">
            <dt className="col-6 col-sm-4 fw-normal text-muted">Ventas</dt>
            <dd className="col-6 col-sm-8 mb-1">{invoice.orderCount === 1 ? '1 venta' : `${invoice.orderCount} ventas`}</dd>
            <dt className="col-6 col-sm-4 fw-normal text-muted">Subtotal</dt>
            <dd className="col-6 col-sm-8 mb-1 ta-money">{formatMoney(invoice.subTotal)}</dd>
            <dt className="col-6 col-sm-4 fw-normal text-muted">IVA</dt>
            <dd className="col-6 col-sm-8 mb-1 ta-money">{formatMoney(invoice.taxTotal)}</dd>
            <dt className="col-6 col-sm-4">Total</dt>
            <dd className="col-6 col-sm-8 mb-1 fw-semibold ta-money">{formatMoney(invoice.total)}</dd>
            <dt className="col-6 col-sm-4 fw-normal text-muted">Forma de pago</dt>
            <dd className="col-6 col-sm-8 mb-1">{paymentForm}</dd>
            {invoice.uuid && (
              <>
                <dt className="col-6 col-sm-4 fw-normal text-muted">Folio fiscal</dt>
                <dd className="col-6 col-sm-8 mb-1 ta-money text-break">{invoice.uuid}</dd>
              </>
            )}
            {invoice.stampedAt && (
              <>
                <dt className="col-6 col-sm-4 fw-normal text-muted">Timbrada</dt>
                <dd className="col-6 col-sm-8 mb-1">{formatDateTime(invoice.stampedAt)}</dd>
              </>
            )}
            {invoice.cancelledAt && (
              <>
                <dt className="col-6 col-sm-4 fw-normal text-muted">Cancelada</dt>
                <dd className="col-6 col-sm-8 mb-1">{formatDateTime(invoice.cancelledAt)}</dd>
              </>
            )}
          </dl>

          <div className="d-flex flex-wrap gap-2">
            {invoice.hasXml && (
              <>
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary"
                  disabled={downloading !== null}
                  aria-label={`Descargar XML de la factura global ${invoice.number}`}
                  onClick={() => void download('xml')}
                >
                  Descargar XML
                </button>
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary"
                  disabled={downloading !== null}
                  aria-label={`Descargar PDF de la factura global ${invoice.number}`}
                  onClick={() => void download('pdf')}
                >
                  Descargar PDF
                </button>
              </>
            )}
            {invoice.status === OrderInvoiceStatus.Stamping && (
              <button type="button" className="btn btn-sm btn-outline-secondary" disabled={retry.isPending} onClick={() => void askAgain()}>
                Volver a pedir el timbre
              </button>
            )}
            {invoice.status === OrderInvoiceStatus.Stamped && !hasLiveNotes && (
              <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => setCancelling(true)}>
                Cancelar factura global
              </button>
            )}
          </div>
          {invoice.status === OrderInvoiceStatus.Stamped && hasLiveNotes && (
            <p className="small text-muted mb-0 mt-2">
              Para cancelarla, {whatGoesFirst(stampedNotes, waitingNotes)}: el SAT no cancela una factura mientras tenga notas de
              crédito relacionadas.
            </p>
          )}
        </div>
      </section>

      <section className="card" aria-labelledby={`${id}-sales`}>
        <div className="card-body">
          <h2 id={`${id}-sales`} className="h5 mb-3">Ventas ({invoice.orders.length})</h2>
          <div className="table-responsive">
            <table className="table table-sm align-middle mb-0">
              <thead>
                <tr>
                  <th scope="col">Folio</th>
                  <th scope="col" className="text-end">Total</th>
                </tr>
              </thead>
              <tbody>
                {invoice.orders.map((order) => (
                  <tr key={order.orderId}>
                    <td>{order.orderNumber}</td>
                    <td className="text-end ta-money">{formatMoney(order.total)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </section>

      {creditNotes.length > 0 && (
        <section className="card" aria-labelledby={`${id}-notes`}>
          <div className="card-body">
            <h2 id={`${id}-notes`} className="h5 mb-1">Notas de crédito ({creditNotes.length})</h2>
            <p className="small text-muted">
              Las de los reembolsos de sus ventas, al público en general. Se emiten, se cancelan y se descargan en el pedido de
              cada venta.
            </p>
            <div className="table-responsive">
              <table className="table table-sm align-middle mb-0">
                <thead>
                  <tr>
                    <th scope="col">Folio</th>
                    <th scope="col">Venta</th>
                    <th scope="col" className={FROM_TABLET}>Estado</th>
                    <th scope="col" className="text-end">Total</th>
                  </tr>
                </thead>
                <tbody>
                  {creditNotes.map((note) => (
                    <tr key={note.id}>
                      <td>
                        <span className="text-nowrap">{note.number}</span>
                        <div className="d-sm-none mt-1">
                          <span className={`ta-chip ${invoiceStatusChip(note.status)}`}>{invoiceStatusLabel(note.status)}</span>
                        </div>
                      </td>
                      <td className="text-nowrap">
                        <Link to={`/orders?search=${encodeURIComponent(note.orderNumber)}`}>{note.orderNumber}</Link>
                      </td>
                      <td className={FROM_TABLET}>
                        <span className={`ta-chip ${invoiceStatusChip(note.status)}`}>{invoiceStatusLabel(note.status)}</span>
                      </td>
                      <td className="text-end ta-money">{formatMoney(note.total)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </section>
      )}

      {cancelling && <CancelGlobalInvoiceModal invoice={invoice} onClose={() => setCancelling(false)} />}
    </>
  );
}

/** What has to happen to the live credit notes of a global invoice before it is cancelled. */
function whatGoesFirst(stamped: string[], waiting: string[]): string {
  const cancel = `cancela primero sus notas de crédito vigentes (${stamped.join(', ')}) en el pedido de cada venta`;
  if (waiting.length === 0) return cancel;
  if (stamped.length === 0) {
    return `espera a que sus notas de crédito (${waiting.join(', ')}) terminen de timbrarse o de cancelarse, y cancela en su `
      + 'pedido las que queden vigentes';
  }
  return `${cancel}, y espera a que las demás (${waiting.join(', ')}) terminen de timbrarse o de cancelarse`;
}

/** Owners and admins ask the SAT to cancel a stamped global invoice, with its reason; nobody has to accept it. */
function CancelGlobalInvoiceModal({ invoice, onClose }: { invoice: GlobalInvoice; onClose: () => void }) {
  const titleId = useId();
  const toast = useToast();
  const cancel = useCancelGlobalInvoice();
  const [motive, setMotive] = useState<CfdiCancellationMotive>(CfdiCancellationMotive.NominativeInGlobalInvoice);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      const result = await cancel.mutateAsync({ invoiceId: invoice.id, motive });
      toast.success(result.status === OrderInvoiceStatus.Cancelled
        ? `Factura global ${result.number} cancelada.`
        : `Cancelación de ${result.number} pedida: el SAT la está procesando.`);
      onClose();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cancelar la factura global.'));
    }
  };

  return (
    <Modal show onHide={onClose} centered aria-labelledby={titleId}>
      <form onSubmit={submit}>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">Cancelar la factura global {invoice.number}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="small">
            TienditApp le pide al SAT que la cancele; va al público en general, así que nadie tiene que aceptarla. Al cancelarse,
            sus ventas quedan libres.
          </p>
          <p className="small">
            Si un cliente pidió su factura: cancela con 04, factura esa venta en Pedidos y emite otra vez la factura global del
            periodo, que ya no la incluirá.
          </p>
          <fieldset>
            <legend className="form-label small mb-1">Motivo</legend>
            {GLOBAL_CANCELLATION_MOTIVES.map(([value, label]) => (
              <div className="form-check" key={value}>
                <input
                  id={`global-cancel-motive-${value}`}
                  type="radio"
                  name="global-cancel-motive"
                  className="form-check-input"
                  checked={motive === value}
                  onChange={() => setMotive(value)}
                />
                <label className="form-check-label" htmlFor={`global-cancel-motive-${value}`}>{label}</label>
              </div>
            ))}
          </fieldset>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose}>Volver</button>
          <button type="submit" className="btn btn-danger" disabled={cancel.isPending}>
            {cancel.isPending ? 'Cancelando…' : 'Cancelar factura global'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
