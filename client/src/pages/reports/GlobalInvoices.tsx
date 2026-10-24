import { useState } from 'react';
import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../../api/errors';
import { useGlobalInvoices, type GlobalInvoice } from '../../api/invoicing';
import DataTable, { type DataTableColumn } from '../../components/common/DataTable';
import PageTitle from '../../components/common/PageTitle';
import { globalPeriodLabel } from '../../components/invoicing/globalPeriod';
import { invoiceStatusChip, invoiceStatusLabel } from '../../utils/cfdi';
import { formatMoney } from '../../utils/money';

// The state goes under the folio: on a phone the first columns are what shows, and a cancelled global invoice must not
// look like the one issued again for the same period.
const COLUMNS: DataTableColumn[] = [
  { header: 'Factura' },
  { header: 'Periodo' },
  { header: 'Total', className: 'text-end' },
  { header: 'Acciones', className: 'text-end' },
];

// The API takes up to 100 a page and answers 20 by default, and a search of up to 100 characters.
const PAGE_SIZES = [20, 50, 100];
const MAX_SEARCH_LENGTH = 100;

/**
 * Reportes › Factura global: the global invoices of the business to the general public, the newest period first, and the
 * way to issue the one of a period that is over. Owners and admins only, as the API.
 */
export default function GlobalInvoices() {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(PAGE_SIZES[0]);
  const [search, setSearch] = useState('');
  const invoices = useGlobalInvoices(page, pageSize, search);

  return (
    <>
      <PageTitle title="Factura global" />
      <p className="text-muted">
        Una factura al público en general por las ventas de un periodo que ya terminó y que no tienen factura propia. El SAT
        pide emitirla dentro de las 24 horas siguientes al cierre del periodo.
      </p>

      <section className="card" aria-label="Facturas globales">
        <div className="card-body">
          {invoices.isError && (
            <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
              <span className="me-auto">{getApiErrorMessage(invoices.error, 'No se pudieron cargar las facturas globales.')}</span>
              <button type="button" className="btn btn-sm btn-light" onClick={() => void invoices.refetch()}>Reintentar</button>
            </div>
          )}
          <DataTable<GlobalInvoice>
            data={invoices.data?.items ?? []}
            columns={COLUMNS}
            keyExtractor={(invoice) => invoice.id}
            loading={invoices.isPending}
            // With a search, the table says it found nothing.
            emptyMessage={invoices.isError ? 'Sin facturas globales que mostrar.' : 'Todavía no emites facturas globales.'}
            searchPlaceholder="Buscar folio, UUID o folio de una venta"
            searchMaxLength={MAX_SEARCH_LENGTH}
            pageSizeOptions={PAGE_SIZES}
            toolbarEnd={<Link className="btn btn-primary btn-sm" to="/reports/global-invoices/new">Nueva factura global</Link>}
            serverSide={{
              totalCount: invoices.data?.totalCount ?? 0,
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
            renderRow={(invoice) => {
              const period = globalPeriodLabel(invoice);
              const state = invoiceStatusLabel(invoice.status);
              return (
                <>
                  <td>
                    <div className="fw-semibold">{invoice.number}</div>
                    <span className={`ta-chip ${invoiceStatusChip(invoice.status)}`}>{state}</span>
                  </td>
                  <td>
                    {period}
                    <div className="small text-muted">{invoice.orderCount === 1 ? '1 venta' : `${invoice.orderCount} ventas`}</div>
                  </td>
                  <td className="text-end ta-money">{formatMoney(invoice.total)}</td>
                  <td className="text-end">
                    {/* The state in the name: a cancelled global invoice and the one issued again share the period. */}
                    <Link
                      className="btn btn-light btn-sm"
                      to={`/reports/global-invoices/${invoice.id}`}
                      aria-label={`Ver la factura global ${invoice.number} ${state.toLowerCase()} (${period})`}
                    >
                      Ver
                    </Link>
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
