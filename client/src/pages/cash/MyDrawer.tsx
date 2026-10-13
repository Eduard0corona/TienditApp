import { useCallback, useEffect, useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import CashCutTicket from '../../components/cash/CashCutTicket';
import DataTable from '../../components/common/DataTable';
import PrintButton from '../../components/common/PrintButton';
import WhatsAppLink from '../../components/common/WhatsAppLink';
import type { Branch } from '../../api/branchesApi';
import { cashApi } from '../../api/cashApi';
import { getApiErrorMessage } from '../../api/errors';
import { useRefreshCashState } from '../../api/queries';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import type {
  CashCountLineDto,
  CashSessionDto,
  CashTransactionDto,
  CashTransactionType,
  PaginatedList,
} from '../../types';
import { CashTransactionType as CashTransactionTypeConst } from '../../types';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney, formatSignedMoney } from '../../utils/money';
import CloseDrawerModal from './CloseDrawerModal';
import { buildShiftCutText } from './cashCutText';
import { describeDifference, getTransactionTypeLabel, parseCashAmount, personLabel } from './cashFormat';
import { formatInBusinessZone } from '../../utils/dates';

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
  branches: Branch[];
  /** Branch this device works at: the drawer opens there unless another one is chosen. */
  workingBranchId?: string;
  /** The drawer was opened at this branch: the device keeps working there. */
  onBranchChosen: (branchId: string) => void;
  /** Called after the drawer opens or closes, so the history refreshes. */
  onChanged: () => void;
}

/** The cashier's own drawer: open it, see what it should hold, record expenses and close it with a count. */
export default function MyDrawer({ branches, workingBranchId, onBranchChosen, onChanged }: MyDrawerProps) {
  const titleId = useId();
  const toast = useToast();
  const { tenant } = useTenant();
  const refreshCashState = useRefreshCashState();
  const [session, setSession] = useState<CashSessionDto | null>(null);
  // The drawer just closed: its cut stays at hand to print it or send it.
  const [closedCut, setClosedCut] = useState<{ session: CashSessionDto; movements: CashTransactionDto[] } | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [movements, setMovements] = useState(EMPTY_PAGE);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [movementSearch, setMovementSearch] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [actionError, setActionError] = useState('');
  const [showClose, setShowClose] = useState(false);
  const [showMovement, setShowMovement] = useState(false);

  const [openingAmount, setOpeningAmount] = useState('');
  const [openingNotes, setOpeningNotes] = useState('');
  const [openingBranchId, setOpeningBranchId] = useState('');
  const chosenBranchId = openingBranchId || workingBranchId || '';

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
    cashApi.getTransactions({ cashSessionId: sessionId, search: movementSearch || undefined, pageNumber, pageSize })
      .then(({ data }) => {
        if (!cancelled) setMovements(data);
      })
      .catch(() => {
        if (!cancelled) setMovements(EMPTY_PAGE);
      });
    return () => {
      cancelled = true;
    };
  }, [movementSearch, pageNumber, pageSize, session?.expectedBalance, sessionId]);

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
        branchId: chosenBranchId || undefined,
      });
      setSession(data);
      setClosedCut(null);
      refreshCashState();
      if (data.branchId) onBranchChosen(data.branchId);
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
      setClosedCut({ session: data, movements: [] });
      refreshCashState();
      cashApi.getTransactions({ cashSessionId: data.id, pageSize: 200 })
        .then(({ data: page }) => setClosedCut((current) => (current?.session.id === data.id ? { ...current, movements: page.items } : current)))
        .catch(() => undefined);
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
      refreshCashState();
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
      <>
        {closedCut && (
          <div className="card border-success mb-3">
            <div className="card-body d-flex flex-wrap align-items-center justify-content-between gap-2">
              <div>
                <h2 className="h6 mb-1">Corte de tu caja</h2>
                <div className="small text-muted">
                  Debía haber {formatMoney(closedCut.session.expectedClosingAmount ?? closedCut.session.expectedBalance)}
                  {' · '}Contado {formatMoney(closedCut.session.closingAmount ?? 0)}
                  {' · '}{describeDifference(closedCut.session.closingDifference ?? 0, formatMoney)}
                </div>
              </div>
              <div className="d-flex flex-wrap gap-2">
                <PrintButton
                  label="Imprimir corte"
                  render={() => <CashCutTicket businessName={tenant?.name} session={closedCut.session} movements={closedCut.movements} />}
                />
                <WhatsAppLink text={buildShiftCutText(closedCut.session, tenant?.name)} />
                <button type="button" className="btn btn-link" onClick={() => setClosedCut(null)}>Listo</button>
              </div>
            </div>
          </div>
        )}
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
              {branches.length > 1 && (
                <div className="col-sm-4 col-lg-3">
                  <label className="form-label" htmlFor="drawer-branch">Sucursal</label>
                  <select
                    id="drawer-branch"
                    className="form-select"
                    value={chosenBranchId}
                    onChange={(event) => setOpeningBranchId(event.target.value)}
                    disabled={submitting}
                  >
                    {branches.map((branch) => (
                      <option key={branch.id} value={branch.id}>{branch.name}{branch.isDefault ? ' (principal)' : ''}</option>
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
      </>
    );
  }

  return (
    <>
      <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-3">
        <div className="text-muted">
          {session.isMine ? 'Tu caja' : `Caja de ${personLabel(session.openedByName, session.openedBy)}`}
          {(session.branchName ?? session.inventoryLocationName) ? ` · ${session.branchName ?? session.inventoryLocationName}` : ''}
          {' · abierta '}
          {formatInBusinessZone(session.openedAt, { dateStyle: 'short', timeStyle: 'short' })}
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
            serverSide={{
              totalCount: movements.totalCount,
              page: pageNumber,
              pageSize,
              onPageChange: setPageNumber,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPageNumber(1);
              },
              onSearchChange: (term) => {
                setMovementSearch(term);
                setPageNumber(1);
              },
            }}
            searchPlaceholder="Referencia, nota o quién lo registró"
            renderRow={(item) => (
              <>
                <td className="text-center text-nowrap">{formatInBusinessZone(item.occurredAt, { timeStyle: 'short' })}</td>
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

      <Modal show={showMovement} onHide={() => setShowMovement(false)} centered aria-labelledby={titleId}>
        <form onSubmit={(event) => void addMovement(event)}>
          <Modal.Header closeButton>
            <Modal.Title id={titleId} as="h2" className="h5">Registrar movimiento</Modal.Title>
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
