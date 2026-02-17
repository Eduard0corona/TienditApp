import { useCallback, useEffect, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { cashApi } from '../../api/cashApi';
import type {
  AddCashTransactionRequest,
  CashSessionDto,
  CashTransactionDto,
  CashTransactionType,
  PaginatedList,
} from '../../types';
import { CashTransactionType as CashTransactionTypeConst } from '../../types';

const EMPTY_PAGINATED = {
  items: [],
  pageNumber: 1,
  totalPages: 0,
  totalCount: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

const COLUMNS = [
  { header: 'Fecha', className: 'text-center' },
  { header: 'Tipo' },
  { header: 'Monto', className: 'text-end' },
  { header: 'Referencia' },
];

const getErrorMessage = (err: unknown, fallback: string) =>
  (err as { response?: { data?: { error?: string } } })?.response?.data?.error || fallback;

const transactionTypeLabel = (type: CashTransactionType) => {
  switch (type) {
    case CashTransactionTypeConst.SalePayment: return 'Pago de venta';
    case CashTransactionTypeConst.Refund: return 'Reembolso';
    case CashTransactionTypeConst.Expense: return 'Gasto';
    case CashTransactionTypeConst.Withdrawal: return 'Retiro';
    case CashTransactionTypeConst.Deposit: return 'Depósito';
    case CashTransactionTypeConst.OpeningAdjustment: return 'Ajuste apertura';
    case CashTransactionTypeConst.ClosingAdjustment: return 'Ajuste cierre';
    default: return String(type);
  }
};

export default function CashManagement() {
  const [session, setSession] = useState<CashSessionDto | null>(null);
  const [transactions, setTransactions] = useState<PaginatedList<CashTransactionDto>>(EMPTY_PAGINATED);
  const [loading, setLoading] = useState(true);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [openingAmount, setOpeningAmount] = useState('0');
  const [closingAmount, setClosingAmount] = useState('');
  const [transactionType, setTransactionType] = useState<CashTransactionType>(CashTransactionTypeConst.Expense);
  const [transactionAmount, setTransactionAmount] = useState('');
  const [transactionReference, setTransactionReference] = useState('');
  const [transactionNotes, setTransactionNotes] = useState('');

  const loadSession = useCallback(async () => {
    const { data } = await cashApi.getCurrentSession();
    setSession(data);
  }, []);

  const loadTransactions = useCallback(async () => {
    if (!session) {
      setTransactions(EMPTY_PAGINATED);
      return;
    }
    const { data } = await cashApi.getTransactions({
      cashSessionId: session.id,
      pageNumber,
      pageSize,
    });
    setTransactions(data);
  }, [pageNumber, pageSize, session]);

  const loadAll = useCallback(async () => {
    setLoading(true);
    try {
      await loadSession();
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'No se pudo cargar la caja.'));
    } finally {
      setLoading(false);
    }
  }, [loadSession]);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  useEffect(() => {
    if (!session) return;
    loadTransactions().catch(() => undefined);
  }, [loadTransactions, session]);

  const openSession = async () => {
    setError('');
    setSuccess('');
    const amount = Number(openingAmount);
    if (Number.isNaN(amount) || amount < 0) {
      setError('Monto de apertura inválido.');
      return;
    }
    try {
      await cashApi.openSession({ openingAmount: amount });
      setSuccess('Caja abierta correctamente.');
      await loadAll();
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'No se pudo abrir la caja.'));
    }
  };

  const closeSession = async () => {
    if (!session) return;
    setError('');
    setSuccess('');
    const amount = Number(closingAmount);
    if (Number.isNaN(amount)) {
      setError('Monto de cierre inválido.');
      return;
    }
    try {
      await cashApi.closeSession(session.id, { closingAmount: amount });
      setSuccess('Caja cerrada correctamente.');
      setClosingAmount('');
      await loadAll();
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'No se pudo cerrar la caja.'));
    }
  };

  const addTransaction = async () => {
    if (!session) return;
    setError('');
    setSuccess('');
    const amount = Number(transactionAmount);
    if (Number.isNaN(amount) || amount === 0) {
      setError('Monto inválido.');
      return;
    }

    const payload: AddCashTransactionRequest = {
      cashSessionId: session.id,
      transactionType,
      amount,
      reference: transactionReference.trim() || undefined,
      notes: transactionNotes.trim() || undefined,
    };

    try {
      await cashApi.addTransaction(payload);
      setSuccess('Movimiento registrado.');
      setTransactionAmount('');
      setTransactionReference('');
      setTransactionNotes('');
      await Promise.all([loadAll(), loadTransactions()]);
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'No se pudo registrar el movimiento.'));
    }
  };

  return (
    <>
      <PageTitle title="Caja" subtitle="Pagos y arqueo" />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError('')}></button>
        </div>
      )}
      {success && (
        <div className="alert alert-success alert-dismissible fade show">
          {success}
          <button type="button" className="btn-close" onClick={() => setSuccess('')}></button>
        </div>
      )}

      {loading ? (
        <div className="text-center py-4">
          <div className="spinner-border text-primary" role="status"></div>
        </div>
      ) : !session ? (
        <div className="card">
          <div className="card-body">
            <h5>Abrir caja</h5>
            <div className="row align-items-end">
              <div className="col-md-4 mb-2">
                <label className="form-label">Monto de apertura</label>
                <input
                  type="number"
                  step="0.01"
                  className="form-control"
                  value={openingAmount}
                  onChange={(e) => setOpeningAmount(e.target.value)}
                />
              </div>
              <div className="col-md-2 mb-2">
                <button className="btn btn-primary w-100" onClick={openSession}>
                  Abrir
                </button>
              </div>
            </div>
          </div>
        </div>
      ) : (
        <>
          <div className="row mb-3">
            <div className="col-md-3">
              <div className="card mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">Apertura</div>
                  <div className="h4 mb-0">${session.openingAmount.toFixed(2)}</div>
                </div>
              </div>
            </div>
            <div className="col-md-3">
              <div className="card mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">Entradas</div>
                  <div className="h4 mb-0 text-success">${session.inflows.toFixed(2)}</div>
                </div>
              </div>
            </div>
            <div className="col-md-3">
              <div className="card mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">Salidas</div>
                  <div className="h4 mb-0 text-danger">${session.outflows.toFixed(2)}</div>
                </div>
              </div>
            </div>
            <div className="col-md-3">
              <div className="card mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">Esperado</div>
                  <div className="h4 mb-0">${session.expectedBalance.toFixed(2)}</div>
                </div>
              </div>
            </div>
          </div>

          <div className="card mb-3">
            <div className="card-body">
              <h5 className="mb-2">Registrar movimiento manual</h5>
              <div className="row">
                <div className="col-md-3 mb-2">
                  <label className="form-label">Tipo</label>
                  <select
                    className="form-select"
                    value={transactionType}
                    onChange={(e) => setTransactionType(Number(e.target.value) as CashTransactionType)}
                  >
                    <option value={CashTransactionTypeConst.Expense}>Gasto</option>
                    <option value={CashTransactionTypeConst.Deposit}>Depósito</option>
                    <option value={CashTransactionTypeConst.Withdrawal}>Retiro</option>
                    <option value={CashTransactionTypeConst.Refund}>Reembolso</option>
                  </select>
                </div>
                <div className="col-md-2 mb-2">
                  <label className="form-label">Monto</label>
                  <input
                    type="number"
                    step="0.01"
                    className="form-control"
                    value={transactionAmount}
                    onChange={(e) => setTransactionAmount(e.target.value)}
                  />
                </div>
                <div className="col-md-3 mb-2">
                  <label className="form-label">Referencia</label>
                  <input
                    className="form-control"
                    value={transactionReference}
                    onChange={(e) => setTransactionReference(e.target.value)}
                  />
                </div>
                <div className="col-md-4 mb-2">
                  <label className="form-label">Notas</label>
                  <input
                    className="form-control"
                    value={transactionNotes}
                    onChange={(e) => setTransactionNotes(e.target.value)}
                  />
                </div>
              </div>
              <button className="btn btn-primary btn-sm me-2" onClick={addTransaction}>Registrar movimiento</button>
            </div>
          </div>

          <div className="card">
            <div className="card-body">
              <div className="d-flex justify-content-between align-items-center mb-2">
                <h5 className="mb-0">Movimientos</h5>
                <div className="d-flex align-items-center gap-2">
                  <input
                    type="number"
                    step="0.01"
                    className="form-control form-control-sm"
                    placeholder="Monto cierre"
                    value={closingAmount}
                    onChange={(e) => setClosingAmount(e.target.value)}
                  />
                  <button className="btn btn-danger btn-sm" onClick={closeSession}>Cerrar caja</button>
                </div>
              </div>

              <DataTable
                data={transactions.items}
                columns={COLUMNS}
                keyExtractor={(item) => item.id}
                loading={false}
                emptyMessage="No hay movimientos."
                searchFilter={(item, search) =>
                  transactionTypeLabel(item.transactionType).toLowerCase().includes(search) ||
                  (item.reference || '').toLowerCase().includes(search) ||
                  (item.notes || '').toLowerCase().includes(search)
                }
                serverSide={{
                  totalCount: transactions.totalCount,
                  page: pageNumber,
                  pageSize,
                  onPageChange: setPageNumber,
                  onPageSizeChange: (size) => {
                    setPageSize(size);
                    setPageNumber(1);
                  },
                }}
                renderRow={(item) => (
                  <>
                    <td className="text-center">{new Date(item.occurredAt).toLocaleString('es-MX')}</td>
                    <td>{transactionTypeLabel(item.transactionType)}</td>
                    <td className={`text-end ${item.amount >= 0 ? 'text-success' : 'text-danger'}`}>
                      ${item.amount.toFixed(2)}
                    </td>
                    <td>
                      <div>{item.reference || '-'}</div>
                      <div className="text-muted small">{item.notes || '-'}</div>
                    </td>
                  </>
                )}
              />
            </div>
          </div>
        </>
      )}
    </>
  );
}
