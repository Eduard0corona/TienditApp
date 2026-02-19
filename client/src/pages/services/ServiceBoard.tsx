import { useCallback, useEffect, useMemo, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import { ordersApi } from '../../api/ordersApi';
import { useOrderSignalR } from '../../hooks/useOrderSignalR';
import { OrderType as OrderTypeConst, ServiceOrderPieceStatus as ServiceOrderPieceStatusConst } from '../../types';
import type { OrderDto, PaginatedList, ServiceOrderPieceStatus } from '../../types';

interface ServiceBoardPiece {
  orderId: string;
  orderNumber: string;
  customerName: string;
  customerPhone?: string;
  createdAt: string;
  promisedAt?: string;
  priority: number;
  pieceId: string;
  itemName: string;
  requestedService?: string;
  internalNotes?: string;
  notifyCustomer: boolean;
  status: ServiceOrderPieceStatus;
}

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

function mapOrdersToPieces(orders: OrderDto[]): ServiceBoardPiece[] {
  return orders.flatMap((order) =>
    order.servicePieces
      .filter((piece) => piece.isTrackable)
      .map((piece) => ({
        orderId: order.id,
        orderNumber: order.orderNumber,
        customerName: order.customerName,
        customerPhone: order.customerPhone,
        createdAt: order.createdAt,
        promisedAt: order.serviceDetail?.promisedAt,
        priority: order.serviceDetail?.priority ?? 0,
        pieceId: piece.id,
        itemName: piece.itemName,
        requestedService: piece.requestedService,
        internalNotes: piece.internalNotes,
        notifyCustomer: piece.notifyCustomer,
        status: piece.status,
      })),
  );
}

export default function ServiceBoard() {
  const [data, setData] = useState<PaginatedList<OrderDto>>({
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
  const [showCompleted, setShowCompleted] = useState(false);
  const [showCancelled, setShowCancelled] = useState(false);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<string>('');
  const { onOrderCreated, onOrderStatusChanged, onOrderCancelled } = useOrderSignalR();

  const loadBoard = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const { data: response } = await ordersApi.getAll({
        orderType: OrderTypeConst.Service,
        pageNumber: 1,
        pageSize: 200,
      });
      setData(response);
      setLastUpdatedAt(new Date().toISOString());
    } catch {
      setError('No se pudieron cargar los servicios para el tablero.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadBoard();
  }, [loadBoard]);

  useEffect(() => {
    const createdOff = onOrderCreated(() => {
      void loadBoard();
    });
    const changedOff = onOrderStatusChanged(() => {
      void loadBoard();
    });
    const cancelledOff = onOrderCancelled(() => {
      void loadBoard();
    });

    return () => {
      createdOff();
      changedOff();
      cancelledOff();
    };
  }, [loadBoard, onOrderCancelled, onOrderCreated, onOrderStatusChanged]);

  useEffect(() => {
    const intervalId = window.setInterval(() => {
      void loadBoard();
    }, 30000);

    return () => window.clearInterval(intervalId);
  }, [loadBoard]);

  const pieces = useMemo(() => mapOrdersToPieces(data.items), [data.items]);

  const visiblePieces = useMemo(() => {
    const normalizedSearch = search.trim().toLowerCase();

    return pieces.filter((piece) => {
      if (!showCompleted && piece.status === ServiceOrderPieceStatusConst.Completed) {
        return false;
      }

      if (!showCancelled && piece.status === ServiceOrderPieceStatusConst.Cancelled) {
        return false;
      }

      if (!normalizedSearch) {
        return true;
      }

      return (
        piece.orderNumber.toLowerCase().includes(normalizedSearch) ||
        piece.customerName.toLowerCase().includes(normalizedSearch) ||
        piece.itemName.toLowerCase().includes(normalizedSearch) ||
        (piece.customerPhone || '').toLowerCase().includes(normalizedSearch)
      );
    });
  }, [pieces, search, showCancelled, showCompleted]);

  const piecesByStatus = useMemo(() => {
    const grouped: Record<number, ServiceBoardPiece[]> = {
      [ServiceOrderPieceStatusConst.Accepted]: [],
      [ServiceOrderPieceStatusConst.InProgress]: [],
      [ServiceOrderPieceStatusConst.Completed]: [],
      [ServiceOrderPieceStatusConst.Cancelled]: [],
    };

    for (const piece of visiblePieces) {
      grouped[piece.status] ??= [];
      grouped[piece.status].push(piece);
    }

    for (const key of Object.keys(grouped)) {
      grouped[Number(key)].sort((a, b) => {
        if (a.priority !== b.priority) {
          return b.priority - a.priority;
        }

        return new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime();
      });
    }

    return grouped;
  }, [visiblePieces]);

  const totalTrackable = pieces.length;
  const activeTrackable = pieces.filter(
    (piece) =>
      piece.status === ServiceOrderPieceStatusConst.Accepted ||
      piece.status === ServiceOrderPieceStatusConst.InProgress,
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
      const msg =
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.detail ||
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.error ||
        'No se pudo actualizar el estado del servicio.';
      setError(msg);
    } finally {
      setProcessingPieceId('');
    }
  };

  return (
    <>
      <PageTitle title="Tablero de Servicios" subtitle="Operacion" />

      <div className="row mb-3">
        <div className="col-xl-3 col-md-6 mb-2">
          <div className="card mb-0">
            <div className="card-body py-2">
              <div className="text-muted small">Piezas rastreables</div>
              <div className="h4 mb-0">{totalTrackable}</div>
            </div>
          </div>
        </div>
        <div className="col-xl-3 col-md-6 mb-2">
          <div className="card mb-0">
            <div className="card-body py-2">
              <div className="text-muted small">Activas</div>
              <div className="h4 mb-0">{activeTrackable}</div>
            </div>
          </div>
        </div>
        <div className="col-xl-6 mb-2">
          <div className="card mb-0">
            <div className="card-body py-2 d-flex flex-wrap gap-3 align-items-center justify-content-between">
              <div className="d-flex flex-wrap gap-2 align-items-center">
                <input
                  type="text"
                  className="form-control form-control-sm"
                  style={{ width: 260 }}
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Buscar por orden, cliente, pieza o telefono"
                />
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
                  Ultima actualizacion: {lastUpdatedAt ? toLocalDateTime(lastUpdatedAt) : '-'}
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
      ) : visiblePieces.length === 0 ? (
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
                            {piece.priority > 0 && (
                              <span className="badge bg-warning text-dark">P{piece.priority}</span>
                            )}
                          </div>

                          <div className="small">
                            <div><strong>Orden:</strong> {piece.orderNumber}</div>
                            <div><strong>Cliente:</strong> {piece.customerName}</div>
                            {piece.customerPhone && <div><strong>Tel:</strong> {piece.customerPhone}</div>}
                            <div><strong>Ingreso:</strong> {toLocalDateTime(piece.createdAt)}</div>
                            {piece.promisedAt && (
                              <div><strong>Promesa:</strong> {toLocalDateTime(piece.promisedAt)}</div>
                            )}
                            {piece.internalNotes && (
                              <div className="text-muted mt-1">Nota interna: {piece.internalNotes}</div>
                            )}
                          </div>

                          <div className="d-flex justify-content-between align-items-center mt-2">
                            <span className={`badge ${piece.notifyCustomer ? 'bg-info' : 'bg-secondary'}`}>
                              {piece.notifyCustomer ? 'Notifica al cliente' : 'Sin notificacion'}
                            </span>
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
