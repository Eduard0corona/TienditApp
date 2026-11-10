import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import ChimeSettingsButton from '../../components/common/ChimeSettingsButton';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import type { ChargeResult } from '../../components/sales/ChargePanel';
import RefundOrderModal from '../../components/orders/RefundOrderModal';
import CfdiDraftModal from '../../components/orders/CfdiDraftModal';
import CustomerCardModal from '../../components/customers/CustomerCardModal';
import { ordersApi } from '../../api/ordersApi';
import { useRefreshCustomerCredit } from '../../api/customerCredit';
import { queryKeys } from '../../api/queries';
import { useOrderChannels } from './useOrderChannels';
import { useArrivalChime } from '../../hooks/useArrivalChime';
import { useOrderSignalR } from '../../hooks/useOrderSignalR';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import type { OrderDto, OrderStatus, OrderType, PaginatedList, RefundOrderRequest, VoidOrderRequest } from '../../types';
import { OrderChannel, OrderStatus as OrderStatusConst } from '../../types';
import { getAccess } from '../../utils/access';
import { formatMoney } from '../../utils/money';
import { isDeliveryPlatform } from '../../utils/deliveryPlatforms';
import { ORDER_TYPE_LABELS, getChannelLabel, getOrderStatusLabel } from '../../utils/labels';
import { getApiErrorMessage } from '../../api/errors';
import { businessDayEnd, businessDayStart, getBusinessToday } from '../../utils/dates';
import { ORDERS_SOUND_KEY } from '../../hooks/useOnlineOrdersWaiting';
import OrderRowCells from './OrderRowCells';
import OrderDetailModal from './OrderDetailModal';
import OrderPaymentModal from './OrderPaymentModal';
import PaidReceiptModal from './PaidReceiptModal';
import { ORDER_TYPE_STATUS_OPTIONS, STATUS_OPTIONS, getChannelOptions, getOrderTypes, withItsQuote } from './ordersListModel';

const COLUMNS = [
  { header: 'Orden' },
  { header: 'Tipo', className: 'text-center' },
  { header: 'Cliente' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Fecha', className: 'text-center' },
  { header: 'Total', className: 'text-end' },
  { header: 'Acciones', className: 'text-center' },
];

// Several realtime events usually arrive together; one reload covers all of them.
const EVENT_RELOAD_DELAY_MS = 400;

/** The link of the bell's online orders to confirm (Fase 29X). */
const TO_CONFIRM_LINK = 'por-confirmar';

export default function OrdersList() {
  const queryClient = useQueryClient();
  const today = getBusinessToday();
  const [searchParams, setSearchParams] = useSearchParams();
  // A search from the global search (Ctrl K) or a link looks in every date, not only today.
  const initialSearch = searchParams.get('search')?.trim() ?? '';
  // The bell's online orders to confirm (Fase 29X) open the ones still waiting, of any day.
  const linkedStatus = searchParams.get('estado');
  const toConfirm = linkedStatus === TO_CONFIRM_LINK;
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
  const [selectedStatus, setSelectedStatus] = useState(toConfirm ? String(OrderStatusConst.Pending) : '');
  const [selectedChannel, setSelectedChannel] = useState('');
  const [fromDate, setFromDate] = useState(initialSearch || toConfirm ? '' : today);
  const [toDate, setToDate] = useState(initialSearch || toConfirm ? '' : today);
  const [search, setSearch] = useState(initialSearch);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  // The bell leads here also from this screen: a link followed again opens the ones waiting, of any day.
  const [seenLink, setSeenLink] = useState(linkedStatus);
  if (seenLink !== linkedStatus) {
    setSeenLink(linkedStatus);
    if (linkedStatus === TO_CONFIRM_LINK) {
      setSelectedStatus(String(OrderStatusConst.Pending));
      setFromDate('');
      setToDate('');
      setPageNumber(1);
    }
  }
  // A filter set by hand drops the one the link named: the bell sets it again, and a reload keeps these.
  const dropLink = () => {
    if (linkedStatus)
      setSearchParams((params) => {
        params.delete('estado');
        return params;
      }, { replace: true });
  };
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
  const { onOrderCreated, onOrderStatusChanged, onOrderCancelled, onOrderUpdated, onConnected } = useOrderSignalR();
  const latestRequestRef = useRef(0);
  // Orders placed by customers (online menu) wait in Pending for the business: they ring on this device.
  const chime = useArrivalChime(ORDERS_SOUND_KEY);
  const playChimeRef = useRef(chime.play);
  useEffect(() => {
    playChimeRef.current = chime.play;
  }, [chime.play]);
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const { confirm, prompt } = useConfirm();
  const toast = useToast();
  const refreshCredit = useRefreshCustomerCredit();
  // Selling, charging, changing status and cancelling are counter actions (OrdersController); kitchen
  // roles can read orders but move food orders from the kitchen board. Refunds also need ProcessRefunds.
  // The list only charges; the detail says which of the others the person may use.
  const access = getAccess(user, capabilities);
  const canCharge = access.charge;
  const orderChannels = useOrderChannels(access.orders);
  const channelOptions = useMemo(() => getChannelOptions(orderChannels.data?.channels ?? []), [orderChannels.data]);
  // Filtering by channel only says something when the business receives orders by more than one.
  const showChannelFilter = channelOptions.length > 1;
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

  const openOrder = (order: OrderDto) => {
    selectedOrderIdRef.current = order.id;
    setSelectedOrder(order);
    // The list does not say what became of each «En línea» refund nor the dispute of the payment (Fases 24I and 24K):
    // the detail of an order paid online reads the whole order.
    if (order.channel === OrderChannel.Online) void refreshSelectedOrder(order.id).catch(() => undefined);
  };

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
      if (payload.externalOrderNumber) {
        // An order of a delivery platform, registered by the team: named by its platform and its folio.
        setRealtimeMessage(`Nuevo pedido ${payload.orderNumber} de ${getChannelLabel(payload.channel)} (folio ${payload.externalOrderNumber})`);
      } else if (payload.status === 'Pending') {
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
      if (payload.byCustomer) {
        // Fase 29R: the customer cancelled it before it was accepted; it rings down, as it rang when it arrived, and the
        // bell stops counting it.
        setRealtimeMessage(`El cliente canceló ${payload.orderNumber ? `el pedido ${payload.orderNumber}` : 'un pedido'} antes de que lo aceptaran.`);
        playChimeRef.current('leaving');
        void queryClient.invalidateQueries({ queryKey: queryKeys.attention });
      } else {
        setRealtimeMessage(`Pedido cancelado${payload.reason ? `: ${payload.reason}` : ''}`);
      }
      scheduleReload(payload.orderId);
    });
    // The trip written down or the order sent out with the courier (Fase 22D): read again, without a message.
    const updatedOff = onOrderUpdated((payload) => scheduleReload(payload.orderId));
    // Events sent while the connection was down are lost: every (re)connection reloads.
    const connectedOff = onConnected(() => scheduleReload());

    return () => {
      window.clearTimeout(reloadTimeoutId);
      createdOff();
      changedOff();
      cancelledOff();
      updatedOff();
      connectedOff();
    };
  }, [loadOrders, onConnected, onOrderCancelled, onOrderCreated, onOrderStatusChanged, onOrderUpdated, queryClient, refreshSelectedOrder]);

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
    // A platform's app often cancels once the food is cooked: then its supplies were used, and nothing goes back unless
    // the person says the food was never made (as when voiding one).
    const restockItems = isDeliveryPlatform(order.channel)
      ? await confirm({
        title: '¿Regresar los insumos al inventario?',
        message: 'Solo si la comida no se preparó. Si ya estaba lista, los insumos ya se usaron.',
        confirmLabel: 'Sí, regresarlos',
        cancelLabel: 'No, ya se preparó',
      })
      : undefined;
    // Its customer called or wrote to cancel an order of the online menu (Fase 31B): whoever gets the emails of the cancelled
    // orders hears of it. Closing the question says no. One ordered with the QR of a table is handled by whoever serves it.
    const atCustomerRequest = order.channel === OrderChannel.Online && !order.tableNumber
      ? await confirm({
        title: '¿Lo pidió el cliente?',
        message: 'Si llamó o escribió para cancelarlo, le llega un correo a quien recibe los pedidos cancelados.',
        confirmLabel: 'Sí, lo pidió',
        cancelLabel: 'No',
      })
      : undefined;

    setStatusSubmitting(true);
    setStatusError('');
    try {
      await ordersApi.cancel(order.id, { reason: reason || undefined, restockItems, atCustomerRequest });
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
      if (selectedOrder?.id === updated.id) setSelectedOrder(withItsQuote(updated, selectedOrder));
      toast.success(request.mode === 'void'
        ? `Venta ${order.orderNumber} anulada.`
        : `Reembolso de ${formatMoney(request.data.amount)} registrado en ${order.orderNumber}.`);
      // A refund (or a void) to the balance in the customer's favor changes their account, card and what Vender offers.
      if (order.customerPhone) refreshCredit();
      // Not awaited: a refund opened while the list is read again does not wait in «Guardando…» for it.
      void loadOrders();
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
        if (selectedOrder?.id === updated.id) setSelectedOrder(withItsQuote(updated, selectedOrder));
      }
      // Not awaited: a charge opened while the list is read again does not wait in «Cobrando…» for it.
      void loadOrders();
    } catch (err: unknown) {
      setPaymentError(getApiErrorMessage(err, 'No se pudo registrar el pago.'));
      // Earlier payments of a mixed charge may already be saved: show the order as it is now.
      if (updated) {
        setPaymentOrder(updated);
        void loadOrders();
      }
    } finally {
      setPaymentSubmitting(false);
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
                emptyMessage={search
                  ? 'No hay pedidos que coincidan con la búsqueda.'
                  : fromDate || toDate ? 'No hay pedidos en estas fechas.' : 'No hay pedidos con estos filtros.'}
                searchPlaceholder="Folio, cliente o teléfono"
                initialSearch={initialSearch}
                filterValues={[selectedOrderType, selectedStatus, selectedChannel, fromDate, toDate]}
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
                            dropLink();
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
                          dropLink();
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
                    {showChannelFilter && (
                      <DataTableFilter label="Canal" htmlFor="orderslist-canal">
                        <select id="orderslist-canal"
                          className="form-select form-select-sm"
                          value={selectedChannel}
                          onChange={(e) => {
                            setSelectedChannel(e.target.value);
                            dropLink();
                            setPageNumber(1);
                          }}
                        >
                          <option value="">Todos</option>
                          {channelOptions.map((channel) => (
                            <option key={channel.value} value={channel.value}>
                              {channel.label}
                            </option>
                          ))}
                        </select>
                      </DataTableFilter>
                    )}
                    <DataTableFilter label="Desde" htmlFor="orderslist-desde">
                      <input id="orderslist-desde"
                        type="date"
                        className="form-control form-control-sm"
                        value={fromDate}
                        onChange={(e) => {
                          setFromDate(e.target.value);
                          dropLink();
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
                          dropLink();
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
                        dropLink();
                        setPageNumber(1);
                      }}
                    >
                      Limpiar filtros
                    </button>
                  </>
                )}
                toolbarEnd={(
                  <>
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
                    <ChimeSettingsButton />
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
                  onSearchChange: (term) => {
                    setSearch(term);
                    setPageNumber(1);
                  },
                }}
                renderRow={(order) => (
                  <OrderRowCells
                    order={order}
                    showTypeColumn={showTypeColumn}
                    canCharge={canCharge}
                    onOpen={openOrder}
                    onCharge={openPaymentModal}
                  />
                )}
              />
            </div>
          </div>
        </div>
      </div>

      <OrderDetailModal
        order={selectedOrder}
        access={access}
        capabilities={capabilities}
        statusSubmitting={statusSubmitting}
        statusError={statusError}
        onClose={() => {
          setSelectedOrder(null);
          setStatusError('');
        }}
        onCharge={openPaymentModal}
        onChangeStatus={(order, status) => void handleStatusChange(order, status)}
        onCancelOrder={(order) => void handleCancelOrder(order)}
        onInvoice={setCfdiFor}
        onRefund={openRefundModal}
        onOpenCard={setCardFor}
        onPieceStatusChange={(orderId, pieceId, status) => void handleServicePieceStatusChange(orderId, pieceId, status)}
        onReload={(orderId) => Promise.all([loadOrders(), refreshSelectedOrder(orderId)])}
      />

      {cardFor && (
        <CustomerCardModal phone={cardFor.phone} name={cardFor.name} onClose={() => setCardFor(null)} />
      )}

      {cfdiFor && (
        <CfdiDraftModal
          orderId={cfdiFor.id}
          orderNumber={cfdiFor.orderNumber}
          canCancel={access.businessSettings}
          canSaveToCustomer={access.customers}
          orderCancelled={cfdiFor.status === OrderStatusConst.Cancelled}
          onClose={() => setCfdiFor(null)}
        />
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

      <OrderPaymentModal
        order={paymentOrder}
        submitting={paymentSubmitting}
        error={paymentError}
        onClose={closePaymentModal}
        onSubmit={(charge) => void handleAddPayment(charge)}
      />

      <PaidReceiptModal receipt={paidReceipt} onClose={() => setPaidReceipt(null)} />
    </>
  );
}

