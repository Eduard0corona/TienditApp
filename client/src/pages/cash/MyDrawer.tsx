import { useCallback, useEffect, useState } from 'react';
import { Modal } from 'react-bootstrap';
import DataTable from '../../components/common/DataTable';
import { cashApi } from '../../api/cashApi';
import { getApiErrorMessage } from '../../api/errors';
import { useToast } from '../../hooks/useToast';
import type {
  CashCountLineDto,
  CashSessionDto,
  CashTransactionDto,
  CashTransactionType,
  InventoryLocationDto,
  PaginatedList,
} from '../../types';
import { CashTransactionType as CashTransactionTypeConst } from '../../types';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney, formatSignedMoney } from '../../utils/money';
import CloseDrawerModal from './CloseDrawerModal';
import { getTransactionTypeLabel, parseCashAmount } from './cashFormat';

const EMPTY_PAGE: PaginatedList<CashTransactionDto> = {
  items: [],
  pageNumber: 1,
  totalPages: 0,
  totalCount: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

const MOVEMENT_COLUMNS = [
  { header: 'Hora', className: 'text-center' },
  { header: 'Tipo' },
  { header: 'Monto', className: 'text-end' },
  { header: 'Detalle' },
];

const MANUAL_TYPES: CashTransactionType[] = [
  CashTransactionTypeConst.Expense,
  CashTransactionTypeConst.Withdrawal,
  CashTransactionTypeConst.Deposit,
  CashTransactionTypeConst.Refund,
];

interface MyDrawerProps {
  locations: InventoryLocationDto[];
  /** Called after the drawer opens or closes, so the history refreshes. */
  onChanged: () => void;
}

/** The cashier's own drawer: open it, see what it should hold, record expenses and close it with a count. */
export default function MyDrawer({ locations, onChanged }: MyDrawerProps) {
  const toast = useToast();
  const [session, setSession] = useState<CashSessionDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [movements, setMovements] = useState(EMPTY_PAGE);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [submitting, setSubmitting] = useState(false);
  const [actionError, setActionError] = useState('');
  const [showClose, setShowClose] = useState(false);
  const [showMovement, setShowMovement] = useState(false);

  const [openingAmount, setOpeningAmount] = useState('');
  const [openingNotes, setOpeningNotes] = useState('');
  const [openingLocationId, setOpeningLocationId] = useState('');

  const [movementType, setMovementType] = useState<CashTransactionType>(CashTransactionTypeConst.Expense);
  const [movementAmount, setMovementAmount] = useState('');
  const [movementReference, setMovementReference] = useState('');
  const [movementNotes, setMovementNotes] = useState('');

  const loadSession = useCallback(async () => {
    try {
      const { data } = await cashApi.getCurrentSession();
      setSession(data);
      setLoadError('');
    } catch (err: unknown) {
      setLoadError(getApiErrorMessage(err, 'No se pudo cargar tu caja.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadSession();
  }, [loadSession]);

  const sessionId = session?.id;
  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    cashApi.getTransactions({ cashSessionId: sessionId, pageNumber, pageSize })
      .then(({ data }) => {
        if (!cancelled) setMovements(data);
      })
      .catch(() => {
        if (!cancelled) setMovements(EMPTY_PAGE);
      });
    return () => {
      cancelled = true;
    };
  }, [pageNumber, pageSize, sessionId, session?.expectedBalance]);

  const openDrawer = async (event: React.FormEvent) => {
    event.preventDefault();
    const amount = openingAmount.trim() === '' ? 0 : parseCashAmount(openingAmount);
    if (amount === null) {
      setActionError('Escribe el fondo inicial, por ejemplo 500 o 350.50.');
      return;
    }
    setSubmitting(true);
    setActionError('');
    try {
      const { data } = await cashApi.openSession({
        openingAmount: amount,
        notes: openingNotes.trim() || undefined,
        inventoryLocationId: openingLocationId || undefined,
      });
      setSession(data);
      setOpeningAmount('');
      setOpeningNotes('');
      toast.success('Tu caja quedó abierta.');
      onChanged();
    } catch (err: unknown) {
      setActionError(getApiErrorMessage(err, 'No se pudo abrir la caja.'));
    } finally {
      setSubmitting(false);
    }
  };

  const closeDrawer = async (closingAmount: number, notes: string | undefined, cashCount: CashCountLineDto[] | undefined) => {
    if (!session) return;
    setSubmitting(true);
    setActionError('');
    try {
      const { data } = await cashApi.closeSession(session.id, { closingAmount, notes, cashCount });
      setShowClose(false);
      setSession(null);
      setMovements(EMPTY_PAGE);
      const difference = data.closingDifference ?? 0;
      if (difference === 0) toast.success('Caja cerrada: cuadró exacto.');
      else toast.warning(`Caja cerrada con ${difference < 0 ? 'faltante' : 'sobrante'} de ${formatMoney(Math.abs(difference))}.`);
      onChanged();
    } catch (err: unknown) {
      setActionError(getApiErrorMessage(err, 'No se pudo cerrar la caja.'));
    } finally {
      setSubmitting(false);
    }
  };

  const addMovement = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!session) return;
    // Always positive: the server applies the sign from the type (expenses, withdrawals and refunds subtract).
    const amount = parseCashAmount(movementAmount);
    if (amount === null || amount === 0) {
      setActionError('Escribe un monto mayor a 0, con máximo 2 decimales.');
      return;
    }
    setSubmitting(true);
    setActionError('');
    try {
      await cashApi.addTransaction({
        cashSessionId: session.id,
        transactionType: movementType,
        amount,
        inventoryLocationId: session.inventoryLocationId ?? undefined,
        reference: movementReference.trim() || undefined,
        notes: movementNotes.trim() || undefined,
      });
      setShowMovement(false);
      setMovementAmount('');
      setMovementReference('');
      setMovementNotes('');
      toast.success(`${getTransactionTypeLabel(movementType)} registrado.`);
      await loadSession();
    } catch (err: unknown) {
      setActionError(getApiErrorMessage(err, 'No se pudo registrar el movimiento.'));
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return <div className="text-center py-4"><div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando…</span></div></div>;
  }

  if (loadError) {
    return (
      <div className="alert alert-danger d-flex justify-content-between align-items-center">
        <span>{loadError}</span>
        <button type="button" className="btn btn-sm btn-light" onClick={() => void loadSession()}>Reintentar</button>
      </div>
    );
  }

  if (!session) {
    return (
      <div className="card">
        <div className="card-body">
          <h2 className="h5 mb-1">Abre tu caja</h2>
          <p className="text-muted mb-3">Cada cajero tiene su propia caja: el efectivo que cobres entra a tu corte.</p>
          <form className="row g-2 align-items-end" onSubmit={(event) => void openDrawer(event)}>
            <div className="col-sm-4 col-lg-3">
              <label className="form-label" htmlFor="drawer-opening">Fondo inicial</label>
              <input
                id="drawer-opening"
                className="form-control ta-money"
                inputMode="decimal"
                placeholder="$0.00"
                value={openingAmount}
                onChange={(event) => setOpeningAmount(event.target.value)}
                disabled={submitting}
              />
            </div>
            {locations.length > 1 && (
              <div className="col-sm-4 col-lg-3">
                <label className="form-label" htmlFor="drawer-location">Sucursal</label>
                <select
                  id="drawer-location"
                  className="form-select"
                  value={openingLocationId}
                  onChange={(event) => setOpeningLocationId(event.target.value)}
                  disabled={submitting}
                >
                  <option value="">Principal</option>
                  {locations.map((location) => (
                    <option key={location.id} value={location.id}>{location.name}</option>
                  ))}
                </select>
              </div>
            )}
            <div className="col-sm-4 col-lg-4">
              <label className="form-label" htmlFor="drawer-notes">Notas (opcional)</label>
              <input
                id="drawer-notes"
                className="form-control"
                maxLength={2000}
                value={openingNotes}
                onChange={(event) => setOpeningNotes(event.target.value)}
                disabled={submitting}
              />
            </div>
            <div className="col-sm-12 col-lg-2">
              <button type="submit" className="btn btn-primary btn-touch w-100" disabled={submitting}>
                {submitting ? 'Abriendo…' : 'Abrir mi caja'}
              </button>
            </div>
          </form>
          {actionError && <div className="alert alert-danger mt-3 mb-0">{actionError}</div>}
        </div>
      </div>
    );
  }

  return (
    <>
      <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-3">
        <div className="text-muted">
          {session.isMine ? 'Tu caja' : `Caja de ${session.openedBy}`}
          {session.inventoryLocationName ? ` · ${session.inventoryLocationName}` : ''}
          {' · abierta '}
          {new Date(session.openedAt).toLocaleString('es-MX', { dateStyle: 'short', timeStyle: 'short' })}
        </div>
        <div className="d-flex gap-2">
          <button type="button" className="btn btn-light" onClick={() => { setActionError(''); setShowMovement(true); }}>
            <i className="ri-add-line me-1" aria-hidden="true"></i>Gasto, retiro o depósito
          </button>
          <button type="button" className="btn btn-danger" onClick={() => { setActionError(''); setShowClose(true); }}>
            Cerrar caja
          </button>
        </div>
      </div>

      <div className="row g-3 mb-3">
        {[
          { label: 'Fondo inicial', value: session.openingAmount, className: '' },
          { label: 'Entró en efectivo', value: session.inflows, className: 'text-success' },
          { label: 'Salió en efectivo', value: session.outflows, className: 'text-danger' },
          { label: 'Tarjeta y otros', value: session.nonCashInflows, className: 'text-info', hint: 'No forman parte del efectivo en caja' },
          { label: 'Debe haber en caja', value: session.expectedBalance, className: 'fw-bold' },
        ].map((kpi) => (
          <div key={kpi.label} className="col-6 col-lg">
            <div className="card h-100 mb-0">
              <div className="card-body py-2">
                <div className="text-muted small" title={kpi.hint}>{kpi.label}</div>
                <div className={`h4 mb-0 ta-money ${kpi.className}`}>{formatMoney(kpi.value)}</div>
              </div>
            </div>
          </div>
        ))}
      </div>

      <div className="card">
        <div className="card-body">
          <h2 className="h5 mb-3">Movimientos de la caja</h2>
          <DataTable
            data={movements.items}
            columns={MOVEMENT_COLUMNS}
            keyExtractor={(item) => item.id}
            loading={false}
            emptyMessage="Todavía no hay movimientos en esta caja."
            searchFilter={(item, search) =>
              getTransactionTypeLabel(item.transactionType).toLowerCase().includes(search)
              || (item.reference || '').toLowerCase().includes(search)
              || (item.notes || '').toLowerCase().includes(search)}
            serverSide={{
              totalCount: movements.totalCount,
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
                <td className="text-center text-nowrap">{new Date(item.occurredAt).toLocaleTimeString('es-MX', { timeStyle: 'short' })}</td>
                <td>
                  {getTransactionTypeLabel(item.transactionType)}
                  {item.paymentMethod !== undefined && item.paymentMethod !== null && (
                    <div className="text-muted small">{getPaymentMethodLabel(item.paymentMethod)}</div>
                  )}
                </td>
                {/* The API stores the signed amount (outflows are negative), so the sign decides the color. */}
                <td className={`text-end text-nowrap ${item.amount >= 0 ? 'text-success' : 'text-danger'}`}>
                  {formatSignedMoney(item.amount)}
                </td>
                <td>
                  <div>{item.reference || '—'}</div>
                  {(item.notes || item.createdBy) && (
                    <div className="text-muted small">{[item.notes, item.createdBy].filter(Boolean).join(' · ')}</div>
                  )}
                </td>
              </>
            )}
          />
        </div>
      </div>

      {showClose && (
        <CloseDrawerModal
          session={session}
          submitting={submitting}
          error={actionError}
          onClose={() => setShowClose(false)}
          onSubmit={(amount, notes, count) => void closeDrawer(amount, notes, count)}
        />
      )}

      <Modal show={showMovement} onHide={() => setShowMovement(false)} centered>
        <form onSubmit={(event) => void addMovement(event)}>
          <Modal.Header closeButton>
            <Modal.Title as="h2" className="h5">Registrar movimiento</Modal.Title>
          </Modal.Header>
          <Modal.Body>
            <div className="mb-3" role="radiogroup" aria-label="Tipo de movimiento">
              <div className="d-flex flex-wrap gap-2">
                {MANUAL_TYPES.map((type) => (
                  <button
                    key={type}
                    type="button"
                    role="radio"
                    aria-checked={movementType === type}
                    className={`btn btn-sm ${movementType === type ? 'btn-primary' : 'btn-outline-secondary'}`}
                    onClick={() => setMovementType(type)}
                  >
                    {getTransactionTypeLabel(type)}
                  </button>
                ))}
              </div>
              <div className="form-text">Gastos, retiros y reembolsos restan del efectivo; los depósitos suman.</div>
            </div>
            <div className="mb-3">
              <label className="form-label" htmlFor="movement-amount">Monto</label>
              <input
                id="movement-amount"
                className="form-control form-control-lg ta-money"
                inputMode="decimal"
                value={movementAmount}
                onChange={(event) => setMovementAmount(event.target.value)}
                autoFocus
              />
            </div>
            <div className="mb-3">
              <label className="form-label" htmlFor="movement-reference">Referencia (opcional)</label>
              <input
                id="movement-reference"
                className="form-control"
                maxLength={120}
                placeholder="Ej. Factura del gas"
                value={movementReference}
                onChange={(event) => setMovementReference(event.target.value)}
              />
            </div>
            <div>
              <label className="form-label" htmlFor="movement-notes">Notas (opcional)</label>
              <input
                id="movement-notes"
                className="form-control"
                maxLength={1000}
                value={movementNotes}
                onChange={(event) => setMovementNotes(event.target.value)}
              />
            </div>
            {actionError && <div className="alert alert-danger mt-3 mb-0">{actionError}</div>}
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light" onClick={() => setShowMovement(false)} disabled={submitting}>Volver</button>
            <button type="submit" className="btn btn-primary" disabled={submitting}>
              {submitting ? 'Guardando…' : 'Registrar'}
            </button>
          </Modal.Footer>
        </form>
      </Modal>
    </>
  );
}
