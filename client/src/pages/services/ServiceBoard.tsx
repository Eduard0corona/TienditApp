import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import { ordersApi } from '../../api/ordersApi';
import { serviceBoardApi } from '../../api/serviceBoardApi';
import { useOrderSignalR } from '../../hooks/useOrderSignalR';
import { ServiceOrderPieceStatus as ServiceOrderPieceStatusConst } from '../../types';
import type { PaginatedList, ServiceBoardPieceDto, ServiceOrderPieceStatus } from '../../types';
import { getApiErrorMessage } from '../../api/errors';

const STATUS_COLUMNS: Array<{
  status: ServiceOrderPieceStatus;
  title: string;
  badgeClass: string;
}> = [
  { status: ServiceOrderPieceStatusConst.Accepted, title: 'Aceptadas', badgeClass: 'bg-info' },
  { status: ServiceOrderPieceStatusConst.InProgress, title: 'En progreso', badgeClass: 'bg-primary' },
  { status: ServiceOrderPieceStatusConst.Completed, title: 'Completadas', badgeClass: 'bg-success' },
  { status: ServiceOrderPieceStatusConst.Cancelled, title: 'Canceladas', badgeClass: 'bg-danger' },
];

function toLocalDateTime(date: string) {
  return new Date(date).toLocaleString('es-MX', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function getAllowedTransitions(status: ServiceOrderPieceStatus): ServiceOrderPieceStatus[] {
  switch (status) {
    case ServiceOrderPieceStatusConst.Accepted:
      return [ServiceOrderPieceStatusConst.InProgress, ServiceOrderPieceStatusConst.Cancelled];
    case ServiceOrderPieceStatusConst.InProgress:
      return [ServiceOrderPieceStatusConst.Completed, ServiceOrderPieceStatusConst.Cancelled];
    default:
      return [];
  }
}

export default function ServiceBoard() {
  const [data, setData] = useState<PaginatedList<ServiceBoardPieceDto>>({
    items: [],
    pageNumber: 1,
    totalPages: 0,
    totalCount: 0,
    hasPreviousPage: false,
    hasNextPage: false,
  });
  const [loading, setLoading] = useState(true);
  const [processingPieceId, setProcessingPieceId] = useState('');
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [showCompleted, setShowCompleted] = useState(false);
  const [showCancelled, setShowCancelled] = useState(false);
  const [onlyMine, setOnlyMine] = useState(false);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<string>('');
  const refreshTimeoutRef = useRef<number | undefined>(undefined);
  const { onOrderCreated, onOrderStatusChanged, onOrderCancelled, onServiceBoardPieceUpdated } = useOrderSignalR();

  const loadBoard = useCallback(async (silent = false) => {
    if (!silent) {
      setLoading(true);
    }

    setError('');
    try {
      const { data: response } = await serviceBoardApi.getPieces({
        search: debouncedSearch || undefined,
        includeCompleted: showCompleted,
        includeCancelled: showCancelled,
        onlyMine,
        pageNumber: 1,
        pageSize: 300,
      });

      setData(response);
      setLastUpdatedAt(new Date().toISOString());
    } catch {
      setError('No se pudieron cargar los servicios para el tablero.');
    } finally {
      if (!silent) {
        setLoading(false);
      }
    }
  }, [debouncedSearch, onlyMine, showCancelled, showCompleted]);

  const scheduleBoardRefresh = useCallback(() => {
    if (refreshTimeoutRef.current !== undefined) {
      return;
    }

    refreshTimeoutRef.current = window.setTimeout(() => {
      refreshTimeoutRef.current = undefined;
      void loadBoard(true);
    }, 150);
  }, [loadBoard]);

  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      setDebouncedSearch(search.trim());
    }, 250);

    return () => window.clearTimeout(timeoutId);
  }, [search]);

  useEffect(() => {
    void loadBoard(false);
  }, [loadBoard]);

  useEffect(() => {
    const createdOff = onOrderCreated(() => {
      scheduleBoardRefresh();
    });
    const changedOff = onOrderStatusChanged(() => {
      scheduleBoardRefresh();
    });
    const cancelledOff = onOrderCancelled(() => {
      scheduleBoardRefresh();
    });
    const pieceUpdatedOff = onServiceBoardPieceUpdated(() => {
      scheduleBoardRefresh();
    });

    return () => {
      createdOff();
      changedOff();
      cancelledOff();
      pieceUpdatedOff();
      if (refreshTimeoutRef.current !== undefined) {
        window.clearTimeout(refreshTimeoutRef.current);
        refreshTimeoutRef.current = undefined;
      }
    };
  }, [onOrderCancelled, onOrderCreated, onOrderStatusChanged, onServiceBoardPieceUpdated, scheduleBoardRefresh]);

  const piecesByStatus = useMemo(() => {
    const grouped: Record<number, ServiceBoardPieceDto[]> = {
      [ServiceOrderPieceStatusConst.Accepted]: [],
      [ServiceOrderPieceStatusConst.InProgress]: [],
      [ServiceOrderPieceStatusConst.Completed]: [],
      [ServiceOrderPieceStatusConst.Cancelled]: [],
    };

    for (const piece of data.items) {
      grouped[piece.status] ??= [];
      grouped[piece.status].push(piece);
    }

    return grouped;
  }, [data.items]);

  const totalTrackable = data.totalCount;
  const activeTrackable = data.items.filter(
    (piece) =>
      piece.status === ServiceOrderPieceStatusConst.Accepted ||
      piece.status === ServiceOrderPieceStatusConst.InProgress,
  ).length;
  const overdueTrackable = data.items.filter(
    (piece) =>
      piece.isOverdue &&
      (piece.status === ServiceOrderPieceStatusConst.Accepted ||
        piece.status === ServiceOrderPieceStatusConst.InProgress),
  ).length;
  const nearDueTrackable = data.items.filter(
    (piece) =>
      !piece.isOverdue &&
      (piece.minutesToPromise ?? Number.MAX_SAFE_INTEGER) <= 60 &&
      (piece.status === ServiceOrderPieceStatusConst.Accepted ||
        piece.status === ServiceOrderPieceStatusConst.InProgress),
  ).length;

  const handlePieceStatusChange = async (
    orderId: string,
    pieceId: string,
    nextStatus: ServiceOrderPieceStatus,
  ) => {
    setProcessingPieceId(pieceId);
    setError('');
    try {
      await ordersApi.updateServicePieceStatus(orderId, pieceId, nextStatus);
      await loadBoard();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'No se pudo actualizar el estado del servicio.');
      setError(msg);
    } finally {
      setProcessingPieceId('');
    }
  };

  const handleAssignToMe = async (pieceId: string) => {
    setProcessingPieceId(pieceId);
    setError('');
    try {
      await serviceBoardApi.assignToMe(pieceId);
      await loadBoard();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'No se pudo asignar la pieza.');
      setError(msg);
    } finally {
      setProcessingPieceId('');
    }
  };

  const handleRelease = async (pieceId: string) => {
    setProcessingPieceId(pieceId);
    setError('');
    try {
      await serviceBoardApi.release(pieceId);
      await loadBoard();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'No se pudo liberar la asignación de la pieza.');
      setError(msg);
    } finally {
      setProcessingPieceId('');
    }
  };

  return (
    <>
      <PageTitle title="Tablero de Servicios" subtitle="Operación" />

      <div className="row mb-3">
        <div className="col-xl-2 col-md-6 mb-2">
          <div className="card mb-0">
            <div className="card-body py-2">
              <div className="text-muted small">Piezas rastreables</div>
              <div className="h4 mb-0">{totalTrackable}</div>
            </div>
          </div>
        </div>
        <div className="col-xl-2 col-md-6 mb-2">
          <div className="card mb-0">
            <div className="card-body py-2">
              <div className="text-muted small">Activas</div>
              <div className="h4 mb-0">{activeTrackable}</div>
            </div>
          </div>
        </div>
        <div className="col-xl-2 col-md-6 mb-2">
          <div className="card mb-0 border border-danger-subtle">
            <div className="card-body py-2">
              <div className="text-muted small">Atrasadas</div>
              <div className="h4 mb-0 text-danger">{overdueTrackable}</div>
            </div>
          </div>
        </div>
        <div className="col-xl-2 col-md-6 mb-2">
          <div className="card mb-0 border border-warning-subtle">
            <div className="card-body py-2">
              <div className="text-muted small">Por vencer (&lt;1h)</div>
              <div className="h4 mb-0 text-warning">{nearDueTrackable}</div>
            </div>
          </div>
        </div>
        <div className="col-xl-4 mb-2">
          <div className="card mb-0">
            <div className="card-body py-2 d-flex flex-wrap gap-3 align-items-center justify-content-between">
              <div className="d-flex flex-wrap gap-2 align-items-center">
                <input
                  type="text"
                  className="form-control form-control-sm"
                  style={{ width: 260 }}
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Buscar por orden, cliente, pieza o teléfono"
                />
                <div className="form-check">
                  <input
                    id="onlyMinePieces"
                    className="form-check-input"
                    type="checkbox"
                    checked={onlyMine}
                    onChange={(e) => setOnlyMine(e.target.checked)}
                  />
                  <label className="form-check-label" htmlFor="onlyMinePieces">
                    Mis servicios
                  </label>
                </div>
                <div className="form-check">
                  <input
                    id="showCompletedPieces"
                    className="form-check-input"
                    type="checkbox"
                    checked={showCompleted}
                    onChange={(e) => setShowCompleted(e.target.checked)}
                  />
                  <label className="form-check-label" htmlFor="showCompletedPieces">
                    Mostrar completadas
                  </label>
                </div>
                <div className="form-check">
                  <input
                    id="showCancelledPieces"
                    className="form-check-input"
                    type="checkbox"
                    checked={showCancelled}
                    onChange={(e) => setShowCancelled(e.target.checked)}
                  />
                  <label className="form-check-label" htmlFor="showCancelledPieces">
                    Mostrar canceladas
                  </label>
                </div>
              </div>
              <div className="d-flex flex-column align-items-end">
                <small className="text-muted">
                  Última actualización: {lastUpdatedAt ? toLocalDateTime(lastUpdatedAt) : '-'}
                </small>
                <button type="button" className="btn btn-light btn-sm mt-1" onClick={() => void loadBoard()}>
                  Recargar
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError('')}></button>
        </div>
      )}

      {loading ? (
        <div className="text-center py-5">
          <div className="spinner-border text-primary" role="status"></div>
        </div>
      ) : data.items.length === 0 ? (
        <div className="card">
          <div className="card-body text-center py-5 text-muted">
            No hay servicios rastreables para mostrar con los filtros actuales.
          </div>
        </div>
      ) : (
        <div className="row">
          {STATUS_COLUMNS.map((column) => (
            <div className="col-12 col-lg-6 col-xxl-3 mb-3" key={column.status}>
              <div className="card h-100">
                <div className="card-header d-flex justify-content-between align-items-center">
                  <span className="fw-semibold">{column.title}</span>
                  <span className={`badge ${column.badgeClass}`}>
                    {piecesByStatus[column.status]?.length ?? 0}
                  </span>
                </div>
                <div className="card-body" style={{ maxHeight: 620, overflowY: 'auto' }}>
                  {(piecesByStatus[column.status] ?? []).length === 0 ? (
                    <div className="text-muted small">Sin piezas</div>
                  ) : (
                    piecesByStatus[column.status].map((piece) => {
                      const transitions = getAllowedTransitions(piece.status);
                      const isProcessing = processingPieceId === piece.pieceId;

                      return (
                        <div key={piece.pieceId} className="border rounded p-2 mb-2 bg-light-subtle">
                          <div className="d-flex justify-content-between align-items-start mb-1">
                            <div>
                              <div className="fw-semibold">{piece.itemName}</div>
                              {piece.requestedService && (
                                <small className="text-muted">{piece.requestedService}</small>
                              )}
                            </div>
                            <div className="d-flex flex-column align-items-end gap-1">
                              {piece.priority > 0 && (
                                <span className="badge bg-warning text-dark">P{piece.priority}</span>
                              )}
                              {piece.isOverdue && (
                                <span className="badge bg-danger">
                                  Atrasada {Math.max(1, Math.floor((piece.delayMinutes ?? 0) / 60))}h
                                </span>
                              )}
                              {!piece.isOverdue && piece.minutesToPromise !== undefined && (
                                <span className="badge bg-secondary-subtle text-dark">
                                  SLA {Math.max(0, piece.minutesToPromise)} min
                                </span>
                              )}
                            </div>
                          </div>

                          <div className="small">
                            <div><strong>Orden:</strong> {piece.orderNumber}</div>
                            <div><strong>Cliente:</strong> {piece.customerName}</div>
                            {piece.customerPhone && <div><strong>Tel:</strong> {piece.customerPhone}</div>}
                            <div><strong>Ingreso:</strong> {toLocalDateTime(piece.orderCreatedAt)}</div>
                            {piece.promisedAt && (
                              <div className={piece.isOverdue ? 'text-danger fw-semibold' : undefined}>
                                <strong>Promesa:</strong> {toLocalDateTime(piece.promisedAt)}
                              </div>
                            )}
                            {piece.internalNotes && (
                              <div className="text-muted mt-1">Nota interna: {piece.internalNotes}</div>
                            )}
                          </div>

                          <div className="d-flex justify-content-between align-items-center mt-2 gap-2 flex-wrap">
                            <span className={`badge ${piece.notifyCustomer ? 'bg-info' : 'bg-secondary'}`}>
                              {piece.notifyCustomer ? 'Notifica al cliente' : 'Sin notificación'}
                            </span>
                            {piece.assignedToUserId ? (
                              <span className="badge bg-primary-subtle text-primary">
                                Asignada: {piece.assignedToName || 'Empleado'}
                              </span>
                            ) : (
                              <span className="badge bg-light text-muted">Sin asignar</span>
                            )}
                          </div>

                          <div className="d-flex gap-1 mt-2">
                            {!piece.assignedToUserId ? (
                              <button
                                type="button"
                                className="btn btn-sm btn-outline-primary flex-fill"
                                disabled={isProcessing}
                                onClick={() => void handleAssignToMe(piece.pieceId)}
                              >
                                Tomar servicio
                              </button>
                            ) : (
                              <button
                                type="button"
                                className="btn btn-sm btn-outline-secondary flex-fill"
                                disabled={isProcessing}
                                onClick={() => void handleRelease(piece.pieceId)}
                              >
                                Liberar
                              </button>
                            )}
                          </div>

                          {transitions.length > 0 && (
                            <div className="d-flex gap-1 mt-2">
                              {transitions.map((status) => (
                                <button
                                  key={status}
                                  type="button"
                                  className={`btn btn-sm flex-fill ${
                                    status === ServiceOrderPieceStatusConst.Completed
                                      ? 'btn-success'
                                      : status === ServiceOrderPieceStatusConst.Cancelled
                                        ? 'btn-danger'
                                        : 'btn-primary'
                                  }`}
                                  disabled={isProcessing}
                                  onClick={() =>
                                    void handlePieceStatusChange(piece.orderId, piece.pieceId, status)
                                  }
                                >
                                  {status === ServiceOrderPieceStatusConst.InProgress && 'En progreso'}
                                  {status === ServiceOrderPieceStatusConst.Completed && 'Completar'}
                                  {status === ServiceOrderPieceStatusConst.Cancelled && 'Cancelar'}
                                </button>
                              ))}
                            </div>
                          )}
                        </div>
                      );
                    })
                  )}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}
    </>
  );
}
