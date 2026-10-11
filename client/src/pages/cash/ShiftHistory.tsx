import { useCallback, useEffect, useState } from 'react';
import { Modal } from 'react-bootstrap';
import CashCutTicket from '../../components/cash/CashCutTicket';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import PrintButton from '../../components/common/PrintButton';
import WhatsAppLink from '../../components/common/WhatsAppLink';
import type { Branch } from '../../api/branchesApi';
import { cashApi } from '../../api/cashApi';
import { getApiErrorMessage } from '../../api/errors';
import { useRefreshCashState } from '../../api/queries';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import type { CashCountLineDto, CashSessionDto, CashTransactionDto, PaginatedList } from '../../types';
import { CashSessionStatus } from '../../types';
import { formatMoney, formatSignedMoney } from '../../utils/money';
import CloseDrawerModal from './CloseDrawerModal';
import { buildShiftCutText } from './cashCutText';
import { describeDifference, getTransactionTypeLabel, personLabel } from './cashFormat';
import { formatInBusinessZone, businessDayEnd, businessDayStart } from '../../utils/dates';

const EMPTY_SESSIONS: PaginatedList<CashSessionDto> = {
  items: [],
  pageNumber: 1,
  totalPages: 0,
  totalCount: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

const COLUMNS = [
  { header: 'Turno' },
  { header: 'Cajero' },
  { header: 'Sucursal' },
  { header: 'Debía haber', className: 'text-end' },
  { header: 'Contado', className: 'text-end' },
  { header: 'Diferencia', className: 'text-center' },
];

interface ShiftHistoryProps {
  branches: Branch[];
  /** Owners and admins can close a drawer that another cashier left open. */
  canCloseOthers: boolean;
  /** Changes when a drawer opens or closes elsewhere on the page, to reload. */
  refreshKey: number;
}

function toStartOfDay(date: string) {
  return date ? businessDayStart(date) : undefined;
}

function toEndOfDay(date: string) {
  return date ? businessDayEnd(date) : undefined;
}

/** Every drawer by date, branch and status, with what it should have held, what was counted and why it differed. */
export default function ShiftHistory({ branches, canCloseOthers, refreshKey }: ShiftHistoryProps) {
  const toast = useToast();
  const { tenant } = useTenant();
  const refreshCashState = useRefreshCashState();
  const [data, setData] = useState(EMPTY_SESSIONS);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [branchId, setBranchId] = useState('');
  const [onlyWithDifference, setOnlyWithDifference] = useState(false);
  const [selected, setSelected] = useState<CashSessionDto | null>(null);
  const [selectedMovements, setSelectedMovements] = useState<CashTransactionDto[]>([]);
  const [closing, setClosing] = useState(false);
  const [closeError, setCloseError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const { data: page } = await cashApi.getSessions({
        fromDate: toStartOfDay(fromDate),
        toDate: toEndOfDay(toDate),
        branchId: branchId || undefined,
        onlyWithDifference: onlyWithDifference || undefined,
        pageNumber,
        pageSize,
      });
      setData(page);
      setError('');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo cargar el historial de turnos.'));
    } finally {
      setLoading(false);
    }
  }, [branchId, fromDate, onlyWithDifference, pageNumber, pageSize, toDate]);

  useEffect(() => {
    void load();
  }, [load, refreshKey]);

  const openDetail = async (session: CashSessionDto) => {
    setSelected(session);
    setSelectedMovements([]);
    try {
      const { data: page } = await cashApi.getTransactions({ cashSessionId: session.id, pageSize: 200 });
      setSelectedMovements(page.items);
    } catch {
      setSelectedMovements([]);
    }
  };

  const closeOthersDrawer = async (closingAmount: number, notes: string | undefined, cashCount: CashCountLineDto[] | undefined) => {
    if (!selected) return;
    setSubmitting(true);
    setCloseError('');
    try {
      await cashApi.closeSession(selected.id, { closingAmount, notes, cashCount });
      setClosing(false);
      setSelected(null);
      toast.success(`Caja de ${personLabel(selected.openedByName, selected.openedBy)} cerrada.`);
      refreshCashState();
      await load();
    } catch (err: unknown) {
      setCloseError(getApiErrorMessage(err, 'No se pudo cerrar la caja.'));
    } finally {
      setSubmitting(false);
    }
  };

  const resetPage = () => setPageNumber(1);

  return (
    <div className="card">
      <div className="card-body">
        {error && (
          <div className="alert alert-danger d-flex justify-content-between align-items-center">
            <span>{error}</span>
            <button type="button" className="btn btn-sm btn-light" onClick={() => void load()}>Reintentar</button>
          </div>
        )}

        <DataTable
          data={data.items}
          columns={COLUMNS}
          keyExtractor={(session) => session.id}
          loading={loading}
          emptyMessage="No hay turnos con estos filtros."
          filters={(
            <>
              <DataTableFilter label="Desde" htmlFor="shifts-from">
                <input id="shifts-from" type="date" className="form-control form-control-sm" value={fromDate} max={toDate || undefined}
                  onChange={(event) => { setFromDate(event.target.value); resetPage(); }} />
              </DataTableFilter>
              <DataTableFilter label="Hasta" htmlFor="shifts-to">
                <input id="shifts-to" type="date" className="form-control form-control-sm" value={toDate} min={fromDate || undefined}
                  onChange={(event) => { setToDate(event.target.value); resetPage(); }} />
              </DataTableFilter>
              {branches.length > 1 && (
                <DataTableFilter label="Sucursal" htmlFor="shifts-branch">
                  <select id="shifts-branch" className="form-select form-select-sm" value={branchId}
                    onChange={(event) => { setBranchId(event.target.value); resetPage(); }}>
                    <option value="">Todas</option>
                    {branches.map((branch) => (
                      <option key={branch.id} value={branch.id}>{branch.name}</option>
                    ))}
                  </select>
                </DataTableFilter>
              )}
              <div className="form-check form-switch mb-1">
                <input id="shifts-difference" type="checkbox" role="switch" className="form-check-input" checked={onlyWithDifference}
                  onChange={(event) => { setOnlyWithDifference(event.target.checked); resetPage(); }} />
                <label className="form-check-label" htmlFor="shifts-difference">Solo con faltante o sobrante</label>
              </div>
            </>
          )}
          serverSide={{
            totalCount: data.totalCount,
            page: pageNumber,
            pageSize,
            onPageChange: setPageNumber,
            onPageSizeChange: (size) => {
              setPageSize(size);
              setPageNumber(1);
            },
          }}
          renderRow={(session) => {
            const isOpen = session.status === CashSessionStatus.Open;
            const difference = session.closingDifference ?? 0;
            return (
              <>
                <td>
                  <button type="button" className="btn btn-link p-0 text-start" onClick={() => void openDetail(session)}>
                    {formatInBusinessZone(session.openedAt, { dateStyle: 'short', timeStyle: 'short' })}
                  </button>
                  <div className="small text-muted">
                    {isOpen ? 'Abierta' : `Cerrada ${session.closedAt ? formatInBusinessZone(session.closedAt, { timeStyle: 'short' }) : ''}`}
                  </div>
                </td>
                <td>{personLabel(session.openedByName, session.openedBy)}{session.isMine && <span className="badge bg-light text-dark ms-1">Tú</span>}</td>
                <td>{session.branchName ?? session.inventoryLocationName ?? '—'}</td>
                <td className="text-end ta-money">{formatMoney(isOpen ? session.expectedBalance : session.expectedClosingAmount ?? session.expectedBalance)}</td>
                <td className="text-end ta-money">{isOpen ? '—' : formatMoney(session.closingAmount ?? 0)}</td>
                <td className="text-center">
                  {isOpen ? (
                    <span className="ta-chip ta-chip-confirmed">En curso</span>
                  ) : session.closingDifference === null || session.closingDifference === undefined ? (
                    <span className="text-muted">—</span>
                  ) : (
                    <span className={`ta-chip ${difference === 0 ? 'ta-chip-ready' : difference < 0 ? 'ta-chip-cancelled' : 'ta-chip-new'}`}>
                      {describeDifference(difference, formatMoney)}
                    </span>
                  )}
                </td>
              </>
            );
          }}
        />
      </div>

      <Modal show={selected !== null && !closing} onHide={() => setSelected(null)} centered size="lg">
        {selected && (
          <>
            <Modal.Header closeButton>
              <Modal.Title as="h2" className="h5">
                Turno de {personLabel(selected.openedByName, selected.openedBy)}
                <span className="d-block small text-muted fw-normal">
                  {selected.branchName ?? selected.inventoryLocationName ?? 'Sin sucursal'} · {formatInBusinessZone(selected.openedAt, { dateStyle: 'medium', timeStyle: 'short' })}
                </span>
              </Modal.Title>
            </Modal.Header>
            <Modal.Body>
              <dl className="row mb-2">
                <dt className="col-6">Fondo inicial</dt><dd className="col-6 text-end">{formatMoney(selected.openingAmount)}</dd>
                <dt className="col-6">Entró en efectivo</dt><dd className="col-6 text-end">{formatMoney(selected.inflows)}</dd>
                <dt className="col-6">Salió en efectivo</dt><dd className="col-6 text-end">{formatMoney(selected.outflows)}</dd>
                <dt className="col-6">Tarjeta y otros</dt><dd className="col-6 text-end">{formatMoney(selected.nonCashInflows)}</dd>
                <dt className="col-6">Debía haber</dt>
                <dd className="col-6 text-end fw-bold">{formatMoney(selected.expectedClosingAmount ?? selected.expectedBalance)}</dd>
                {selected.status === CashSessionStatus.Closed && (
                  <>
                    <dt className="col-6">Contado{selected.closedBy ? ` (${personLabel(selected.closedByName, selected.closedBy)})` : ''}</dt>
                    <dd className="col-6 text-end fw-bold">{formatMoney(selected.closingAmount ?? 0)}</dd>
                    {selected.closingDifference !== null && selected.closingDifference !== undefined && (
                      <>
                        <dt className="col-6">Diferencia</dt>
                        <dd className={`col-6 text-end fw-bold ${selected.closingDifference < 0 ? 'text-danger' : selected.closingDifference > 0 ? 'text-warning' : ''}`}>
                          {describeDifference(selected.closingDifference, formatMoney)}
                        </dd>
                      </>
                    )}
                  </>
                )}
              </dl>
              {selected.closingNotes && <div className="alert alert-light py-2"><strong>Nota del cierre:</strong> {selected.closingNotes}</div>}
              {selected.cashCount.length > 0 && (
                <p className="small text-muted">
                  Conteo: {selected.cashCount.map((line) => `${line.quantity} × ${formatMoney(line.denomination)}`).join(', ')}
                </p>
              )}
              <h3 className="h6 mt-3">Movimientos</h3>
              {selectedMovements.length === 0 ? (
                <p className="text-muted small mb-0">Sin movimientos.</p>
              ) : (
                <ul className="list-unstyled mb-0 small">
                  {selectedMovements.map((movement) => (
                    <li key={movement.id} className="d-flex justify-content-between border-bottom py-1">
                      <span>
                        {formatInBusinessZone(movement.occurredAt, { timeStyle: 'short' })} · {getTransactionTypeLabel(movement.transactionType)}
                        {movement.reference ? ` · ${movement.reference}` : ''}
                      </span>
                      <span className={movement.amount >= 0 ? 'text-success' : 'text-danger'}>{formatSignedMoney(movement.amount)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </Modal.Body>
            <Modal.Footer>
              <PrintButton
                label={selected.status === CashSessionStatus.Closed ? 'Imprimir corte' : 'Imprimir corte parcial'}
                render={() => <CashCutTicket businessName={tenant?.name} session={selected} movements={selectedMovements} />}
              />
              <WhatsAppLink text={buildShiftCutText(selected, tenant?.name)} />
              {selected.status === CashSessionStatus.Open && !selected.isMine && canCloseOthers && (
                <button type="button" className="btn btn-outline-danger" onClick={() => { setCloseError(''); setClosing(true); }}>
                  Cerrar esta caja
                </button>
              )}
            </Modal.Footer>
          </>
        )}
      </Modal>

      {closing && selected && (
        <CloseDrawerModal
          session={selected}
          submitting={submitting}
          error={closeError}
          onClose={() => setClosing(false)}
          onSubmit={(amount, notes, count) => void closeOthersDrawer(amount, notes, count)}
        />
      )}
    </div>
  );
}
