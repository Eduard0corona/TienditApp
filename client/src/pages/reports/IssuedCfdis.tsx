import { useState } from 'react';
import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../../api/errors';
import {
  downloadIssuedCfdiFile,
  downloadIssuedCfdis,
  useIssuedCfdiSummary,
  useIssuedCfdis,
  type IssuedCfdi,
  type IssuedCfdiSummary,
} from '../../api/invoicing';
import DataTable, { DataTableFilter, type DataTableColumn } from '../../components/common/DataTable';
import PageTitle from '../../components/common/PageTitle';
import { globalPeriodLabel } from '../../components/invoicing/globalPeriod';
import { useToast } from '../../hooks/useToast';
import { IssuedCfdiExportFormat, IssuedCfdiKind, OrderInvoiceStatus } from '../../types';
import { complementStatusLabel, invoiceStatusChip, invoiceStatusLabel, saveBlob } from '../../utils/cfdi';
import { formatDate, getBusinessToday } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

const COLUMNS: DataTableColumn[] = [
  { header: 'CFDI' },
  { header: 'Fecha' },
  { header: 'Receptor' },
  { header: 'Relación' },
  { header: 'Total', className: 'text-end' },
  { header: 'Acciones', className: 'text-end' },
];

// The API takes up to 100 a page and answers 20 by default, and a search of up to 100 characters.
const PAGE_SIZES = [20, 50, 100];
const MAX_SEARCH_LENGTH = 100;

// CFDI 4.0 is from 2022: nothing is issued through TienditApp before (as the API).
const FIRST_MONTH = { year: 2022, month: 1 };

const KIND_LABELS: Record<IssuedCfdiKind, string> = {
  [IssuedCfdiKind.OrderInvoice]: 'Factura',
  [IssuedCfdiKind.GlobalInvoice]: 'Factura global',
  [IssuedCfdiKind.PaymentComplement]: 'Complemento de pago',
  [IssuedCfdiKind.CreditNote]: 'Nota de crédito',
};

const KIND_PLURALS: Record<IssuedCfdiKind, string> = {
  [IssuedCfdiKind.OrderInvoice]: 'Facturas',
  [IssuedCfdiKind.GlobalInvoice]: 'Facturas globales',
  [IssuedCfdiKind.PaymentComplement]: 'Complementos de pago',
  [IssuedCfdiKind.CreditNote]: 'Notas de crédito',
};

// The states as the chips say them, in the order a CFDI goes through them.
const STATUS_FILTERS: Array<[OrderInvoiceStatus, string]> = [
  [OrderInvoiceStatus.Stamping, 'Timbrando'],
  [OrderInvoiceStatus.Stamped, 'Timbradas'],
  [OrderInvoiceStatus.Cancelling, 'Cancelándose'],
  [OrderInvoiceStatus.Cancelled, 'Canceladas'],
  [OrderInvoiceStatus.Refused, 'Rechazadas'],
];

interface Month {
  year: number;
  month: number;
}

function currentMonth(): Month {
  const today = getBusinessToday();
  return { year: Number(today.slice(0, 4)), month: Number(today.slice(5, 7)) };
}

function shift({ year, month }: Month, by: number): Month {
  const index = year * 12 + (month - 1) + by;
  return { year: Math.floor(index / 12), month: (index % 12) + 1 };
}

function isBefore(a: Month, b: Month): boolean {
  return a.year * 12 + a.month < b.year * 12 + b.month;
}

/** «Octubre de 2026». */
function monthLabel({ year, month }: Month): string {
  const label = new Intl.DateTimeFormat('es-MX', { month: 'long', year: 'numeric', timeZone: 'UTC' })
    .format(Date.UTC(year, month - 1, 1));
  return label.charAt(0).toUpperCase() + label.slice(1);
}

/** «de la factura F-1», «del complemento de pago P-1»: what a button of the row acts on. */
function ofDocument(cfdi: IssuedCfdi): string {
  const article = cfdi.kind === IssuedCfdiKind.PaymentComplement ? 'del' : 'de la';
  return `${article} ${KIND_LABELS[cfdi.kind].toLowerCase()} ${cfdi.number}`;
}

function statusLabel(cfdi: IssuedCfdi): string {
  // «Complemento» asks for its states in masculine.
  return cfdi.kind === IssuedCfdiKind.PaymentComplement ? complementStatusLabel(cfdi.status) : invoiceStatusLabel(cfdi.status);
}

function count(n: number, one: string, many: string): string {
  return `${n} ${n === 1 ? one : many}`;
}

/**
 * Reportes › CFDI emitidos: what the business stamped in a month, in its time zone (invoices of orders, global invoices,
 * payment complements and credit notes), what they add up to, and the download for the accountant: a ZIP with their XML
 * and the list as CSV. Owners and admins only, as the API.
 */
export default function IssuedCfdis() {
  const toast = useToast();
  const today = currentMonth();
  const [period, setPeriod] = useState<Month>(today);
  const [kind, setKind] = useState<IssuedCfdiKind | undefined>();
  const [status, setStatus] = useState<OrderInvoiceStatus | undefined>();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(PAGE_SIZES[0]);
  const [exporting, setExporting] = useState<IssuedCfdiExportFormat | null>(null);
  const [downloading, setDownloading] = useState<string | null>(null);
  const summary = useIssuedCfdiSummary(period.year, period.month);
  const cfdis = useIssuedCfdis(period.year, period.month, { kind, status, search }, page, pageSize);

  const previous = shift(period, -1);
  const next = shift(period, 1);
  const goTo = (month: Month) => {
    setPeriod(month);
    setPage(1);
  };

  const exportMonth = async (format: IssuedCfdiExportFormat) => {
    setExporting(format);
    try {
      const file = await downloadIssuedCfdis(period.year, period.month, format);
      saveBlob(file.blob, file.fileName);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, format === IssuedCfdiExportFormat.Csv ? 'No se pudo descargar la lista.' : 'No se pudieron descargar los XML.'));
    } finally {
      setExporting(null);
    }
  };

  const download = async (cfdi: IssuedCfdi, file: 'xml' | 'pdf') => {
    setDownloading(`${cfdi.id}-${file}`);
    try {
      const downloaded = await downloadIssuedCfdiFile(cfdi, file);
      saveBlob(downloaded.blob, downloaded.fileName);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, file === 'pdf' ? 'No se pudo descargar el PDF.' : 'No se pudo descargar el XML.'));
    } finally {
      setDownloading(null);
    }
  };

  return (
    <>
      <PageTitle title="CFDI emitidos" />
      <p className="text-muted">
        Las facturas, facturas globales, complementos de pago y notas de crédito que el negocio timbró en el mes, por la fecha
        del CFDI. Descarga sus XML para tu contador.
      </p>

      <div className="d-flex flex-wrap align-items-center gap-2 mb-3">
        <button
          type="button"
          className="btn btn-light btn-sm"
          disabled={isBefore(previous, FIRST_MONTH)}
          aria-label={`Mes anterior (${monthLabel(previous).toLowerCase()})`}
          onClick={() => goTo(previous)}
        >
          <i className="ri-arrow-left-s-line" aria-hidden="true"></i>Mes anterior
        </button>
        <h2 className="h5 mb-0 px-1" aria-live="polite">{monthLabel(period)}</h2>
        <button
          type="button"
          className="btn btn-light btn-sm"
          disabled={!isBefore(period, today)}
          aria-label={`Mes siguiente (${monthLabel(next).toLowerCase()})`}
          onClick={() => goTo(next)}
        >
          Mes siguiente<i className="ri-arrow-right-s-line" aria-hidden="true"></i>
        </button>
        <div className="d-flex flex-wrap gap-2 ms-md-auto">
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={exporting !== null}
            onClick={() => void exportMonth(IssuedCfdiExportFormat.Zip)}
          >
            <i className="ri-file-zip-line me-1" aria-hidden="true"></i>
            {exporting === IssuedCfdiExportFormat.Zip ? 'Descargando…' : 'Descargar XML'}
          </button>
          <button
            type="button"
            className="btn btn-light btn-sm"
            disabled={exporting !== null}
            onClick={() => void exportMonth(IssuedCfdiExportFormat.Csv)}
          >
            <i className="ri-file-list-3-line me-1" aria-hidden="true"></i>
            {exporting === IssuedCfdiExportFormat.Csv ? 'Descargando…' : 'Descargar lista'}
          </button>
        </div>
      </div>
      <p className="small text-muted">
        El ZIP lleva el XML de cada CFDI timbrado del mes, también los cancelados, y la lista de todos en CSV. Los PDF se
        descargan uno por uno.
      </p>

      <MonthSummary summary={summary.data} loading={summary.isPending} error={summary.isError ? summary.error : null}
        onRetry={() => void summary.refetch()} />

      <section className="card" aria-label={`CFDI de ${monthLabel(period).toLowerCase()}`}>
        <div className="card-body">
          {cfdis.isError && (
            <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
              <span className="me-auto">{getApiErrorMessage(cfdis.error, 'No se pudieron cargar los CFDI.')}</span>
              <button type="button" className="btn btn-sm btn-light" onClick={() => void cfdis.refetch()}>Reintentar</button>
            </div>
          )}
          <DataTable<IssuedCfdi>
            data={cfdis.data?.items ?? []}
            columns={COLUMNS}
            keyExtractor={(cfdi) => cfdi.id}
            loading={cfdis.isPending}
            emptyMessage={cfdis.isError ? 'Sin CFDI que mostrar.' : 'No hay CFDI en este mes.'}
            searchPlaceholder="Buscar folio, UUID, RFC, nombre o pedido"
            searchMaxLength={MAX_SEARCH_LENGTH}
            pageSizeOptions={PAGE_SIZES}
            filters={(
              <>
                <DataTableFilter label="Tipo" htmlFor="issued-cfdis-kind">
                  <select
                    id="issued-cfdis-kind"
                    className="form-select form-select-sm"
                    value={kind ?? ''}
                    onChange={(e) => {
                      setKind(e.target.value === '' ? undefined : Number(e.target.value) as IssuedCfdiKind);
                      setPage(1);
                    }}
                  >
                    <option value="">Todos</option>
                    {Object.values(IssuedCfdiKind).map((value) => (
                      <option key={value} value={value}>{KIND_PLURALS[value]}</option>
                    ))}
                  </select>
                </DataTableFilter>
                <DataTableFilter label="Estado" htmlFor="issued-cfdis-status">
                  <select
                    id="issued-cfdis-status"
                    className="form-select form-select-sm"
                    value={status ?? ''}
                    onChange={(e) => {
                      setStatus(e.target.value === '' ? undefined : Number(e.target.value) as OrderInvoiceStatus);
                      setPage(1);
                    }}
                  >
                    <option value="">Todos</option>
                    {STATUS_FILTERS.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                  </select>
                </DataTableFilter>
              </>
            )}
            serverSide={{
              totalCount: cfdis.data?.totalCount ?? 0,
              page,
              pageSize,
              onPageChange: setPage,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPage(1);
              },
              onSearchChange: (value) => {
                setSearch(value.trim());
                setPage(1);
              },
            }}
            renderRow={(cfdi) => {
              const document = ofDocument(cfdi);
              return (
                <>
                  <td>
                    <div className="fw-semibold">{cfdi.number}</div>
                    <div className="small text-muted">{KIND_LABELS[cfdi.kind]}</div>
                    <span className={`ta-chip ${invoiceStatusChip(cfdi.status)}`}>{statusLabel(cfdi)}</span>
                    {cfdi.status === OrderInvoiceStatus.Refused && cfdi.failureMessage && (
                      <div className="small text-muted">{cfdi.failureMessage}</div>
                    )}
                  </td>
                  <td className="text-nowrap">{formatDate(cfdi.issuedAt)}</td>
                  <td>
                    {cfdi.receiverName}
                    <div className="small text-muted">{cfdi.receiverRfc}</div>
                  </td>
                  <td><Relation cfdi={cfdi} /></td>
                  <td className="text-end">
                    {cfdi.kind === IssuedCfdiKind.PaymentComplement
                      ? <>Pagado <span className="ta-money">{formatMoney(cfdi.paidAmount ?? 0)}</span></>
                      : <span className="ta-money">{formatMoney(cfdi.total ?? 0)}</span>}
                  </td>
                  <td className="text-end text-nowrap">
                    {cfdi.hasXml && (
                      <>
                        <button
                          type="button"
                          className="btn btn-light btn-sm me-1"
                          disabled={downloading !== null}
                          aria-label={`XML ${document}`}
                          onClick={() => void download(cfdi, 'xml')}
                        >
                          XML
                        </button>
                        <button
                          type="button"
                          className="btn btn-light btn-sm"
                          disabled={downloading !== null}
                          aria-label={`PDF ${document}`}
                          onClick={() => void download(cfdi, 'pdf')}
                        >
                          PDF
                        </button>
                      </>
                    )}
                  </td>
                </>
              );
            }}
          />
        </div>
      </section>
    </>
  );
}

/** Where a CFDI comes from: its order, the invoice a complement or a credit note is of, or a global invoice's period. */
function Relation({ cfdi }: { cfdi: IssuedCfdi }) {
  if (cfdi.kind === IssuedCfdiKind.GlobalInvoice && cfdi.periodicity != null && cfdi.periodFrom && cfdi.periodTo) {
    const period = globalPeriodLabel({ periodicity: cfdi.periodicity, periodFrom: cfdi.periodFrom, periodTo: cfdi.periodTo });
    return (
      <>
        <Link to={`/reports/global-invoices/${cfdi.id}`} aria-label={`${period}: ver la factura global ${cfdi.number}`}>{period}</Link>
        {cfdi.orderCount != null && (
          <div className="small text-muted">{count(cfdi.orderCount, 'venta', 'ventas')}</div>
        )}
      </>
    );
  }

  let relation: string | null = null;
  if (cfdi.kind === IssuedCfdiKind.PaymentComplement && cfdi.relatedNumber) relation = `Pago de la ${cfdi.relatedNumber}`;
  else if (cfdi.kind === IssuedCfdiKind.CreditNote && cfdi.relatedNumber) relation = `De la ${cfdi.relatedNumber}`;
  else if (cfdi.relatedNumber) relation = `Sustituye a la ${cfdi.relatedNumber}`;
  return (
    <>
      {cfdi.orderNumber && (
        <Link to={`/orders?search=${encodeURIComponent(cfdi.orderNumber)}`}>{cfdi.orderNumber}</Link>
      )}
      {relation && <div className="small text-muted">{relation}</div>}
      {cfdi.replacedByNumber && <div className="small text-muted">Sustituida por la {cfdi.replacedByNumber}</div>}
    </>
  );
}

/** What the month's CFDI in force add up to by kind, and how many were cancelled, wait for something or were refused. */
function MonthSummary({ summary, loading, error, onRetry }: {
  summary: IssuedCfdiSummary | undefined;
  loading: boolean;
  error: unknown;
  onRetry: () => void;
}) {
  if (error) {
    return (
      <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
        <span className="me-auto">{getApiErrorMessage(error, 'No se pudo cargar el resumen del mes.')}</span>
        <button type="button" className="btn btn-sm btn-light" onClick={onRetry}>Reintentar</button>
      </div>
    );
  }
  if (loading || !summary) return <p className="text-muted" aria-busy="true">Cargando…</p>;

  const kinds = summary.kinds;
  const total = (pick: 'cancelled' | 'cancelling' | 'stamping' | 'refused') => kinds.reduce((sum, k) => sum + k[pick], 0);
  const notes = [
    total('cancelled') > 0 && count(total('cancelled'), 'cancelado', 'cancelados'),
    total('cancelling') > 0 && `${count(total('cancelling'), 'espera', 'esperan')} que el cliente acepte su cancelación en el SAT`,
    total('stamping') > 0 && `${count(total('stamping'), 'se está', 'se están')} timbrando`,
    total('refused') > 0 && `${count(total('refused'), 'rechazado', 'rechazados')} por el PAC: no son CFDI y explican los folios que faltan`,
  ].filter(Boolean);

  return (
    <section className="card mb-3" aria-label="Resumen del mes">
      <div className="card-body">
        <h2 className="h6">Vigentes del mes</h2>
        <div className="table-responsive">
          <table className="table table-sm align-middle mb-2">
            <thead>
              <tr>
                <th scope="col">Documento</th>
                <th scope="col" className="text-end">Vigentes</th>
                <th scope="col" className="text-end">Subtotal</th>
                <th scope="col" className="text-end">Impuestos</th>
                <th scope="col" className="text-end">Total</th>
              </tr>
            </thead>
            <tbody>
              {kinds.map((k) => (
                <tr key={k.kind}>
                  <th scope="row" className="fw-normal">{KIND_PLURALS[k.kind]}</th>
                  <td className="text-end">{k.inForce}</td>
                  {k.kind === IssuedCfdiKind.PaymentComplement ? (
                    <>
                      <td className="text-end text-muted">—</td>
                      <td className="text-end text-muted">—</td>
                      <td className="text-end">Pagado <span className="ta-money">{formatMoney(k.total)}</span></td>
                    </>
                  ) : (
                    <>
                      <td className="text-end ta-money">{formatMoney(k.subTotal)}</td>
                      <td className="text-end ta-money">{formatMoney(k.taxTotal)}</td>
                      <td className="text-end ta-money">{formatMoney(k.total)}</td>
                    </>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="small text-muted mb-0">
          Vigentes son los timbrados y los que esperan que el cliente acepte su cancelación.
          {notes.length > 0 && ` Además: ${notes.join('; ')}.`}
        </p>
      </div>
    </section>
  );
}
