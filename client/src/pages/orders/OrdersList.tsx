import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import StatusChip from '../../components/common/StatusChip';
import ChargePanel, { type ChargeResult } from '../../components/sales/ChargePanel';
import SaleTicket from '../../components/sales/SaleTicket';
import RefundOrderModal from '../../components/orders/RefundOrderModal';
import CfdiDraftModal from '../../components/orders/CfdiDraftModal';
import { allowsFractions, formatQuantity } from '../../utils/units';
import CustomerCardModal from '../../components/customers/CustomerCardModal';
import { ordersApi } from '../../api/ordersApi';
import { queryKeys } from '../../api/queries';
import { useArrivalChime } from '../../hooks/useArrivalChime';
import { getServicePriorityLabel, parseChecklist, parsePhotos } from '../../utils/serviceDetail';
import { useOrderSignalR } from '../../hooks/useOrderSignalR';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import type { OrderDto, OrderStatus, OrderType, PaginatedList, RefundOrderRequest, VoidOrderRequest } from '../../types';
import {
  OrderChannel,
  OrderStatus as OrderStatusConst,
  OrderType as OrderTypeConst,
  TenantCapability,
} from '../../types';
import { toPrivateFilePath } from '../../utils/privateFiles';
import PrivateFileLink from '../../components/common/PrivateFileLink';
import PrivatePhoto from '../../components/common/PrivatePhoto';
import { getAccess } from '../../utils/access';
import { hasAnyCapability, hasCapability } from '../../utils/tenantCapabilities';
import { formatMoney } from '../../utils/money';
import { getVatLabel } from '../../utils/orderTotals';
import { buildTicketText, buildWhatsAppTicketUrl, formatCancelled, printTicket } from '../../utils/ticket';
import {
  ORDER_TYPE_LABELS,
  SERVICE_PIECE_STATUS_LABELS,
  getOrderStatusLabel,
  getPaymentMethodLabel,
} from '../../utils/labels';
import { getApiErrorMessage } from '../../api/errors';
import { formatInBusinessZone, businessDayEnd, businessDayStart, getBusinessToday } from '../../utils/dates';

const ALL_STATUSES: OrderStatus[] = [0, 1, 2, 3, 4, 5, 6, 7, 99];

const toStatusOptions = (statuses: OrderStatus[]) =>
  statuses.map((value) => ({ value, label: getOrderStatusLabel(value) }));

const STATUS_OPTIONS = toStatusOptions(ALL_STATUSES);

const ORDER_TYPE_STATUS_OPTIONS: Record<OrderType, Array<{ value: OrderStatus; label: string }>> = {
  [OrderTypeConst.Retail]: toStatusOptions([0, 1, 2, 3, 6, 99]),
  [OrderTypeConst.Service]: toStatusOptions([0, 4, 5, 7, 99]),
};

// Where the order came from; the counter is the usual one and carries no badge in the list.
const CHANNEL_OPTIONS = [
  { value: OrderChannel.Counter, label: 'Mostrador' },
  { value: OrderChannel.WhatsApp, label: 'WhatsApp' },
  { value: OrderChannel.Online, label: 'En línea' },
];

/** The kinds of order of the business, by the rule the server lists them with: sales (products or food), services or both (both while unknown). */
function getOrderTypes(capabilities: number | null): OrderType[] {
  const sells = hasAnyCapability(capabilities, TenantCapability.Retail | TenantCapability.Food);
  const serves = hasCapability(capabilities, TenantCapability.Service);
  if (sells && !serves) return [OrderTypeConst.Retail];
  if (serves && !sells) return [OrderTypeConst.Service];
  return [OrderTypeConst.Retail, OrderTypeConst.Service];
}

const SERVICE_PIECE_STATUS_OPTIONS = ([0, 1, 2, 99] as const).map((value) => ({
  value,
  label: SERVICE_PIECE_STATUS_LABELS[value],
}));

const COLUMNS = [
  { header: 'Orden' },
  { header: 'Tipo', className: 'text-center' },
  { header: 'Cliente' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Fecha', className: 'text-center' },
  { header: 'Total', className: 'text-end' },
  { header: 'Acciones', className: 'text-center' },
];

function toLocalDateTime(date: string) {
  return formatInBusinessZone(date, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function getStatusLabel(status: OrderStatus): string {
  return getOrderStatusLabel(status);
}

// Several realtime events usually arrive together; one reload covers all of them.
const EVENT_RELOAD_DELAY_MS = 400;

export default function OrdersList() {
  const queryClient = useQueryClient();
  const today = getBusinessToday();
  const [searchParams] = useSearchParams();
  // A search from the global search (Ctrl K) or a link looks in every date, not only today.
  const initialSearch = searchParams.get('search')?.trim() ?? '';
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
  const [selectedChannel, setSelectedChannel] = useState('');
  const [fromDate, setFromDate] = useState(initialSearch ? '' : today);
  const [toDate, setToDate] = useState(initialSearch ? '' : today);
  const [search, setSearch] = useState(initialSearch);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [selectedOrder, setSelectedOrder] = useState<OrderDto | null>(null);
  // The realtime handlers read the open order without resubscribing on every selection.
  const selectedOrderIdRef = useRef<string | null>(null);
  useEffect(() => {
    selectedOrderIdRef.current = selectedOrder?.id ?? null;
  }, [selectedOrder]);
  const [cfdiFor, setCfdiFor] = useState<OrderDto | null>(null);
  const [paymentOrder, setPaymentOrder] = useState<OrderDto | null>(null);
  const [paymentError, setPaymentError] = useState('');
  const [paymentSubmitting, setPaymentSubmitting] = useState(false);
  const [paidReceipt, setPaidReceipt] = useState<{ order: OrderDto; change: number } | null>(null);
  const [realtimeMessage, setRealtimeMessage] = useState('');
  const [statusSubmitting, setStatusSubmitting] = useState(false);
  const [statusError, setStatusError] = useState('');
  const [refundTarget, setRefundTarget] = useState<{ order: OrderDto; mode: 'refund' | 'void' } | null>(null);
  const [refundSubmitting, setRefundSubmitting] = useState(false);
  const [refundError, setRefundError] = useState('');
  const [cardFor, setCardFor] = useState<{ phone: string; name: string } | null>(null);
  const { onOrderCreated, onOrderStatusChanged, onOrderCancelled, onConnected } = useOrderSignalR();
  const latestRequestRef = useRef(0);
  // Orders placed by customers (online menu) wait in Pending for the business: they ring on this device.
  const chime = useArrivalChime('tienditapp:orders-sound');
  const playChimeRef = useRef(chime.play);
  useEffect(() => {
    playChimeRef.current = chime.play;
  }, [chime.play]);
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const { prompt } = useConfirm();
  const toast = useToast();
  // Selling, charging, changing status and cancelling are counter actions (OrdersController); kitchen
  // roles can read orders but move food orders from the kitchen board. Refunds also need ProcessRefunds.
  const access = getAccess(user, capabilities);
  const canSell = access.sell;
  const canCharge = access.charge;
  const canRefund = access.refunds;
  // The kind of each order says something only while the list mixes sales and services.
  const orderTypes = getOrderTypes(capabilities);
  const mixesOrderTypes = orderTypes.length > 1;
  const showTypeColumn = mixesOrderTypes && selectedOrderType === '';
  const columns = useMemo(
    () => (showTypeColumn ? COLUMNS : COLUMNS.filter((column) => column.header !== 'Tipo')),
    [showTypeColumn],
  );
  // The states of the kind picked, or of the only kind the business has.
  const statusOrderType = selectedOrderType !== ''
    ? Number(selectedOrderType) as OrderType
    : (mixesOrderTypes ? null : orderTypes[0]);
  const statusOptions = statusOrderType === null ? STATUS_OPTIONS : ORDER_TYPE_STATUS_OPTIONS[statusOrderType];

  // Transitions come from the server's state machine; cancellation has its own action (it needs a reason
  // and is blocked while the order has payments).
  const selectedOrderStatusActions = useMemo(
    () => selectedOrder?.allowedTransitions.filter((status) => status !== OrderStatusConst.Cancelled) ?? [],
    [selectedOrder],
  );

  const selectedNetPaid = selectedOrder ? (selectedOrder.netPaidAmount ?? selectedOrder.paidAmount) : 0;
  const canRefundSelected = canRefund && selectedNetPaid > 0;
  const selectedOrderTotalItems = useMemo(
    // A weighed line counts as one article.
    () => selectedOrder?.items.reduce((acc, item) => acc + (allowsFractions(item.unit) ? 1 : item.quantity), 0) ?? 0,
    [selectedOrder],
  );
  const selectedOrderChecklist = useMemo(
    () => selectedOrder?.servicePieces.filter((piece) => piece.isTrackable) ?? [],
    [selectedOrder],
  );
  const selectedOrderIntakeChecklist = useMemo(
    () => parseChecklist(selectedOrder?.serviceDetail?.intakeChecklistJson),
    [selectedOrder],
  );
  const selectedOrderBeforePhotos = useMemo(
    () => parsePhotos(selectedOrder?.serviceDetail?.beforePhotosJson),
    [selectedOrder],
  );
  const selectedOrderAfterPhotos = useMemo(
    () => parsePhotos(selectedOrder?.serviceDetail?.afterPhotosJson),
    [selectedOrder],
  );
  // A silent reload (realtime event, reconnection) keeps the table on screen: no spinner, no lost scroll.
  const loadOrders = useCallback(async ({ silent = false }: { silent?: boolean } = {}) => {
    const requestId = ++latestRequestRef.current;
    if (!silent) setLoading(true);
    setError('');
    try {
      const params: NonNullable<Parameters<typeof ordersApi.getAll>[0]> = {
        pageNumber,
        pageSize,
      };

      if (selectedOrderType !== '') params.orderType = Number(selectedOrderType) as OrderType;
      if (selectedStatus !== '') params.status = Number(selectedStatus) as OrderStatus;
      if (selectedChannel !== '') params.channel = Number(selectedChannel) as OrderChannel;
      if (fromDate) params.fromDate = businessDayStart(fromDate);
      if (toDate) params.toDate = businessDayEnd(toDate);
      if (search) params.search = search;

      const { data: response } = await ordersApi.getAll(params);
      // A slower, older reload must not overwrite a newer one (filters changed meanwhile).
      if (requestId !== latestRequestRef.current) return;
      setData(response);
    } catch (err: unknown) {
      if (requestId !== latestRequestRef.current) return;
      setError(getApiErrorMessage(err, 'No se pudieron cargar los pedidos.'));
    } finally {
      if (requestId === latestRequestRef.current) setLoading(false);
    }
  }, [fromDate, pageNumber, pageSize, search, selectedChannel, selectedOrderType, selectedStatus, toDate]);

  const refreshSelectedOrder = useCallback(async (orderId: string) => {
    const { data: order } = await ordersApi.getById(orderId);
    // The detail may have been closed or switched while the request ran.
    if (selectedOrderIdRef.current === orderId) setSelectedOrder(order);
  }, []);

  useEffect(() => {
    loadOrders();
  }, [loadOrders]);

  useEffect(() => {
    // Several events usually arrive together (status change + payment): one silent reload covers them.
    let reloadTimeoutId: number | undefined;
    const scheduleReload = (orderId?: string) => {
      window.clearTimeout(reloadTimeoutId);
      reloadTimeoutId = window.setTimeout(() => void loadOrders({ silent: true }), EVENT_RELOAD_DELAY_MS);
      // The open detail follows its own order.
      if (orderId && orderId === selectedOrderIdRef.current) void refreshSelectedOrder(orderId).catch(() => undefined);
    };
    const createdOff = onOrderCreated((payload) => {
      if (payload.status === 'Pending') {
        setRealtimeMessage(`Nuevo pedido por confirmar: ${payload.orderNumber}`);
        playChimeRef.current();
      } else {
        setRealtimeMessage(`Nuevo pedido recibido: ${payload.orderNumber}`);
      }
      scheduleReload();
    });
    const changedOff = onOrderStatusChanged((payload) => {
      setRealtimeMessage(`Un pedido cambió a: ${getOrderStatusLabel(payload.newStatus)}`);
      scheduleReload(payload.orderId);
    });
    const cancelledOff = onOrderCancelled((payload) => {
      setRealtimeMessage(`Pedido cancelado${payload.reason ? `: ${payload.reason}` : ''}`);
      scheduleReload(payload.orderId);
    });
    // Events sent while the connection was down are lost: every (re)connection reloads.
    const connectedOff = onConnected(() => scheduleReload());

    return () => {
      window.clearTimeout(reloadTimeoutId);
      createdOff();
      changedOff();
      cancelledOff();
      connectedOff();
    };
  }, [loadOrders, onConnected, onOrderCancelled, onOrderCreated, onOrderStatusChanged, refreshSelectedOrder]);

  const openPaymentModal = (order: OrderDto) => {
    setPaymentOrder(order);
    setPaymentError('');
  };

  const closePaymentModal = () => {
    if (paymentSubmitting) return;
    setPaymentOrder(null);
    setPaymentError('');
  };

  const handleStatusChange = async (order: OrderDto, newStatus: OrderStatus) => {
    if (statusSubmitting) return;
    setStatusSubmitting(true);
    setStatusError('');
    try {
      await ordersApi.updateStatus(order.id, newStatus);
      // Confirming an online order takes it off the bell.
      void queryClient.invalidateQueries({ queryKey: queryKeys.attention });
      await Promise.all([loadOrders(), refreshSelectedOrder(order.id)]);
    } catch (err: unknown) {
      setStatusError(getApiErrorMessage(err, 'No se pudo actualizar el estado de la orden.'));
    } finally {
      setStatusSubmitting(false);
    }
  };

  const handleCancelOrder = async (order: OrderDto) => {
    if (statusSubmitting) return;
    const reason = await prompt({
      title: `Cancelar el pedido ${order.orderNumber}`,
      message: `El pedido de ${order.customerName} por ${formatMoney(order.total)} quedará cancelado y saldrá de la cocina o del taller.`,
      inputLabel: 'Motivo (opcional)',
      placeholder: 'Ej. el cliente ya no lo quiso',
      maxLength: 500,
      confirmLabel: 'Cancelar pedido',
      cancelLabel: 'No cancelar',
      danger: true,
    });
    if (reason === null) return;

    setStatusSubmitting(true);
    setStatusError('');
    try {
      await ordersApi.cancel(order.id, reason || undefined);
      void queryClient.invalidateQueries({ queryKey: queryKeys.attention });
      await Promise.all([loadOrders(), refreshSelectedOrder(order.id)]);
      toast.success(`Pedido ${order.orderNumber} cancelado.`);
    } catch (err: unknown) {
      setStatusError(getApiErrorMessage(err, 'No se pudo cancelar el pedido.'));
    } finally {
      setStatusSubmitting(false);
    }
  };

  const openRefundModal = (order: OrderDto, mode: 'refund' | 'void') => {
    setRefundTarget({ order, mode });
    setRefundError('');
  };

  const closeRefundModal = () => {
    if (refundSubmitting) return;
    setRefundTarget(null);
    setRefundError('');
  };

  // A refund never edits the sale: the server records it apart, takes cash from the cashier's drawer and
  // returns the units to inventory when asked.
  const handleRefund = async (request: { mode: 'refund'; data: RefundOrderRequest } | { mode: 'void'; data: VoidOrderRequest }) => {
    if (!refundTarget || refundSubmitting) return;
    const order = refundTarget.order;
    setRefundSubmitting(true);
    setRefundError('');
    try {
      const { data: updated } = request.mode === 'void'
        ? await ordersApi.voidOrder(order.id, request.data)
        : await ordersApi.refund(order.id, request.data);
      setRefundTarget(null);
      if (selectedOrder?.id === updated.id) setSelectedOrder(updated);
      toast.success(request.mode === 'void'
        ? `Venta ${order.orderNumber} anulada.`
        : `Reembolso de ${formatMoney(request.data.amount)} registrado en ${order.orderNumber}.`);
      await loadOrders();
    } catch (err: unknown) {
      setRefundError(getApiErrorMessage(err, request.mode === 'void' ? 'No se pudo anular la venta.' : 'No se pudo registrar el reembolso.'));
    } finally {
      setRefundSubmitting(false);
    }
  };

  // Payments are registered one by one (a mixed payment is several calls); the server closes the sale
  // with the payment that settles it.
  const handleAddPayment = async (charge: ChargeResult) => {
    if (!paymentOrder || paymentSubmitting) return;

    setPaymentSubmitting(true);
    setPaymentError('');
    let updated: OrderDto | null = null;
    try {
      for (const payment of charge.payments) {
        const { data } = await ordersApi.addPayment(paymentOrder.id, {
          amount: payment.amount,
          method: payment.method,
          reference: payment.reference,
          evidenceUrl: payment.evidenceUrl,
          tip: payment.tip,
          cashReceived: payment.cashReceived,
        });
        updated = data;
      }
      setPaymentOrder(null);
      if (updated) {
        // The change of this charge (the ticket adds up every payment of the order).
        setPaidReceipt({ order: updated, change: charge.change });
        if (selectedOrder?.id === updated.id) setSelectedOrder(updated);
      }
      await loadOrders();
    } catch (err: unknown) {
      setPaymentError(getApiErrorMessage(err, 'No se pudo registrar el pago.'));
      // Earlier payments of a mixed charge may already be saved: show the order as it is now.
      if (updated) {
        setPaymentOrder(updated);
        await loadOrders();
      }
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
      const msg = getApiErrorMessage(err, 'No se pudo actualizar el estado de la pieza.');
      setError(msg);
    }
  };

  return (
    <>
      <PageTitle title="Pedidos" />

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              {error && (
                <div className="alert alert-danger alert-dismissible fade show">
                  {error}
                  <button type="button" className="btn-close" onClick={() => setError('')}></button>
                </div>
              )}

              {realtimeMessage && (
                <div className="alert alert-info alert-dismissible fade show" role="status">
                  {realtimeMessage}
                  <button type="button" className="btn-close" aria-label="Cerrar" onClick={() => setRealtimeMessage('')}></button>
                </div>
              )}

              <DataTable
                data={data.items}
                columns={columns}
                keyExtractor={(order) => order.id}
                loading={loading}
                emptyMessage={search ? 'No hay pedidos que coincidan con la búsqueda.' : 'No hay pedidos en estas fechas.'}
                searchPlaceholder="Folio, cliente o teléfono"
                initialSearch={initialSearch}
                filters={(
                  <>
                    {mixesOrderTypes && (
                      <DataTableFilter label="Tipo" htmlFor="orderslist-tipo">
                        <select id="orderslist-tipo"
                          className="form-select form-select-sm"
                          value={selectedOrderType}
                          onChange={(e) => {
                            setSelectedOrderType(e.target.value);
                            setSelectedStatus('');
                            setPageNumber(1);
                          }}
                        >
                          <option value="">Todos</option>
                          {orderTypes.map((type) => (
                            <option key={type} value={type}>{ORDER_TYPE_LABELS[type]}</option>
                          ))}
                        </select>
                      </DataTableFilter>
                    )}
                    <DataTableFilter label="Estado" htmlFor="orderslist-estado">
                      <select id="orderslist-estado"
                        className="form-select form-select-sm"
                        value={selectedStatus}
                        onChange={(e) => {
                          setSelectedStatus(e.target.value);
                          setPageNumber(1);
                        }}
                      >
                        <option value="">Todos</option>
                        {statusOptions.map((status) => (
                          <option key={status.value} value={status.value}>
                            {status.label}
                          </option>
                        ))}
                      </select>
                    </DataTableFilter>
                    <DataTableFilter label="Canal" htmlFor="orderslist-canal">
                      <select id="orderslist-canal"
                        className="form-select form-select-sm"
                        value={selectedChannel}
                        onChange={(e) => {
                          setSelectedChannel(e.target.value);
                          setPageNumber(1);
                        }}
                      >
                        <option value="">Todos</option>
                        {CHANNEL_OPTIONS.map((channel) => (
                          <option key={channel.value} value={channel.value}>
                            {channel.label}
                          </option>
                        ))}
                      </select>
                    </DataTableFilter>
                    <DataTableFilter label="Desde" htmlFor="orderslist-desde">
                      <input id="orderslist-desde"
                        type="date"
                        className="form-control form-control-sm"
                        value={fromDate}
                        onChange={(e) => {
                          setFromDate(e.target.value);
                          setPageNumber(1);
                        }}
                      />
                    </DataTableFilter>
                    <DataTableFilter label="Hasta" htmlFor="orderslist-hasta">
                      <input id="orderslist-hasta"
                        type="date"
                        className="form-control form-control-sm"
                        value={toDate}
                        onChange={(e) => {
                          setToDate(e.target.value);
                          setPageNumber(1);
                        }}
                      />
                    </DataTableFilter>
                    <button
                      type="button"
                      className="btn btn-light btn-sm"
                      onClick={() => {
                        setSelectedOrderType('');
                        setSelectedStatus('');
                        setSelectedChannel('');
                        setFromDate(today);
                        setToDate(today);
                        setPageNumber(1);
                      }}
                    >
                      Limpiar filtros
                    </button>
                  </>
                )}
                toolbarEnd={(
                  <button
                    type="button"
                    className={`btn btn-sm ${chime.enabled ? 'btn-primary' : 'btn-light'}`}
                    onClick={chime.toggle}
                    aria-pressed={chime.enabled}
                    title="Suena cuando llega un pedido en línea por confirmar"
                  >
                    <i className={`${chime.enabled ? 'ri-volume-up-line' : 'ri-volume-mute-line'} me-1`} aria-hidden="true"></i>
                    {chime.enabled ? 'Sonido activado' : 'Sonido para pedidos en línea'}
                  </button>
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
                  onSearchChange: (term) => {
                    setSearch(term);
                    setPageNumber(1);
                  },
                }}
                renderRow={(order) => (
                  <>
                    <td>
                      <button
                        type="button"
                        className="btn btn-link p-0 fw-semibold text-decoration-none text-nowrap"
                        onClick={() => setSelectedOrder(order)}
                      >
                        {order.orderNumber}
                      </button>
                      {order.channel === OrderChannel.WhatsApp && (
                        <div>
                          <span className="badge mt-1 bg-success-subtle text-success-emphasis">
                            <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>WhatsApp
                          </span>
                        </div>
                      )}
                      {order.channel === OrderChannel.Online && (
                        <div>
                          <span className="badge mt-1 bg-primary-subtle text-primary-emphasis">En línea</span>
                        </div>
                      )}
                    </td>
                    {showTypeColumn && (
                      <td className="text-center">
                        <span className={`badge ${order.orderType === OrderTypeConst.Service ? 'bg-info' : 'bg-secondary'}`}>
                          {ORDER_TYPE_LABELS[order.orderType]}
                        </span>
                      </td>
                    )}
                    <td>
                      <div>{order.customerName}</div>
                      {order.customerPhone && <small className="text-muted">{order.customerPhone}</small>}
                    </td>
                    <td className="text-center">
                      <StatusChip status={order.status} />
                    </td>
                    <td className="text-center">{toLocalDateTime(order.createdAt)}</td>
                    <td className="text-end fw-semibold">
                      <div className="ta-money">{formatMoney(order.total)}</div>
                      <small className={`d-block ${order.balance > 0 ? 'text-danger' : 'text-success'}`}>
                        {order.balance > 0 ? `Por cobrar ${formatMoney(order.balance)}` : 'Pagado'}
                      </small>
                      {(order.refundedAmount ?? 0) > 0 && (
                        <small className="d-block text-warning">Reembolsado {formatMoney(order.refundedAmount ?? 0)}</small>
                      )}
                    </td>
                    <td className="text-center">
                      {canCharge && order.balance > 0 && order.status !== OrderStatusConst.Cancelled ? (
                        <div className="d-flex justify-content-center">
                          <button
                            type="button"
                            className="btn btn-sm btn-soft-primary"
                            onClick={() => openPaymentModal(order)}
                          >
                            Cobrar
                          </button>
                        </div>
                      ) : (
                        <button
                          type="button"
                          className="btn btn-sm btn-light"
                          onClick={() => setSelectedOrder(order)}
                        >
                          Ver detalle
                        </button>
                      )}
                    </td>
                  </>
                )}
              />
            </div>
          </div>
        </div>
      </div>

      <Modal
        show={!!selectedOrder}
        onHide={() => {
          setSelectedOrder(null);
          setStatusError('');
        }}
        size="lg"
        centered
      >
        <Modal.Header closeButton>
          <Modal.Title>Pedido {selectedOrder?.orderNumber}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {selectedOrder && (
            <>
              <div className="row mb-3">
                <div className="col-md-6">
                  <p className="mb-1"><strong>Orden:</strong> {selectedOrder.orderNumber}</p>
                  <p className="mb-1"><strong>Tipo:</strong> {ORDER_TYPE_LABELS[selectedOrder.orderType]}</p>
                  <p className="mb-1">
                    <strong>Cliente:</strong> {selectedOrder.customerName}
                    {selectedOrder.customerPhone && (
                      <button
                        type="button"
                        className="btn btn-link btn-sm p-0 ms-2 align-baseline"
                        onClick={() => setCardFor({ phone: selectedOrder.customerPhone ?? '', name: selectedOrder.customerName })}
                      >
                        Ver ficha
                      </button>
                    )}
                  </p>
                  <p className="mb-1"><strong>Teléfono:</strong> {selectedOrder.customerPhone || '-'}</p>
                  <p className="mb-1"><strong>Mesa:</strong> {selectedOrder.tableNumber || '-'}</p>
                </div>
                <div className="col-md-6">
                  <p className="mb-1"><strong>Estado:</strong> <StatusChip status={selectedOrder.status} /></p>
                  <p className="mb-1"><strong>Fecha:</strong> {toLocalDateTime(selectedOrder.createdAt)}</p>
                  <p className="mb-1"><strong>Artículos:</strong> {selectedOrderTotalItems}</p>
                  <p className="mb-1"><strong>Total:</strong> <span className="ta-money">{formatMoney(selectedOrder.total)}</span></p>
                  {selectedOrder.taxBreakdown.filter((line) => line.taxAmount > 0).map((line) => (
                    <p className="mb-1 small text-muted" key={`${line.ratePercent}-${line.isExempt}`}>
                      {selectedOrder.pricesIncludeTax ? 'Incluye ' : ''}{getVatLabel(line)}: {formatMoney(line.taxAmount)}
                    </p>
                  ))}
                  <p className="mb-1"><strong>Pagado:</strong> <span className="ta-money">{formatMoney(selectedOrder.paidAmount)}</span></p>
                  {(selectedOrder.refundedAmount ?? 0) > 0 && (
                    <p className="mb-1">
                      <strong>Reembolsado:</strong> <span className="ta-money text-warning">{formatMoney(selectedOrder.refundedAmount ?? 0)}</span>
                      <span className="text-muted small"> · neto {formatMoney(selectedNetPaid)}</span>
                    </p>
                  )}
                  <p className="mb-1"><strong>Por cobrar:</strong> <span className="ta-money">{formatMoney(selectedOrder.balance)}</span></p>
                </div>
              </div>

              {((canSell && (selectedOrderStatusActions.length > 0 || selectedOrder.canCancel))
                || (canCharge && selectedOrder.balance > 0)
                || canRefundSelected
                || (canSell && selectedOrder.status !== OrderStatusConst.Cancelled)) && (
                <div className="border rounded p-2 mb-3">
                  <div className="fw-semibold mb-2">Acciones</div>
                  <div className="d-flex flex-wrap gap-2">
                    {canCharge && selectedOrder.balance > 0 && selectedOrder.status !== OrderStatusConst.Cancelled && (
                      <button
                        type="button"
                        className="btn btn-sm btn-primary"
                        disabled={statusSubmitting}
                        onClick={() => openPaymentModal(selectedOrder)}
                      >
                        Cobrar {formatMoney(selectedOrder.balance)}
                      </button>
                    )}
                    {canSell && selectedOrderStatusActions.map((status) => (
                      <button
                        key={status}
                        type="button"
                        className="btn btn-sm btn-outline-primary"
                        disabled={statusSubmitting}
                        onClick={() => void handleStatusChange(selectedOrder, status)}
                      >
                        {getStatusLabel(status)}
                      </button>
                    ))}
                    {canSell && selectedOrder.canCancel && (
                      <button
                        type="button"
                        className="btn btn-sm btn-outline-danger"
                        disabled={statusSubmitting}
                        onClick={() => void handleCancelOrder(selectedOrder)}
                      >
                        Cancelar pedido
                      </button>
                    )}
                    {canSell && selectedOrder.status !== OrderStatusConst.Cancelled && (
                      <button
                        type="button"
                        className="btn btn-sm btn-outline-secondary"
                        onClick={() => setCfdiFor(selectedOrder)}
                      >
                        <i className="ri-file-text-line me-1" aria-hidden="true"></i>Factura (vista previa)
                      </button>
                    )}
                    {canRefundSelected && (
                      <>
                        <button
                          type="button"
                          className="btn btn-sm btn-outline-warning"
                          disabled={statusSubmitting}
                          onClick={() => openRefundModal(selectedOrder, 'refund')}
                        >
                          Reembolsar
                        </button>
                        <button
                          type="button"
                          className="btn btn-sm btn-outline-danger"
                          disabled={statusSubmitting}
                          onClick={() => openRefundModal(selectedOrder, 'void')}
                        >
                          Anular venta
                        </button>
                      </>
                    )}
                  </div>
                  {!selectedOrder.canCancel && selectedNetPaid > 0
                    && selectedOrder.allowedTransitions.includes(OrderStatusConst.Cancelled) && (
                    <small className="text-muted d-block mt-2">
                      {canRefund
                        ? 'Tiene pagos registrados: para cancelarla usa «Anular venta», que devuelve el dinero.'
                        : 'Tiene pagos registrados: solo quien puede hacer reembolsos puede anularla.'}
                    </small>
                  )}
                  {statusError && <div className="alert alert-danger mt-2 mb-0 py-2">{statusError}</div>}
                </div>
              )}

              <h5 className="mb-2">Items</h5>
              <div className="table-responsive">
                <table className="table table-sm table-striped mb-0">
                  <thead>
                    <tr>
                      <th>Producto</th>
                      <th className="text-center">Cantidad</th>
                      <th className="text-end">Precio</th>
                      <th className="text-end">Importe</th>
                    </tr>
                  </thead>
                  <tbody>
                    {selectedOrder.items.map((item) => (
                      <tr key={item.id}>
                        <td>
                          {item.productName}
                          {selectedOrder.isTab && <span className="badge bg-light text-dark ms-1">Ronda {item.round ?? 1}</span>}
                          {item.options && item.options.length > 0 && (
                            <div className="small text-primary-emphasis">{item.options.map((option) => option.optionName).join(', ')}</div>
                          )}
                          {item.notes && <div className="text-muted small">Nota: {item.notes}</div>}
                        </td>
                        <td className="text-center">
                          {formatQuantity(item.quantity, item.unit)}
                          {(item.returnedQuantity ?? 0) > 0 && (
                            <div className="text-warning small">
                              {formatQuantity(item.returnedQuantity ?? 0, item.unit)} devuelto{allowsFractions(item.unit) || item.returnedQuantity === 1 ? '' : 's'}
                            </div>
                          )}
                          {(item.cancelledQuantity ?? 0) > 0 && <div className="text-danger small">{formatCancelled(item)}</div>}
                        </td>
                        <td className="text-end">{formatMoney(item.unitPrice)}</td>
                        <td className="text-end">{formatMoney(item.chargedPrice ?? item.totalPrice)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {selectedOrder.serviceDetail && (
                <div className="mt-3">
                  <h5 className="mb-2">Recepción de servicio</h5>
                  <div className="row g-2">
                    <div className="col-md-6"><strong>Recibido:</strong> {toLocalDateTime(selectedOrder.serviceDetail.receivedAt)}</div>
                    <div className="col-md-6">
                      <strong>Promesa:</strong> {selectedOrder.serviceDetail.promisedAt
                        ? toLocalDateTime(selectedOrder.serviceDetail.promisedAt)
                        : '-'}
                    </div>
                    <div className="col-md-6"><strong>Prioridad:</strong> {getServicePriorityLabel(selectedOrder.serviceDetail.priority)}</div>
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
                      <strong>Checklist de recepción:</strong>
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
                              <PrivatePhoto
                                key={`${photo.path}-${index}`}
                                path={photo.path}
                                alt={photo.fileName || 'Foto antes'}
                                linked
                                className="border rounded"
                                style={{ width: 64, height: 64 }}
                              />
                            ))}
                          </div>
                        )}
                      </div>
                      <div className="col-md-6">
                        <strong>Fotos después</strong>
                        {selectedOrderAfterPhotos.length === 0 ? (
                          <div className="text-muted small">Sin fotos.</div>
                        ) : (
                          <div className="d-flex flex-wrap gap-2 mt-1">
                            {selectedOrderAfterPhotos.map((photo, index) => (
                              <PrivatePhoto
                                key={`${photo.path}-${index}`}
                                path={photo.path}
                                alt={photo.fileName || 'Foto después'}
                                linked
                                className="border rounded"
                                style={{ width: 64, height: 64 }}
                              />
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
                          <th className="text-end">Acción</th>
                        </tr>
                      </thead>
                      <tbody>
                        {selectedOrderChecklist.map((piece) => (
                          <tr key={piece.id}>
                            <td>{piece.itemName}</td>
                            <td>{piece.notifyCustomer ? 'Si' : 'No'}</td>
                            <td>{SERVICE_PIECE_STATUS_LABELS[piece.status] ?? piece.status}</td>
                            <td className="text-end" style={{ minWidth: 220 }}>
                              <select
                                className="form-select form-select-sm"
                                value=""
                                aria-label={`Actualizar estado de ${piece.itemName}`}
                                disabled={!canSell || getPieceAllowedTransitions(piece.status).length === 0}
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
                          <th>Forma de pago</th>
                          <th>Referencia</th>
                          <th>Comprobante</th>
                          <th className="text-end">Monto</th>
                        </tr>
                      </thead>
                      <tbody>
                        {selectedOrder.payments.map((payment) => (
                          <tr key={payment.id}>
                            <td>{toLocalDateTime(payment.paidAt)}</td>
                            <td>{getPaymentMethodLabel(payment.method)}</td>
                            <td>{payment.reference || '-'}</td>
                            <td>
                              {toPrivateFilePath(payment.evidenceUrl) ? (
                                <PrivateFileLink path={toPrivateFilePath(payment.evidenceUrl)!}>Ver</PrivateFileLink>
                              ) : '-'}
                            </td>
                            <td className="text-end">{formatMoney(payment.amount)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {(selectedOrder.refunds ?? []).length > 0 && (
                <div className="mt-3">
                  <h5 className="mb-2">Reembolsos</h5>
                  <div className="table-responsive">
                    <table className="table table-sm table-striped mb-0">
                      <thead>
                        <tr>
                          <th>Fecha</th>
                          <th>Forma</th>
                          <th>Motivo</th>
                          <th>Artículos</th>
                          <th className="text-end">Monto</th>
                        </tr>
                      </thead>
                      <tbody>
                        {(selectedOrder.refunds ?? []).map((refund) => (
                          <tr key={refund.id}>
                            <td>
                              {toLocalDateTime(refund.refundedAt)}
                              <div className="text-muted small">{refund.refundedBy}</div>
                            </td>
                            <td>{getPaymentMethodLabel(refund.method)}</td>
                            <td>{refund.reason}</td>
                            <td>
                              {refund.lines.length === 0 ? '-' : refund.lines.map((line) => {
                                const item = selectedOrder.items.find((candidate) => candidate.id === line.orderItemId);
                                return <div key={line.orderItemId}>{line.quantity} × {item?.productName ?? 'Artículo'}</div>;
                              })}
                            </td>
                            <td className="text-end text-warning">−{formatMoney(refund.amount)}</td>
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

      {cardFor && (
        <CustomerCardModal phone={cardFor.phone} name={cardFor.name} onClose={() => setCardFor(null)} />
      )}

      {cfdiFor && (
        <CfdiDraftModal orderId={cfdiFor.id} orderNumber={cfdiFor.orderNumber} onClose={() => setCfdiFor(null)} />
      )}

      {refundTarget && (refundTarget.mode === 'void' ? (
        <RefundOrderModal
          mode="void"
          order={refundTarget.order}
          submitting={refundSubmitting}
          error={refundError}
          onClose={closeRefundModal}
          onSubmit={(data) => void handleRefund({ mode: 'void', data })}
        />
      ) : (
        <RefundOrderModal
          mode="refund"
          order={refundTarget.order}
          submitting={refundSubmitting}
          error={refundError}
          onClose={closeRefundModal}
          onSubmit={(data) => void handleRefund({ mode: 'refund', data })}
        />
      ))}

      <Modal show={!!paymentOrder} onHide={closePaymentModal} centered size="lg" fullscreen="sm-down">
        <Modal.Header closeButton={!paymentSubmitting}>
          <Modal.Title as="h2" className="h5">
            Cobrar {paymentOrder?.orderNumber}
            <span className="d-block small text-muted fw-normal">{paymentOrder?.customerName}</span>
          </Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {paymentOrder && (
            <ChargePanel
              amountDue={paymentOrder.balance}
              allowPartial
              confirmLabel="Registrar"
              submitting={paymentSubmitting}
              error={paymentError}
              onSubmit={(charge) => void handleAddPayment(charge)}
              onCancel={closePaymentModal}
              uploadEvidence={async (file) => {
                const { data } = await ordersApi.uploadPaymentEvidence(file);
                return { url: data.url, fileName: data.fileName };
              }}
            />
          )}
        </Modal.Body>
      </Modal>

      <Modal show={!!paidReceipt} onHide={() => setPaidReceipt(null)} centered fullscreen="sm-down">
        <Modal.Header closeButton>
          <Modal.Title as="h2" className="h5">
            <i className="ri-checkbox-circle-fill text-success me-1" aria-hidden="true"></i>
            Pago registrado en {paidReceipt?.order.orderNumber}
          </Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {paidReceipt && (
            <>
              {paidReceipt.change > 0 && (
                <div className="charge-change has-change mb-3" aria-live="polite">
                  <span>Cambio</span>
                  <strong className="ta-money">{formatMoney(paidReceipt.change)}</strong>
                </div>
              )}
              <SaleTicket
                order={paidReceipt.order}
                businessName={tenant?.name ?? 'TienditApp'}
                logoUrl={tenant?.logoImages?.small ?? tenant?.logoUrl}
              />
            </>
          )}
        </Modal.Body>
        <Modal.Footer className="flex-wrap">
          <button type="button" className="btn btn-light btn-touch" onClick={printTicket}>
            <i className="ri-printer-line me-1" aria-hidden="true"></i>Imprimir
          </button>
          {paidReceipt && (
            <a
              className="btn btn-light btn-touch"
              href={buildWhatsAppTicketUrl(
                buildTicketText(paidReceipt.order, { businessName: tenant?.name ?? 'TienditApp' }),
                paidReceipt.order.customerPhone,
              )}
              target="_blank"
              rel="noopener noreferrer"
            >
              <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Enviar por WhatsApp
            </a>
          )}
          <button type="button" className="btn btn-primary btn-touch flex-grow-1" onClick={() => setPaidReceipt(null)} autoFocus>
            Listo
          </button>
        </Modal.Footer>
      </Modal>
    </>
  );
}

