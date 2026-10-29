import { Fragment, useId, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { getApiErrorMessage } from '../../api/errors';
import { useGlobalInvoiceDraft, useIssueGlobalInvoice, type GlobalInvoiceDraft } from '../../api/invoicing';
import PageTitle from '../../components/common/PageTitle';
import { globalPeriodLabel } from '../../components/invoicing/globalPeriod';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { GlobalInvoicePeriodicity, OrderInvoiceStatus } from '../../types';
import { GLOBAL_PERIODICITIES, PAYMENT_FORMS } from '../../utils/cfdi';
import { addCalendarDays, formatDateTime, getBusinessToday } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

const PARENT = { label: 'Factura global', to: '/reports/global-invoices' };

// What the API takes as a day (GlobalInvoicing.IsReasonableDay, years 2022 to 9998): anything else is not asked for, as
// the year being typed in the date field (0002, 0020, 0202…).
const DAY_PATTERN = /^(\d{4})-\d{2}-\d{2}$/;

function isReasonableDay(day: string): boolean {
  const year = Number(DAY_PATTERN.exec(day)?.[1]);
  return year >= 2022 && year <= 9998;
}

function saleCount(count: number): string {
  return count === 1 ? '1 venta' : `${count} ventas`;
}

/**
 * Reportes › Factura global › Nueva: the global invoice of the period of a periodicity that holds a day, as it would be
 * stamped (its sales, the ones left out and why, its amounts and what stops it), and stamping it. What is stamped is what
 * the person reviewed: the API refuses it if the sales of the period changed meanwhile.
 */
export default function NewGlobalInvoice() {
  const id = useId();
  const navigate = useNavigate();
  const toast = useToast();
  const { confirm } = useConfirm();
  // Yesterday: the usual global invoice is the one of the day that just ended.
  const [periodicity, setPeriodicity] = useState<GlobalInvoicePeriodicity>(GlobalInvoicePeriodicity.Daily);
  const yesterday = addCalendarDays(getBusinessToday(), -1);
  const [day, setDay] = useState(yesterday);
  const validDay = isReasonableDay(day);
  const draft = useGlobalInvoiceDraft(periodicity, day, validDay);
  const issue = useIssueGlobalInvoice();

  const stamp = async (preview: GlobalInvoiceDraft) => {
    const period = globalPeriodLabel(preview);
    const accepted = await confirm({
      title: 'Timbrar la factura global',
      message: `Se timbra la factura global de ${period.toLowerCase()} al público en general: ${saleCount(preview.saleCount)} por ${formatMoney(preview.total)}. Esas ventas ya no se facturan solas mientras esté vigente.`,
      confirmLabel: 'Timbrar factura global',
    });
    if (!accepted) return;
    try {
      const result = await issue.mutateAsync({
        periodicity: preview.periodicity,
        day,
        expectedSaleCount: preview.saleCount,
        expectedTotal: preview.total,
        expectedFingerprint: preview.fingerprint,
      });
      if (result.status === OrderInvoiceStatus.Stamped) toast.success(`Factura global ${result.number} timbrada.`);
      else if (result.status === OrderInvoiceStatus.Refused) toast.error(result.failureMessage ?? 'El PAC rechazó la factura global.');
      else toast.info('El PAC todavía no responde: TienditApp lo vuelve a intentar cada pocos minutos.');
      navigate(`/reports/global-invoices/${result.id}`);
    } catch (err: unknown) {
      // The sales changed, or the period cannot be stamped: the preview is read again (the change does it) and says why.
      toast.error(getApiErrorMessage(err, 'No se pudo timbrar la factura global.'));
    }
  };

  return (
    <>
      <PageTitle title="Nueva factura global" parent={PARENT} />
      <section className="card" aria-labelledby={`${id}-period`}>
        <div className="card-body">
          <h2 id={`${id}-period`} className="h5 mb-3">Periodo</h2>
          <div className="row g-3">
            <div className="col-sm-6 col-lg-4">
              <label className="form-label" htmlFor={`${id}-periodicity`}>Periodicidad</label>
              <select
                id={`${id}-periodicity`}
                className="form-select"
                value={periodicity}
                onChange={(event) => setPeriodicity(Number(event.target.value) as GlobalInvoicePeriodicity)}
              >
                {GLOBAL_PERIODICITIES.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
              </select>
            </div>
            <div className="col-sm-6 col-lg-4">
              <label className="form-label" htmlFor={`${id}-day`}>Un día del periodo</label>
              <input
                id={`${id}-day`}
                type="date"
                className="form-control"
                value={day}
                // No period that holds today has ended yet.
                max={yesterday}
                onChange={(event) => setDay(event.target.value)}
              />
            </div>
          </div>
          <p className="small text-muted mt-2 mb-0">
            Entran las ventas del periodo que ya no deben nada (pagadas, o con lo que faltaba condonado) y no tienen factura
            propia. Las semanas y quincenas se cortan al terminar el mes.
          </p>
        </div>
      </section>

      {!validDay && <div className="alert alert-info">Elige un día del periodo, de 2022 en adelante.</div>}
      {validDay && draft.isPending && <p className="text-muted" aria-busy="true">Calculando la factura global…</p>}
      {validDay && draft.isError && (
        <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
          <span className="me-auto">{getApiErrorMessage(draft.error, 'No se pudo calcular la factura global.')}</span>
          <button type="button" className="btn btn-sm btn-light" onClick={() => void draft.refetch()}>Reintentar</button>
        </div>
      )}
      {validDay && draft.data && (
        <DraftPreview
          draft={draft.data}
          stamping={issue.isPending}
          // A preview being read again, or that could not be read again, is not the one the person reviewed.
          stale={draft.isFetching || draft.isError}
          onStamp={() => void stamp(draft.data)}
        />
      )}
    </>
  );
}

interface DraftPreviewProps {
  draft: GlobalInvoiceDraft;
  stamping: boolean;
  stale: boolean;
  onStamp: () => void;
}

function DraftPreview({ draft, stamping, stale, onStamp }: DraftPreviewProps) {
  const id = useId();
  const period = globalPeriodLabel(draft);
  const paymentForm = draft.paymentForm ? `${draft.paymentForm} · ${PAYMENT_FORMS[draft.paymentForm] ?? 'Otra'}` : null;

  return (
    <section className="card" aria-labelledby={`${id}-title`}>
      <div className="card-body">
        <h2 id={`${id}-title`} className="h5 mb-1">{period}</h2>
        <p className="small text-muted">
          Al público en general (XAXX010101000) · Meses {draft.months} · Año {draft.year}
        </p>

        {draft.sandbox && (
          <div className="alert alert-warning py-2 small">Modo de pruebas: las facturas que timbres no tienen validez fiscal.</div>
        )}
        {draft.problem && (
          <div className="alert alert-info py-2 small" role="status">
            {draft.problem} <Link to="/settings/invoicing">Ir a Configuración › Facturación</Link>
          </div>
        )}
        {draft.blockers.length > 0 && (
          <div className="alert alert-danger">
            <p className="mb-1 fw-semibold">Todavía no se puede timbrar:</p>
            <ul className="mb-0">{draft.blockers.map((item) => <li key={item}>{item}</li>)}</ul>
          </div>
        )}
        {draft.warnings.length > 0 && (
          <div className="alert alert-warning">
            <ul className="mb-0">{draft.warnings.map((item) => <li key={item}>{item}</li>)}</ul>
          </div>
        )}

        {draft.saleCount > 0 && (
          <dl className="row mb-3">
            <dt className="col-6 col-sm-4 fw-normal text-muted">Ventas</dt>
            <dd className="col-6 col-sm-8 mb-1">{saleCount(draft.saleCount)}</dd>
            <dt className="col-6 col-sm-4 fw-normal text-muted">Subtotal</dt>
            <dd className="col-6 col-sm-8 mb-1 ta-money">{formatMoney(draft.subTotal)}</dd>
            {draft.transfers.filter((transfer) => transfer.factorType !== 'Exento').map((transfer) => (
              <Fragment key={`${transfer.factorType}-${transfer.rateOrQuota}`}>
                <dt className="col-6 col-sm-4 fw-normal text-muted">
                  IVA {((transfer.rateOrQuota ?? 0) * 100).toFixed(0)} % sobre {formatMoney(transfer.base)}
                </dt>
                <dd className="col-6 col-sm-8 mb-1 ta-money">{formatMoney(transfer.amount ?? 0)}</dd>
              </Fragment>
            ))}
            <dt className="col-6 col-sm-4">Total</dt>
            <dd className="col-6 col-sm-8 mb-1 fw-semibold ta-money">{formatMoney(draft.total)}</dd>
            {paymentForm && (
              <>
                <dt className="col-6 col-sm-4 fw-normal text-muted">Forma de pago</dt>
                <dd className="col-6 col-sm-8 mb-1">{paymentForm} <span className="small text-muted">(la de la venta más grande)</span></dd>
              </>
            )}
          </dl>
        )}

        <button
          type="button"
          className="btn btn-primary mb-3"
          disabled={!draft.isReadyToStamp || stamping || stale}
          onClick={onStamp}
        >
          {stamping ? 'Timbrando…' : 'Timbrar factura global'}
        </button>

        {draft.sales.length > 0 && (
          <>
            <h3 className="h6">Ventas que entran ({draft.sales.length})</h3>
            <div className="table-responsive mb-3">
              <table className="table table-sm align-middle mb-0">
                <thead>
                  <tr>
                    <th scope="col">Folio</th>
                    <th scope="col">Pagada</th>
                    <th scope="col" className="text-end">Total</th>
                  </tr>
                </thead>
                <tbody>
                  {draft.sales.map((sale) => (
                    <tr key={sale.orderId}>
                      <td>{sale.orderNumber}</td>
                      <td className="small text-muted">{formatDateTime(sale.paidAt)}</td>
                      <td className="text-end ta-money">{formatMoney(sale.total)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}

        {draft.leftOut.length > 0 && (
          <>
            <h3 className="h6">No entran ({draft.leftOut.length})</h3>
            <ul className="list-unstyled small mb-0">
              {draft.leftOut.map((sale) => (
                <li key={sale.orderId} className="border-bottom py-1">
                  <span className="fw-semibold">{sale.orderNumber}</span> · <span className="ta-money">{formatMoney(sale.total)}</span>
                  <div className="text-muted">{sale.reason}</div>
                </li>
              ))}
            </ul>
          </>
        )}
      </div>
    </section>
  );
}
