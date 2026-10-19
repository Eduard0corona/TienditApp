import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import CustomerAccountModal from '../../components/customers/CustomerAccountModal';
import { daysSince, describeDays } from '../../components/customers/customerAccount';
import { type AccountOf, type Receivable, useReceivables } from '../../api/customerCredit';
import { ReceivablesSort } from '../../api/generated/enums';
import { getApiErrorMessage } from '../../api/errors';
import { formatDate } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

const COLUMNS = [
  { header: 'Cliente' },
  { header: 'Debe', className: 'text-end' },
  { header: 'Pedidos', className: 'text-center' },
  { header: 'Desde' },
  { header: 'Límite', className: 'text-end' },
  { header: 'Acciones', className: 'text-center' },
];

/**
 * Clientes › Por cobrar (fiado, Fase 26A): who owes the business, a row per customer by phone, with the total. Each row
 * opens the customer's account, where whoever charges takes a payment. The customer card links here with ?cuenta=.
 */
export default function ReceivablesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState<ReceivablesSort>(ReceivablesSort.Owed);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const linkedPhone = searchParams.get('cuenta')?.trim() || null;
  const [account, setAccount] = useState<(AccountOf & { name?: string }) | null>(linkedPhone ? { phone: linkedPhone } : null);
  const { data, error, isPending } = useReceivables({ search, sort, page, pageSize });

  const closeAccount = () => {
    setAccount(null);
    // The account the card linked to closes for good: going back does not open it again.
    if (linkedPhone) setSearchParams({}, { replace: true });
  };

  return (
    <>
      <PageTitle title="Por cobrar" />

      <div className="card">
        <div className="card-body">
          {error && <div className="alert alert-danger">{getApiErrorMessage(error, 'No se pudo cargar lo que te deben.')}</div>}
          <p className="mb-3">
            Te deben <strong className="ta-money fs-5">{formatMoney(data?.totalOwed ?? 0)}</strong>
            {data && data.page.totalCount > 0 && (
              <span className="text-muted"> · {data.page.totalCount === 1 ? '1 cliente' : `${data.page.totalCount} clientes`}</span>
            )}
          </p>

          <DataTable<Receivable>
            data={data?.page.items ?? []}
            columns={COLUMNS}
            keyExtractor={(row) => row.phone}
            loading={isPending}
            emptyMessage={search ? 'Nadie que coincida te debe.' : 'Nadie te debe: los pedidos entregados están pagados.'}
            searchPlaceholder="Nombre o teléfono"
            searchMaxLength={100}
            filters={(
              <DataTableFilter label="Ordenar" htmlFor="receivables-sort">
                <select
                  id="receivables-sort"
                  className="form-select form-select-sm"
                  value={sort}
                  onChange={(event) => {
                    setSort(Number(event.target.value) as ReceivablesSort);
                    setPage(1);
                  }}
                >
                  <option value={ReceivablesSort.Owed}>Debe más</option>
                  <option value={ReceivablesSort.Oldest}>Debe desde hace más</option>
                </select>
              </DataTableFilter>
            )}
            serverSide={{
              totalCount: data?.page.totalCount ?? 0,
              page,
              pageSize,
              onPageChange: setPage,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPage(1);
              },
              onSearchChange: (term) => {
                setSearch(term);
                setPage(1);
              },
            }}
            renderRow={(row) => (
              <>
                <td>
                  <div className="fw-semibold">{row.name}</div>
                  <small className="text-muted">{row.phone}</small>
                </td>
                <td className="text-end fw-semibold ta-money text-danger">{formatMoney(row.owed)}</td>
                <td className="text-center">{row.orders}</td>
                <td>
                  <div>{formatDate(row.since)}</div>
                  <small className="text-muted">{describeDays(daysSince(row.since))}</small>
                </td>
                <td className="text-end">
                  {row.creditLimit != null ? <span className="ta-money">{formatMoney(row.creditLimit)}</span> : <span className="text-muted">Sin fiado</span>}
                  {row.overLimit && <div><span className="badge bg-danger-subtle text-danger-emphasis">Pasó su límite</span></div>}
                </td>
                <td className="text-center">
                  <button
                    type="button"
                    className="btn btn-sm btn-light"
                    aria-label={`Ver la cuenta de ${row.name}`}
                    onClick={() => setAccount({ customerId: row.customerId, phone: row.customerId ? null : row.phone, name: row.name })}
                  >
                    Ver cuenta
                  </button>
                </td>
              </>
            )}
          />
        </div>
      </div>

      {account && <CustomerAccountModal of={account} name={account.name} onClose={closeAccount} />}
    </>
  );
}
