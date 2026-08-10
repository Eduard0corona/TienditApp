import { useCallback, useEffect, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import UploadLoadingModal from '../../components/common/UploadLoadingModal';
import { ordersApi } from '../../api/ordersApi';
import { inventoryApi } from '../../api/inventoryApi';
import { useOrderSignalR } from '../../hooks/useOrderSignalR';
import type { InventoryLocationDto, OrderDto, OrderPaymentMethod, OrderStatus, OrderType, PaginatedList } from '../../types';
import { OrderPaymentMethod as OrderPaymentMethodConst, OrderType as OrderTypeConst } from '../../types';

const STATUS_OPTIONS: Array<{ value: OrderStatus; label: string }> = [
  { value: 0, label: 'Pendiente' },
  { value: 1, label: 'Confirmada' },
  { value: 2, label: 'En preparación' },
  { value: 3, label: 'Lista' },
  { value: 4, label: 'Aceptada' },
  { value: 5, label: 'En progreso' },
  { value: 6, label: 'Entregada' },
  { value: 7, label: 'Completada' },
  { value: 99, label: 'Cancelada' },
];

const STATUS_LABELS = new Map<number, string>(STATUS_OPTIONS.map((s) => [s.value, s.label]));

const ORDER_TYPE_LABELS: Record<OrderType, string> = {
  [OrderTypeConst.Retail]: 'Retail',
  [OrderTypeConst.Service]: 'Servicio',
};

const ORDER_TYPE_STATUS_OPTIONS: Record<OrderType, Array<{ value: OrderStatus; label: string }>> = {
  [OrderTypeConst.Retail]: [
    { value: 0, label: 'Pendiente' },
    { value: 1, label: 'Confirmada' },
    { value: 2, label: 'En preparacion' },
    { value: 3, label: 'Lista' },
    { value: 6, label: 'Entregada' },
    { value: 99, label: 'Cancelada' },
  ],
  [OrderTypeConst.Service]: [
    { value: 0, label: 'Pendiente' },
    { value: 4, label: 'Aceptada' },
    { value: 5, label: 'En progreso' },
    { value: 7, label: 'Completada' },
    { value: 99, label: 'Cancelada' },
  ],
};

const PAYMENT_METHOD_LABELS: Record<number, string> = {
  [OrderPaymentMethodConst.Cash]: 'Efectivo',
  [OrderPaymentMethodConst.Card]: 'Tarjeta',
  [OrderPaymentMethodConst.Transfer]: 'Transferencia',
  [OrderPaymentMethodConst.DigitalWallet]: 'Billetera',
  [OrderPaymentMethodConst.Other]: 'Otro',
};

const SERVICE_PIECE_STATUS_LABELS: Record<number, string> = {
  0: 'Aceptada',
  1: 'En progreso',
  2: 'Completada',
  99: 'Cancelada',
};

const SERVICE_PIECE_STATUS_OPTIONS = [
  { value: 0, label: 'Aceptada' },
  { value: 1, label: 'En progreso' },
  { value: 2, label: 'Completada' },
  { value: 99, label: 'Cancelada' },
] as const;

const STATUS_BADGE_CLASS: Record<number, string> = {
  0: 'bg-warning',
  1: 'bg-info',
  2: 'bg-primary',
  3: 'bg-secondary',
  4: 'bg-info',
  5: 'bg-primary',
  6: 'bg-success',
  7: 'bg-success',
  99: 'bg-danger',
};

const COLUMNS = [
  { header: 'Orden' },
  { header: 'Tipo', className: 'text-center' },
  { header: 'Cliente' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Fecha', className: 'text-center' },
  { header: 'Total', className: 'text-end' },
  { header: 'Acciones', className: 'text-center' },
];

type ParsedChecklistItem = {
  label?: string;
  checked?: boolean;
};

type ParsedPhotoItem = {
  url?: string;
  fileName?: string;
};

function toLocalDateTime(date: string) {
  return new Date(date).toLocaleString('es-MX', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function getTodayInputDate(): string {
  const date = new Date();
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function getStatusLabel(status: OrderStatus, orderType?: OrderType): string {
  if (orderType !== undefined) {
    const option = ORDER_TYPE_STATUS_OPTIONS[orderType].find((item) => item.value === status);
    if (option) return option.label;
  }

  return STATUS_LABELS.get(status) ?? String(status);
}

function safeParseArray<T>(raw?: string): T[] {
  if (!raw) return [];
  try {
    const parsed = JSON.parse(raw) as unknown;
    return Array.isArray(parsed) ? (parsed as T[]) : [];
  } catch {
    return [];
  }
}

export default function OrdersList() {
  const today = getTodayInputDate();
  const [data, setData] = useState<PaginatedList<OrderDto>>({
    items: [],
    pageNumber: 1,
    totalPages: 0,
    totalCount: 0,
    hasPreviousPage: false,
    hasNextPage: false,
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [selectedOrderType, setSelectedOrderType] = useState('');
  const [selectedStatus, setSelectedStatus] = useState('');
  const [fromDate, setFromDate] = useState(today);
  const [toDate, setToDate] = useState(today);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [selectedOrder, setSelectedOrder] = useState<OrderDto | null>(null);
  const [paymentOrder, setPaymentOrder] = useState<OrderDto | null>(null);
  const [paymentAmount, setPaymentAmount] = useState('');
  const [paymentMethod, setPaymentMethod] = useState<OrderPaymentMethod>(OrderPaymentMethodConst.Cash);
  const [paymentLocationId, setPaymentLocationId] = useState('');
  const [paymentEvidenceUrl, setPaymentEvidenceUrl] = useState('');
  const [paymentEvidenceName, setPaymentEvidenceName] = useState('');
  const [paymentUploadingEvidence, setPaymentUploadingEvidence] = useState(false);
  const [paymentError, setPaymentError] = useState('');
  const [paymentSubmitting, setPaymentSubmitting] = useState(false);
  const [locations, setLocations] = useState<InventoryLocationDto[]>([]);
  const [realtimeMessage, setRealtimeMessage] = useState('');
  const { onOrderCreated, onOrderStatusChanged, onOrderCancelled } = useOrderSignalR();

  const selectedOrderTotalItems = useMemo(
    () => selectedOrder?.items.reduce((acc, item) => acc + item.quantity, 0) ?? 0,
    [selectedOrder],
  );
  const selectedOrderChecklist = useMemo(
    () => selectedOrder?.servicePieces.filter((piece) => piece.isTrackable) ?? [],
    [selectedOrder],
  );
  const selectedOrderIntakeChecklist = useMemo(
    () => safeParseArray<ParsedChecklistItem>(selectedOrder?.serviceDetail?.intakeChecklistJson),
    [selectedOrder],
  );
  const selectedOrderBeforePhotos = useMemo(() => {
    const raw = safeParseArray<ParsedPhotoItem | string>(selectedOrder?.serviceDetail?.beforePhotosJson);
    return raw
      .map((item) => (typeof item === 'string' ? { url: item } : item))
      .filter((item) => !!item.url);
  }, [selectedOrder]);
  const selectedOrderAfterPhotos = useMemo(() => {
    const raw = safeParseArray<ParsedPhotoItem | string>(selectedOrder?.serviceDetail?.afterPhotosJson);
    return raw
      .map((item) => (typeof item === 'string' ? { url: item } : item))
      .filter((item) => !!item.url);
  }, [selectedOrder]);
  const locationNameById = useMemo(
    () => new Map(locations.map((location) => [location.id, location.name])),
    [locations],
  );
  const paymentSummary = useMemo(() => {
    if (!paymentOrder) {
      return {
        pendingBefore: 0,
        paidBefore: 0,
        total: 0,
        amountToSettle: 0,
        pendingAfter: 0,
      };
    }

    const parsedAmount = Number(paymentAmount);
    const amount = Number.isFinite(parsedAmount) && parsedAmount > 0 ? parsedAmount : 0;
    const pendingBefore = Number(paymentOrder.balance.toFixed(2));

    return {
      total: Number(paymentOrder.total.toFixed(2)),
      paidBefore: Number(paymentOrder.paidAmount.toFixed(2)),
      pendingBefore,
      amountToSettle: pendingBefore,
      pendingAfter: Number(Math.max(0, pendingBefore - amount).toFixed(2)),
    };
  }, [paymentAmount, paymentOrder]);

  const loadOrders = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const params: {
        orderType?: OrderType;
        status?: OrderStatus;
        fromDate?: string;
        toDate?: string;
        pageNumber: number;
        pageSize: number;
      } = {
        pageNumber,
        pageSize,
      };

      if (selectedOrderType !== '') params.orderType = Number(selectedOrderType) as OrderType;
      if (selectedStatus !== '') params.status = Number(selectedStatus) as OrderStatus;
      if (fromDate) params.fromDate = new Date(`${fromDate}T00:00:00`).toISOString();
      if (toDate) params.toDate = new Date(`${toDate}T23:59:59`).toISOString();

      const { data: response } = await ordersApi.getAll(params);
      setData(response);
    } catch {
      setError('Error al cargar las órdenes.');
    } finally {
      setLoading(false);
    }
  }, [fromDate, pageNumber, pageSize, selectedOrderType, selectedStatus, toDate]);

  const refreshSelectedOrder = useCallback(async (orderId: string) => {
    const { data: order } = await ordersApi.getById(orderId);
    setSelectedOrder(order);
  }, []);

  useEffect(() => {
    loadOrders();
  }, [loadOrders]);

  useEffect(() => {
    const loadLocations = async () => {
      try {
        const { data } = await inventoryApi.getLocations({ isActive: true });
        setLocations(data);
      } catch {
        setLocations([]);
      }
    };

    void loadLocations();
  }, []);

  useEffect(() => {
    const createdOff = onOrderCreated((payload) => {
      setRealtimeMessage(`Nueva orden recibida: ${payload.orderNumber}`);
      loadOrders();
    });
    const changedOff = onOrderStatusChanged((payload) => {
      setRealtimeMessage(`Orden actualizada: ${payload.newStatus}`);
      loadOrders();
    });
    const cancelledOff = onOrderCancelled((payload) => {
      setRealtimeMessage(`Orden cancelada${payload.reason ? `: ${payload.reason}` : ''}`);
      loadOrders();
    });

    return () => {
      createdOff();
      changedOff();
      cancelledOff();
    };
  }, [loadOrders, onOrderCancelled, onOrderCreated, onOrderStatusChanged]);

  const openPaymentModal = (order: OrderDto) => {
    setPaymentOrder(order);
    setPaymentAmount(order.balance.toFixed(2));
    setPaymentMethod(OrderPaymentMethodConst.Cash);
    setPaymentLocationId('');
    setPaymentEvidenceUrl('');
    setPaymentEvidenceName('');
    setPaymentError('');
  };

  const closePaymentModal = () => {
    if (paymentSubmitting || paymentUploadingEvidence) return;
    setPaymentOrder(null);
    setPaymentAmount('');
    setPaymentMethod(OrderPaymentMethodConst.Cash);
    setPaymentLocationId('');
    setPaymentEvidenceUrl('');
    setPaymentEvidenceName('');
    setPaymentError('');
  };

  const handleUploadPaymentEvidence = async (file: File | undefined) => {
    if (!file) return;

    setPaymentError('');
    setPaymentUploadingEvidence(true);
    try {
      const { data } = await ordersApi.uploadPaymentEvidence(file);
      setPaymentEvidenceUrl(data.url);
      setPaymentEvidenceName(data.fileName);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.detail ||
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.error ||
        'No se pudo subir el comprobante.';
      setPaymentError(msg);
    } finally {
      setPaymentUploadingEvidence(false);
    }
  };

  const handleAddPayment = async () => {
    if (!paymentOrder) return;

    const amount = Number(paymentAmount);
    if (!Number.isFinite(amount) || amount <= 0) {
      setPaymentError('Monto de pago invalido.');
      return;
    }

    if (amount > paymentOrder.balance) {
      setPaymentError('El monto no puede ser mayor al saldo pendiente.');
      return;
    }

    if (!Object.values(OrderPaymentMethodConst).includes(paymentMethod)) {
      setPaymentError('Metodo de pago invalido.');
      return;
    }

    setPaymentSubmitting(true);
    setPaymentError('');

    try {
      await ordersApi.addPayment(paymentOrder.id, {
        amount,
        method: paymentMethod,
        inventoryLocationId: paymentLocationId || undefined,
        evidenceUrl: paymentEvidenceUrl || undefined,
      });
      await loadOrders();

      if (selectedOrder?.id === paymentOrder.id) {
        await refreshSelectedOrder(paymentOrder.id);
      }

      setPaymentOrder(null);
      setPaymentAmount('');
      setPaymentMethod(OrderPaymentMethodConst.Cash);
      setPaymentLocationId('');
      setPaymentEvidenceUrl('');
      setPaymentEvidenceName('');
      setPaymentError('');
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.detail ||
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.error ||
        'No se pudo registrar el pago.';
      setPaymentError(msg);
    } finally {
      setPaymentSubmitting(false);
    }
  };

  const getPieceAllowedTransitions = (status: number): number[] => {
    switch (status) {
      case 0: return [1, 99];
      case 1: return [2, 99];
      default: return [];
    }
  };

  const handleServicePieceStatusChange = async (orderId: string, pieceId: string, newStatus: string) => {
    if (!newStatus) return;

    try {
      await ordersApi.updateServicePieceStatus(orderId, pieceId, Number(newStatus) as 0 | 1 | 2 | 99);
      await Promise.all([loadOrders(), refreshSelectedOrder(orderId)]);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.detail ||
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.error ||
        'No se pudo actualizar el estado de la pieza.';
      setError(msg);
    }
  };

  return (
    <>
      <UploadLoadingModal
        show={paymentUploadingEvidence}
        title="Subiendo comprobante"
        description="Estamos cargando la evidencia de pago."
      />
      <PageTitle title="Órdenes" subtitle="Operación" />

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              <div className="row mb-3">
                <div className="col-md-2 mb-2">
                  <label className="form-label">Tipo</label>
                  <select
                    className="form-select form-select-sm"
                    value={selectedOrderType}
                    onChange={(e) => {
                      setSelectedOrderType(e.target.value);
                      setSelectedStatus('');
                      setPageNumber(1);
                    }}
                  >
                    <option value="">Todos</option>
                    <option value={OrderTypeConst.Retail}>{ORDER_TYPE_LABELS[OrderTypeConst.Retail]}</option>
                    <option value={OrderTypeConst.Service}>{ORDER_TYPE_LABELS[OrderTypeConst.Service]}</option>
                  </select>
                </div>
                <div className="col-md-2 mb-2">
                  <label className="form-label">Estado</label>
                  <select
                    className="form-select form-select-sm"
                    value={selectedStatus}
                    onChange={(e) => {
                      setSelectedStatus(e.target.value);
                      setPageNumber(1);
                    }}
                  >
                    <option value="">Todos</option>
                    {(selectedOrderType === ''
                      ? STATUS_OPTIONS
                      : ORDER_TYPE_STATUS_OPTIONS[Number(selectedOrderType) as OrderType]).map((status) => (
                        <option key={status.value} value={status.value}>
                          {status.label}
                        </option>
                      ))}
                  </select>
                </div>
                <div className="col-md-2 mb-2">
                  <label className="form-label">Desde</label>
                  <input
                    type="date"
                    className="form-control form-control-sm"
                    value={fromDate}
                    onChange={(e) => {
                      setFromDate(e.target.value);
                      setPageNumber(1);
                    }}
                  />
                </div>
                <div className="col-md-2 mb-2">
                  <label className="form-label">Hasta</label>
                  <input
                    type="date"
                    className="form-control form-control-sm"
                    value={toDate}
                    onChange={(e) => {
                      setToDate(e.target.value);
                      setPageNumber(1);
                    }}
                  />
                </div>
                <div className="col-md-4 mb-2 d-flex align-items-end">
                  <div className="d-flex gap-2">
                    <button
                      type="button"
                      className="btn btn-light btn-sm"
                      onClick={() => {
                        setSelectedOrderType('');
                        setSelectedStatus('');
                        setFromDate(today);
                        setToDate(today);
                        setPageNumber(1);
                      }}
                    >
                      Limpiar filtros
                    </button>
                    <Link to="/orders/cart" className="btn btn-primary btn-sm">
                      <i className="ri-add-line me-1"></i>
                      Generar Orden
                    </Link>
                  </div>
                </div>
              </div>

              {error && (
                <div className="alert alert-danger alert-dismissible fade show">
                  {error}
                  <button type="button" className="btn-close" onClick={() => setError('')}></button>
                </div>
              )}

              {realtimeMessage && (
                <div className="alert alert-info alert-dismissible fade show">
                  {realtimeMessage}
                  <button type="button" className="btn-close" onClick={() => setRealtimeMessage('')}></button>
                </div>
              )}

              <DataTable
                data={data.items}
                columns={COLUMNS}
                keyExtractor={(order) => order.id}
                loading={loading}
                emptyMessage="No hay órdenes registradas."
                searchFilter={(order, search) =>
                  order.orderNumber.toLowerCase().includes(search) ||
                  order.customerName.toLowerCase().includes(search) ||
                  (order.customerPhone || '').toLowerCase().includes(search)
                }
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
                renderRow={(order) => (
                  <>
                    <td>
                      <button
                        type="button"
                        className="btn btn-link p-0 fw-semibold text-decoration-none"
                        onClick={() => setSelectedOrder(order)}
                      >
                        {order.orderNumber}
                      </button>
                      <div>
                        <span className={`badge mt-1 ${order.createdByBusiness ? 'bg-info' : 'bg-secondary'}`}>
                          {order.createdByBusiness ? 'Creada por empresa' : 'Creada por cliente'}
                        </span>
                      </div>
                    </td>
                    <td className="text-center">
                      <span className={`badge ${order.orderType === OrderTypeConst.Service ? 'bg-info' : 'bg-secondary'}`}>
                        {ORDER_TYPE_LABELS[order.orderType]}
                      </span>
                    </td>
                    <td>
                      <div>{order.customerName}</div>
                      {order.customerPhone && <small className="text-muted">{order.customerPhone}</small>}
                    </td>
                    <td className="text-center">
                      <span className={`badge ${STATUS_BADGE_CLASS[order.status] ?? 'bg-secondary'}`}>
                        {getStatusLabel(order.status, order.orderType)}
                      </span>
                    </td>
                    <td className="text-center">{toLocalDateTime(order.createdAt)}</td>
                    <td className="text-end fw-semibold">
                      <div>${order.total.toFixed(2)}</div>
                      <small className={`d-block ${order.balance > 0 ? 'text-danger' : 'text-success'}`}>
                        Saldo: ${order.balance.toFixed(2)}
                      </small>
                    </td>
                    <td className="text-center">
                      <div className="d-flex justify-content-center">
                        <button
                          type="button"
                          className="btn btn-sm btn-soft-success"
                          disabled={order.balance <= 0 || order.status === 99}
                          onClick={() => openPaymentModal(order)}
                        >
                          Registrar pago
                        </button>
                      </div>
                    </td>
                  </>
                )}
              />
            </div>
          </div>
        </div>
      </div>

      <Modal show={!!selectedOrder} onHide={() => setSelectedOrder(null)} size="lg" centered>
        <Modal.Header closeButton>
          <Modal.Title>Detalle de Orden</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {selectedOrder && (
            <>
              <div className="row mb-3">
                <div className="col-md-6">
                  <p className="mb-1"><strong>Orden:</strong> {selectedOrder.orderNumber}</p>
                  <p className="mb-1"><strong>Tipo:</strong> {ORDER_TYPE_LABELS[selectedOrder.orderType]}</p>
                  <p className="mb-1"><strong>Cliente:</strong> {selectedOrder.customerName}</p>
                  <p className="mb-1"><strong>Teléfono:</strong> {selectedOrder.customerPhone || '-'}</p>
                  <p className="mb-1"><strong>Mesa:</strong> {selectedOrder.tableNumber || '-'}</p>
                </div>
                <div className="col-md-6">
                  <p className="mb-1"><strong>Estado:</strong> {getStatusLabel(selectedOrder.status, selectedOrder.orderType)}</p>
                  <p className="mb-1"><strong>Fecha:</strong> {toLocalDateTime(selectedOrder.createdAt)}</p>
                  <p className="mb-1"><strong>Items:</strong> {selectedOrderTotalItems}</p>
                  <p className="mb-1"><strong>Total:</strong> ${selectedOrder.total.toFixed(2)}</p>
                  <p className="mb-1"><strong>Pagado:</strong> ${selectedOrder.paidAmount.toFixed(2)}</p>
                  <p className="mb-1"><strong>Saldo:</strong> ${selectedOrder.balance.toFixed(2)}</p>
                </div>
              </div>

              <h5 className="mb-2">Items</h5>
              <div className="table-responsive">
                <table className="table table-sm table-striped mb-0">
                  <thead>
                    <tr>
                      <th>Producto</th>
                      <th className="text-center">Cantidad</th>
                      <th className="text-end">Precio Unit.</th>
                      <th className="text-end">Total</th>
                    </tr>
                  </thead>
                  <tbody>
                    {selectedOrder.items.map((item) => (
                      <tr key={item.id}>
                        <td>
                          {item.productName}
                          {item.notes && <div className="text-muted small">Nota: {item.notes}</div>}
                        </td>
                        <td className="text-center">{item.quantity}</td>
                        <td className="text-end">${item.unitPrice.toFixed(2)}</td>
                        <td className="text-end">${item.totalPrice.toFixed(2)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {selectedOrder.serviceDetail && (
                <div className="mt-3">
                  <h5 className="mb-2">Recepcion de servicio</h5>
                  <div className="row g-2">
                    <div className="col-md-6"><strong>Recibido:</strong> {toLocalDateTime(selectedOrder.serviceDetail.receivedAt)}</div>
                    <div className="col-md-6">
                      <strong>Promesa:</strong> {selectedOrder.serviceDetail.promisedAt
                        ? toLocalDateTime(selectedOrder.serviceDetail.promisedAt)
                        : '-'}
                    </div>
                    <div className="col-md-6"><strong>Prioridad:</strong> {selectedOrder.serviceDetail.priority}</div>
                  </div>
                  {selectedOrder.serviceDetail.intakeNotes && (
                    <div className="mt-2">
                      <strong>Notas de ingreso:</strong>
                      <div className="text-muted">{selectedOrder.serviceDetail.intakeNotes}</div>
                    </div>
                  )}
                  {selectedOrder.serviceDetail.deliveryNotes && (
                    <div className="mt-2">
                      <strong>Notas de entrega:</strong>
                      <div className="text-muted">{selectedOrder.serviceDetail.deliveryNotes}</div>
                    </div>
                  )}

                  {selectedOrderIntakeChecklist.length > 0 && (
                    <div className="mt-2">
                      <strong>Checklist de recepcion:</strong>
                      <ul className="mb-0 mt-1 ps-3">
                        {selectedOrderIntakeChecklist.map((item, index) => (
                          <li key={`${item.label || 'item'}-${index}`}>
                            {item.checked ? '[x]' : '[ ]'} {item.label || 'Item'}
                          </li>
                        ))}
                      </ul>
                    </div>
                  )}

                  {(selectedOrderBeforePhotos.length > 0 || selectedOrderAfterPhotos.length > 0) && (
                    <div className="row g-2 mt-2">
                      <div className="col-md-6">
                        <strong>Fotos antes</strong>
                        {selectedOrderBeforePhotos.length === 0 ? (
                          <div className="text-muted small">Sin fotos.</div>
                        ) : (
                          <div className="d-flex flex-wrap gap-2 mt-1">
                            {selectedOrderBeforePhotos.map((photo, index) => (
                              <a
                                key={`${photo.url || 'before'}-${index}`}
                                href={photo.url}
                                target="_blank"
                                rel="noreferrer"
                                className="d-inline-flex align-items-center justify-content-center border rounded"
                                style={{ width: 64, height: 64, overflow: 'hidden' }}
                              >
                                <img src={photo.url} alt={photo.fileName || 'Foto antes'} style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                              </a>
                            ))}
                          </div>
                        )}
                      </div>
                      <div className="col-md-6">
                        <strong>Fotos despues</strong>
                        {selectedOrderAfterPhotos.length === 0 ? (
                          <div className="text-muted small">Sin fotos.</div>
                        ) : (
                          <div className="d-flex flex-wrap gap-2 mt-1">
                            {selectedOrderAfterPhotos.map((photo, index) => (
                              <a
                                key={`${photo.url || 'after'}-${index}`}
                                href={photo.url}
                                target="_blank"
                                rel="noreferrer"
                                className="d-inline-flex align-items-center justify-content-center border rounded"
                                style={{ width: 64, height: 64, overflow: 'hidden' }}
                              >
                                <img src={photo.url} alt={photo.fileName || 'Foto despues'} style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                              </a>
                            ))}
                          </div>
                        )}
                      </div>
                    </div>
                  )}
                </div>
              )}

              {selectedOrderChecklist.length > 0 && (
                <div className="mt-3">
                  <h5 className="mb-2">Checklist de servicios rastreables</h5>
                  <div className="table-responsive">
                    <table className="table table-sm table-striped mb-0">
                      <thead>
                        <tr>
                          <th>Servicio</th>
                          <th>Notificar</th>
                          <th>Estado</th>
                          <th className="text-end">Accion</th>
                        </tr>
                      </thead>
                      <tbody>
                        {selectedOrderChecklist.map((piece) => (
                          <tr key={piece.id}>
                            <td>{piece.itemName}</td>
                            <td>{piece.notifyCustomer ? 'Si' : 'No'}</td>
                            <td>{SERVICE_PIECE_STATUS_LABELS[piece.status] || piece.status}</td>
                            <td className="text-end" style={{ minWidth: 220 }}>
                              <select
                                className="form-select form-select-sm"
                                value=""
                                disabled={getPieceAllowedTransitions(piece.status).length === 0}
                                onChange={(e) =>
                                  handleServicePieceStatusChange(selectedOrder.id, piece.id, e.target.value)
                                }
                              >
                                <option value="">Actualizar estado</option>
                                {SERVICE_PIECE_STATUS_OPTIONS
                                  .filter((status) => getPieceAllowedTransitions(piece.status).includes(status.value))
                                  .map((status) => (
                                    <option key={status.value} value={status.value}>
                                      {status.label}
                                    </option>
                                  ))}
                              </select>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {selectedOrder.payments.length > 0 && (
                <div className="mt-3">
                  <h5 className="mb-2">Pagos</h5>
                  <div className="table-responsive">
                    <table className="table table-sm table-striped mb-0">
                      <thead>
                        <tr>
                          <th>Fecha</th>
                          <th>Metodo</th>
                          <th>Sucursal</th>
                          <th>Evidencia</th>
                          <th className="text-end">Monto</th>
                        </tr>
                      </thead>
                      <tbody>
                        {selectedOrder.payments.map((payment) => (
                          <tr key={payment.id}>
                            <td>{toLocalDateTime(payment.paidAt)}</td>
                            <td>{PAYMENT_METHOD_LABELS[payment.method] || payment.method}</td>
                            <td>{payment.inventoryLocationId ? (locationNameById.get(payment.inventoryLocationId) || 'Sucursal') : '-'}</td>
                            <td>
                              {payment.evidenceUrl ? (
                                <a href={payment.evidenceUrl} target="_blank" rel="noreferrer">
                                  Ver
                                </a>
                              ) : '-'}
                            </td>
                            <td className="text-end">${payment.amount.toFixed(2)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {selectedOrder.customerNotes && (
                <div className="mt-3">
                  <strong>Notas del cliente:</strong>
                  <div className="text-muted">{selectedOrder.customerNotes}</div>
                </div>
              )}

              {selectedOrder.cancellationReason && (
                <div className="mt-3">
                  <strong>Motivo de cancelación:</strong>
                  <div className="text-danger">{selectedOrder.cancellationReason}</div>
                </div>
              )}
            </>
          )}
        </Modal.Body>
      </Modal>

      <Modal show={!!paymentOrder} onHide={closePaymentModal} centered>
        <Modal.Header closeButton={!paymentSubmitting && !paymentUploadingEvidence}>
          <Modal.Title>Registrar pago</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {paymentOrder && (
            <>
              <div className="mb-3">
                <div><strong>Orden:</strong> {paymentOrder.orderNumber}</div>
                <div><strong>Cliente:</strong> {paymentOrder.customerName}</div>
              </div>

              <div className="border rounded p-3 mb-3 bg-light-subtle">
                <div className="d-flex justify-content-between mb-1">
                  <span>Total de la orden</span>
                  <strong>${paymentSummary.total.toFixed(2)}</strong>
                </div>
                <div className="d-flex justify-content-between mb-1">
                  <span>Abonado previamente</span>
                  <strong>${paymentSummary.paidBefore.toFixed(2)}</strong>
                </div>
                <div className="d-flex justify-content-between mb-1">
                  <span>Pendiente actual</span>
                  <strong className="text-danger">${paymentSummary.pendingBefore.toFixed(2)}</strong>
                </div>
                <div className="d-flex justify-content-between">
                  <span>Liquida con</span>
                  <strong className="text-success">${paymentSummary.amountToSettle.toFixed(2)}</strong>
                </div>
              </div>

              <div className="mb-3">
                <label className="form-label">Monto a registrar</label>
                <input
                  type="number"
                  className="form-control"
                  min="0.01"
                  max={paymentSummary.pendingBefore}
                  step="0.01"
                  value={paymentAmount}
                  onChange={(e) => setPaymentAmount(e.target.value)}
                  disabled={paymentSubmitting || paymentUploadingEvidence}
                />
              </div>

              <div className="mb-3">
                <label className="form-label">Metodo de pago</label>
                <select
                  className="form-select"
                  value={paymentMethod}
                  onChange={(e) => setPaymentMethod(Number(e.target.value) as OrderPaymentMethod)}
                  disabled={paymentSubmitting || paymentUploadingEvidence}
                >
                  {Object.values(OrderPaymentMethodConst)
                    .filter((value): value is OrderPaymentMethod => typeof value === 'number')
                    .map((value) => (
                      <option key={value} value={value}>
                        {PAYMENT_METHOD_LABELS[value] || value}
                      </option>
                    ))}
                </select>
              </div>

              <div className="mb-3">
                <label className="form-label">Sucursal (opcional)</label>
                <select
                  className="form-select"
                  value={paymentLocationId}
                  onChange={(e) => setPaymentLocationId(e.target.value)}
                  disabled={paymentSubmitting || paymentUploadingEvidence}
                >
                  <option value="">Sin sucursal especifica</option>
                  {locations.map((location) => (
                    <option key={location.id} value={location.id}>
                      {location.name}
                    </option>
                  ))}
                </select>
              </div>

              <div className="mb-3">
                <label className="form-label">Comprobante (imagen o PDF)</label>
                <input
                  type="file"
                  className="form-control"
                  accept="image/*,.pdf"
                  disabled={paymentSubmitting || paymentUploadingEvidence}
                  onChange={(e) => {
                    void handleUploadPaymentEvidence(e.target.files?.[0]);
                    e.currentTarget.value = '';
                  }}
                />
                {paymentEvidenceUrl && (
                  <small className="text-success d-block mt-1">
                    Archivo listo: {paymentEvidenceName || 'Comprobante'}
                  </small>
                )}
              </div>

              <div className="border rounded p-2 bg-light-subtle">
                <small>
                  Saldo despues del pago: <strong>${paymentSummary.pendingAfter.toFixed(2)}</strong>
                </small>
              </div>

              {paymentError && <div className="alert alert-danger mt-3 mb-0">{paymentError}</div>}
            </>
          )}
        </Modal.Body>
        <Modal.Footer>
          <button
            type="button"
            className="btn btn-light"
            onClick={closePaymentModal}
            disabled={paymentSubmitting || paymentUploadingEvidence}
          >
            Cancelar
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleAddPayment}
            disabled={paymentSubmitting || paymentUploadingEvidence || !paymentOrder}
          >
            {paymentSubmitting ? 'Guardando...' : 'Registrar pago'}
          </button>
        </Modal.Footer>
      </Modal>
    </>
  );
}

