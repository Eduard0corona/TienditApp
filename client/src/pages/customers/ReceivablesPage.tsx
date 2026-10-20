import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import CustomerAccountModal from '../../components/customers/CustomerAccountModal';
import CreditTermModal from '../../components/customers/CreditTermModal';
import CreditRemindersModal from '../../components/customers/CreditRemindersModal';
import { daysSince, describeDays, describeTerm } from '../../components/customers/customerAccount';
import { type AccountOf, type Receivable, useReceivables } from '../../api/customerCredit';
import { ReceivablesSort } from '../../api/generated/enums';
import { getApiErrorMessage } from '../../api/errors';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
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
 * Fase 26B: with a term, what is overdue per customer and in total, and «Solo vencidos»; owners and admins set the term.
 * Fase 26C: a customer's own term in their row, and the WhatsApp reminders of what is overdue, which owners and admins set.
 */
export default function ReceivablesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState<ReceivablesSort>(ReceivablesSort.Owed);
  const [overdueOnly, setOverdueOnly] = useState(false);
  const [editingTerm, setEditingTerm] = useState(false);
  const [editingReminders, setEditingReminders] = useState(false);
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const canSetTerm = getAccess(user, capabilities).isAdmin;
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const linkedPhone = searchParams.get('cuenta')?.trim() || null;
  const [account, setAccount] = useState<(AccountOf & { name?: string }) | null>(linkedPhone ? { phone: linkedPhone } : null);
  const { data, error, isPending } = useReceivables({ search, sort, overdueOnly, page, pageSize });
  const creditTerm = data?.creditTermDays ?? null;
  const reminderEveryDays = data?.creditReminderEveryDays ?? null;
  // Customers with a term of their own may have something overdue without the business's; «Solo vencidos» stays while on.
  const showsOverdue = creditTerm != null || (data?.totalOverdue ?? 0) > 0 || overdueOnly;

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
          {data && (
            <div className="d-flex flex-wrap align-items-baseline justify-content-between gap-2 mb-3">
              <p className="mb-0">
                Te deben <strong className="ta-money fs-5">{formatMoney(data.totalOwed)}</strong>
                {data.page.totalCount > 0 && (
                  <span className="text-muted"> · {data.page.totalCount === 1 ? '1 cliente' : `${data.page.totalCount} clientes`}</span>
                )}
                {data.totalOverdue > 0 && (
                  <> · <strong className="text-danger text-nowrap">Vencido <span className="ta-money">{formatMoney(data.totalOverdue)}</span></strong></>
                )}
              </p>
              <div className="small text-muted d-flex flex-wrap column-gap-3 row-gap-1">
                <span>
                  {creditTerm != null ? `Plazo de ${describeTerm(creditTerm)}` : 'Sin plazo'}
                  {canSetTerm && (
                    <button type="button" className="btn btn-link btn-sm align-baseline p-0 ms-2" onClick={() => setEditingTerm(true)}>
                      {creditTerm != null ? 'Cambiar plazo' : 'Poner plazo'}
                    </button>
                  )}
                </span>
                {(canSetTerm || reminderEveryDays != null) && (
                  <span>
                    {reminderEveryDays != null ? `Recordatorios cada ${describeTerm(reminderEveryDays)}` : 'Sin recordatorios'}
                    {canSetTerm && (
                      <button type="button" className="btn btn-link btn-sm align-baseline p-0 ms-2" onClick={() => setEditingReminders(true)}>
                        {reminderEveryDays != null ? 'Cambiar' : 'Activar'}
                      </button>
                    )}
                  </span>
                )}
              </div>
            </div>
          )}

          <DataTable<Receivable>
            data={data?.page.items ?? []}
            columns={COLUMNS}
            keyExtractor={(row) => row.phone}
            loading={isPending}
            emptyMessage={overdueOnly
              ? 'Nadie tiene algo vencido.'
              : search ? 'Nadie que coincida te debe.' : 'Nadie te debe: los pedidos entregados están pagados.'}
            searchPlaceholder="Nombre o teléfono"
            searchMaxLength={100}
            filters={(
              <>
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
                {showsOverdue && (
                  <div className="form-check form-switch mb-1">
                    <input
                      id="receivables-overdue"
                      type="checkbox"
                      role="switch"
                      className="form-check-input"
                      checked={overdueOnly}
                      onChange={(event) => {
                        setOverdueOnly(event.target.checked);
                        setPage(1);
                      }}
                    />
                    <label className="form-check-label" htmlFor="receivables-overdue">Solo vencidos</label>
                  </div>
                )}
              </>
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
                <td className="text-end">
                  <span className="fw-semibold ta-money text-danger">{formatMoney(row.owed)}</span>
                  {row.overdue > 0 && (
                    <small className="d-block text-danger">Vencido <span className="ta-money">{formatMoney(row.overdue)}</span></small>
                  )}
                </td>
                <td className="text-center">{row.orders}</td>
                <td>
                  <div>{formatDate(row.since)}</div>
                  <small className="text-muted">{describeDays(daysSince(row.since))}</small>
                </td>
                <td className="text-end">
                  {row.creditLimit != null ? <span className="ta-money">{formatMoney(row.creditLimit)}</span> : <span className="text-muted">Sin fiado</span>}
                  {row.overLimit && <div><span className="badge bg-danger-subtle text-danger-emphasis">Pasó su límite</span></div>}
                  {row.ownCreditTermDays != null && (
                    <small className="d-block text-muted">Paga en {describeTerm(row.ownCreditTermDays)}</small>
                  )}
                </td>
                <td className="text-center">
                  <button
                    type="button"
                    className="btn btn-sm btn-light"
                    aria-label={`Ver cuenta de ${row.name}`}
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
      {editingTerm && (
        <CreditTermModal
          creditTermDays={creditTerm}
          // Without the business's term, only customers with their own may have something overdue: show everyone again.
          onSaved={(days) => days == null && setOverdueOnly(false)}
          onClose={() => setEditingTerm(false)}
        />
      )}
      {editingReminders && <CreditRemindersModal onClose={() => setEditingReminders(false)} />}
    </>
  );
}
