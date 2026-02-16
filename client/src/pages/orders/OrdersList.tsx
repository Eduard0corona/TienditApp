import { useCallback, useEffect, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { ordersApi } from '../../api/ordersApi';
import { tenantApi } from '../../api/tenantApi';
import { useOrderSignalR } from '../../hooks/useOrderSignalR';
import type { BusinessType, OrderDto, OrderStatus, PaginatedList } from '../../types';

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

const BUSINESS_STATUS_OPTIONS: Record<BusinessType, Array<{ value: OrderStatus; label: string }>> = {
  0: [
    { value: 0, label: 'Pendiente' },
    { value: 1, label: 'Confirmada' },
    { value: 2, label: 'En preparacion' },
    { value: 3, label: 'Lista' },
    { value: 6, label: 'Entregada' },
    { value: 99, label: 'Cancelada' },
  ],
  1: [
    { value: 0, label: 'Pendiente' },
    { value: 1, label: 'Confirmada' },
    { value: 6, label: 'Entregada' },
    { value: 99, label: 'Cancelada' },
  ],
  2: [
    { value: 0, label: 'Pendiente' },
    { value: 4, label: 'Aceptada' },
    { value: 5, label: 'En progreso' },
    { value: 7, label: 'Completada' },
    { value: 99, label: 'Cancelada' },
  ],
};

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
  { header: 'Cliente' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Fecha', className: 'text-center' },
  { header: 'Total', className: 'text-end' },
  { header: 'Acciones', className: 'text-center' },
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

function getTodayInputDate(): string {
  const date = new Date();
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function getStatusLabel(status: OrderStatus): string {
  return STATUS_LABELS.get(status) ?? String(status);
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
  const [selectedStatus, setSelectedStatus] = useState('');
  const [fromDate, setFromDate] = useState(today);
  const [toDate, setToDate] = useState(today);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [selectedOrder, setSelectedOrder] = useState<OrderDto | null>(null);
  const [businessType, setBusinessType] = useState<BusinessType | null>(null);
  const [realtimeMessage, setRealtimeMessage] = useState('');
  const { onOrderCreated, onOrderStatusChanged, onOrderCancelled } = useOrderSignalR();

  const selectedOrderTotalItems = useMemo(
    () => selectedOrder?.items.reduce((acc, item) => acc + item.quantity, 0) ?? 0,
    [selectedOrder],
  );

  const loadOrders = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const params: {
        status?: OrderStatus;
        fromDate?: string;
        toDate?: string;
        pageNumber: number;
        pageSize: number;
      } = {
        pageNumber,
        pageSize,
      };

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
  }, [fromDate, pageNumber, pageSize, selectedStatus, toDate]);

  useEffect(() => {
    loadOrders();
  }, [loadOrders]);

  useEffect(() => {
    if (data.items.length > 0) {
      setBusinessType(data.items[0].businessType);
      return;
    }

    if (businessType !== null) return;

    tenantApi.getMyTenant()
      .then(({ data: tenant }) => setBusinessType(tenant.businessType))
      .catch(() => undefined);
  }, [businessType, data.items]);

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

  const handleStatusChange = async (order: OrderDto, newStatus: string) => {
    if (newStatus === '') return;
    try {
      await ordersApi.updateStatus(order.id, Number(newStatus) as OrderStatus);
      await loadOrders();
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.detail ||
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.error ||
        'No se pudo actualizar el estado.';
      setError(msg);
    }
  };

  const handleCancel = async (order: OrderDto) => {
    const reason = window.prompt('Motivo de cancelación (opcional):') ?? undefined;
    try {
      await ordersApi.cancel(order.id, reason || undefined);
      await loadOrders();
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.detail ||
        (err as { response?: { data?: { detail?: string; error?: string } } })?.response?.data?.error ||
        'No se pudo cancelar la orden.';
      setError(msg);
    }
  };

  return (
    <>
      <PageTitle title="Órdenes" subtitle="Operación" />

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              <div className="row mb-3">
                <div className="col-md-3 mb-2">
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
                    {(businessType === null ? STATUS_OPTIONS : BUSINESS_STATUS_OPTIONS[businessType]).map((status) => (
                      <option key={status.value} value={status.value}>
                        {status.label}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="col-md-3 mb-2">
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
                <div className="col-md-3 mb-2">
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
                <div className="col-md-3 mb-2 d-flex align-items-end">
                  <div className="d-flex gap-2">
                    <button
                      type="button"
                      className="btn btn-light btn-sm"
                      onClick={() => {
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
                      <span className="fw-semibold">{order.orderNumber}</span>
                      <div>
                        <span className={`badge mt-1 ${order.createdByBusiness ? 'bg-info' : 'bg-secondary'}`}>
                          {order.createdByBusiness ? 'Creada por empresa' : 'Creada por cliente'}
                        </span>
                      </div>
                    </td>
                    <td>
                      <div>{order.customerName}</div>
                      {order.customerPhone && <small className="text-muted">{order.customerPhone}</small>}
                    </td>
                    <td className="text-center">
                      <span className={`badge ${STATUS_BADGE_CLASS[order.status] ?? 'bg-secondary'}`}>
                        {getStatusLabel(order.status)}
                      </span>
                    </td>
                    <td className="text-center">{toLocalDateTime(order.createdAt)}</td>
                    <td className="text-end fw-semibold">${order.total.toFixed(2)}</td>
                    <td className="text-center">
                      <div className="d-flex gap-1 justify-content-center flex-wrap">
                        <button
                          className="btn btn-sm btn-soft-secondary"
                          onClick={() => setSelectedOrder(order)}
                          title="Ver detalle"
                        >
                          <i className="ri-eye-line"></i>
                        </button>
                        <select
                          className="form-select form-select-sm"
                          style={{ width: 145 }}
                          value=""
                          disabled={(order.allowedTransitions?.length ?? 0) === 0}
                          onChange={(e) => handleStatusChange(order, e.target.value)}
                        >
                          <option value="">Cambiar estado</option>
                          {STATUS_OPTIONS.filter((status) =>
                            (order.allowedTransitions ?? []).includes(status.value),
                          ).map((status) => (
                            <option key={status.value} value={status.value}>
                              {getStatusLabel(status.value)}
                            </option>
                          ))}
                        </select>
                        {Boolean(order.canCancel) && (
                          <button
                            className="btn btn-sm btn-soft-danger"
                            onClick={() => handleCancel(order)}
                            title="Cancelar orden"
                          >
                            <i className="ri-close-circle-line"></i>
                          </button>
                        )}
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
                  <p className="mb-1"><strong>Cliente:</strong> {selectedOrder.customerName}</p>
                  <p className="mb-1"><strong>Teléfono:</strong> {selectedOrder.customerPhone || '-'}</p>
                  <p className="mb-1"><strong>Mesa:</strong> {selectedOrder.tableNumber || '-'}</p>
                </div>
                <div className="col-md-6">
                  <p className="mb-1"><strong>Estado:</strong> {getStatusLabel(selectedOrder.status)}</p>
                  <p className="mb-1"><strong>Fecha:</strong> {toLocalDateTime(selectedOrder.createdAt)}</p>
                  <p className="mb-1"><strong>Items:</strong> {selectedOrderTotalItems}</p>
                  <p className="mb-1"><strong>Total:</strong> ${selectedOrder.total.toFixed(2)}</p>
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
    </>
  );
}
