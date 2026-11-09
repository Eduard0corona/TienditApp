import { useId, useMemo } from 'react';
import { Modal } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import StatusChip from '../../components/common/StatusChip';
import OrderDeliveryPanel from '../../components/orders/OrderDeliveryPanel';
import PlatformOrderIdEditor from '../../components/orders/PlatformOrderIdEditor';
import WorkAppointmentList from '../../components/appointments/WorkAppointmentList';
import { useToast } from '../../hooks/useToast';
import type { OrderDto, OrderStatus } from '../../types';
import { OrderStatus as OrderStatusConst, PlatformCollectedBy, TenantCapability } from '../../types';
import type { Access } from '../../utils/access';
import { isDeliveryPlatform } from '../../utils/deliveryPlatforms';
import { ORDER_TYPE_LABELS, getChannelLabel, getCollectedByLabel, getOrderStatusLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import { getVatLabel } from '../../utils/orderTotals';
import { quoteFolio } from '../../utils/quotes';
import { hasCapability } from '../../utils/tenantCapabilities';
import { formatCancelled } from '../../utils/ticket';
import { allowsFractions, formatQuantity } from '../../utils/units';
import OrderMoneyRecords from './OrderMoneyRecords';
import OrderServiceSection from './OrderServiceSection';
import { toLocalDateTime } from './ordersListModel';

interface OrderDetailModalProps {
  /** The order open; none closes the detail. */
  order: OrderDto | null;
  access: Access;
  capabilities: number | null;
  /** A change of state or a cancellation is being saved: its buttons wait. */
  statusSubmitting: boolean;
  statusError: string;
  onClose: () => void;
  onCharge: (order: OrderDto) => void;
  onChangeStatus: (order: OrderDto, status: OrderStatus) => void;
  onCancelOrder: (order: OrderDto) => void;
  onInvoice: (order: OrderDto) => void;
  onRefund: (order: OrderDto, mode: 'refund' | 'void') => void;
  onOpenCard: (customer: { phone: string; name: string }) => void;
  onPieceStatusChange: (orderId: string, pieceId: string, newStatus: string) => void;
  /** Reads the list and the open order again after something saved from the detail (the trip, the platform's ID). */
  onReload: (orderId: string) => Promise<unknown>;
}

/**
 * The detail of an order in Pedidos: who and what, its actions and its records. Pedidos keeps what each action does; the
 * detail only says which ones the person may use on this order.
 */
export default function OrderDetailModal({
  order,
  access,
  capabilities,
  statusSubmitting,
  statusError,
  onClose,
  onCharge,
  onChangeStatus,
  onCancelOrder,
  onInvoice,
  onRefund,
  onOpenCard,
  onPieceStatusChange,
  onReload,
}: OrderDetailModalProps) {
  const titleId = useId();
  const toast = useToast();
  // Selling, charging, changing status and cancelling are counter actions (OrdersController). Refunds also need ProcessRefunds.
  const canSell = access.sell;
  const canCharge = access.charge;
  const canRefund = access.refunds;

  // Transitions come from the server's state machine; cancellation has its own action (it needs a reason
  // and is blocked while the order has payments).
  const statusActions = useMemo(
    () => order?.allowedTransitions.filter((status) => status !== OrderStatusConst.Cancelled) ?? [],
    [order],
  );
  const netPaid = order ? (order.netPaidAmount ?? order.paidAmount) : 0;
  const platform = order && isDeliveryPlatform(order.channel) ? order.channel : null;
  const canRefundOrder = canRefund && netPaid > 0;
  const totalItems = useMemo(
    // A weighed line counts as one article.
    () => order?.items.reduce((acc, item) => acc + (allowsFractions(item.unit) ? 1 : item.quantity), 0) ?? 0,
    [order],
  );

  return (
    <Modal
      show={!!order}
      onHide={onClose}
      size="lg"
      centered
      aria-labelledby={titleId}
    >
      <Modal.Header closeButton>
        <Modal.Title id={titleId}>Pedido {order?.orderNumber}</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {order && (
          <>
            <div className="row mb-3">
              <div className="col-md-6">
                <p className="mb-1"><strong>Orden:</strong> {order.orderNumber}</p>
                <p className="mb-1"><strong>Tipo:</strong> {ORDER_TYPE_LABELS[order.orderType]}</p>
                <p className="mb-1">
                  <strong>Cliente:</strong> {order.customerName}
                  {order.customerPhone && !platform && (
                    <button
                      type="button"
                      className="btn btn-link btn-sm p-0 ms-2 align-baseline"
                      onClick={() => onOpenCard({ phone: order.customerPhone ?? '', name: order.customerName })}
                    >
                      Ver ficha
                    </button>
                  )}
                </p>
                {platform ? (
                  // The app of a platform shares neither the customer's phone nor a table: where it came from instead.
                  <>
                    <p className="mb-1">
                      <strong>Origen:</strong> {getChannelLabel(platform)}
                      {order.platformOrder && <> · folio {order.platformOrder.externalOrderNumber}</>}
                    </p>
                    {order.platformOrder && (
                      <PlatformOrderIdEditor
                        key={order.id}
                        order={order}
                        platform={platform}
                        canManage={canSell}
                        onSaved={async (cleared) => {
                          // Saved already: reading the order again failing is not a failed save.
                          toast.success(cleared ? 'ID largo quitado.' : 'ID largo guardado.');
                          await onReload(order.id).catch(() => undefined);
                        }}
                      />
                    )}
                    {order.platformOrder && (
                      <p className="mb-1">
                        <strong>Cobró:</strong>{' '}
                        {order.platformOrder.collectedBy === PlatformCollectedBy.Business
                          ? 'el negocio'
                          : getCollectedByLabel(PlatformCollectedBy.Platform, platform)}
                      </p>
                    )}
                  </>
                ) : (
                  <>
                    <p className="mb-1"><strong>Teléfono:</strong> {order.customerPhone || '-'}</p>
                    <p className="mb-1"><strong>Mesa:</strong> {order.tableNumber || '-'}</p>
                  </>
                )}
              </div>
              <div className="col-md-6">
                <p className="mb-1"><strong>Estado:</strong> <StatusChip status={order.status} /></p>
                <p className="mb-1"><strong>Fecha:</strong> {toLocalDateTime(order.createdAt)}</p>
                <p className="mb-1"><strong>Artículos:</strong> {totalItems}</p>
                <p className="mb-1"><strong>Total:</strong> <span className="ta-money">{formatMoney(order.total)}</span></p>
                {order.visitCharge && (
                  <p className="mb-1 small">
                    Incluye el cargo por visita a domicilio ({order.visitCharge.zoneName}):{' '}
                    <span className="ta-money">{formatMoney(order.visitCharge.fee)}</span>
                  </p>
                )}
                {order.quote && (
                  <p className="mb-1 small">
                    De la cotización{' '}
                    <Link to={`/quotes/${order.quote.id}`}>{quoteFolio(order.quote.number, order.quote.revision)}</Link>
                  </p>
                )}
                {/* The appointments that do its job (Fase 29F); they are agendadas from its quote. */}
                {order.workAppointments && order.workAppointments.length > 0 && (
                  <div className="mb-2">
                    <WorkAppointmentList appointments={order.workAppointments} canOpenAgenda={access.appointments} />
                  </div>
                )}
                {order.taxBreakdown.filter((line) => line.taxAmount > 0).map((line) => (
                  <p className="mb-1 small text-muted" key={`${line.ratePercent}-${line.isExempt}`}>
                    {order.pricesIncludeTax ? 'Incluye ' : ''}{getVatLabel(line)}: {formatMoney(line.taxAmount)}
                  </p>
                ))}
                <p className="mb-1"><strong>Pagado:</strong> <span className="ta-money">{formatMoney(order.paidAmount)}</span></p>
                {(order.refundedAmount ?? 0) > 0 && (
                  <p className="mb-1">
                    <strong>Reembolsado:</strong> <span className="ta-money text-warning">{formatMoney(order.refundedAmount ?? 0)}</span>
                    <span className="text-muted small"> · neto {formatMoney(netPaid)}</span>
                  </p>
                )}
                {(order.writtenOffAmount ?? 0) > 0 && (
                  <p className="mb-1"><strong>Condonado:</strong> <span className="ta-money">{formatMoney(order.writtenOffAmount ?? 0)}</span></p>
                )}
                <p className="mb-1"><strong>Por cobrar:</strong> <span className="ta-money">{formatMoney(order.balance)}</span></p>
              </div>
            </div>

            <OrderDeliveryPanel
              key={order.id}
              order={order}
              canManage={canSell}
              onSaved={async (change) => {
                await onReload(order.id);
                toast.success(change === 'dispatch' ? 'Salida guardada.' : 'Envío guardado.');
              }}
            />

            {/* Whoever sells always has «Factura», also on a cancelled order (its invoices). */}
            {(canSell || (canCharge && order.balance > 0) || canRefundOrder) && (
              <div className="border rounded p-2 mb-3">
                <div className="fw-semibold mb-2">Acciones</div>
                <div className="d-flex flex-wrap gap-2">
                  {canCharge && order.balance > 0 && order.status !== OrderStatusConst.Cancelled && (
                    <button
                      type="button"
                      className="btn btn-sm btn-primary"
                      disabled={statusSubmitting}
                      onClick={() => onCharge(order)}
                    >
                      Cobrar {formatMoney(order.balance)}
                    </button>
                  )}
                  {canSell && statusActions.map((status) => (
                    <button
                      key={status}
                      type="button"
                      className="btn btn-sm btn-outline-primary"
                      disabled={statusSubmitting}
                      onClick={() => onChangeStatus(order, status)}
                    >
                      {getOrderStatusLabel(status)}
                    </button>
                  ))}
                  {canSell && order.canCancel && (
                    <button
                      type="button"
                      className="btn btn-sm btn-outline-danger"
                      disabled={statusSubmitting}
                      onClick={() => onCancelOrder(order)}
                    >
                      Cancelar pedido
                    </button>
                  )}
                  {/* A cancelled order is not invoiced, but its invoices stay reachable to download or cancel them. */}
                  {canSell && (
                    <button
                      type="button"
                      className="btn btn-sm btn-outline-secondary"
                      onClick={() => onInvoice(order)}
                    >
                      <i className="ri-file-text-line me-1" aria-hidden="true"></i>Factura
                    </button>
                  )}
                  {canRefundOrder && (
                    <>
                      <button
                        type="button"
                        className="btn btn-sm btn-outline-warning"
                        disabled={statusSubmitting}
                        onClick={() => onRefund(order, 'refund')}
                      >
                        Reembolsar
                      </button>
                      <button
                        type="button"
                        className="btn btn-sm btn-outline-danger"
                        disabled={statusSubmitting}
                        onClick={() => onRefund(order, 'void')}
                      >
                        Anular venta
                      </button>
                    </>
                  )}
                </div>
                {!order.canCancel && netPaid > 0
                  && order.allowedTransitions.includes(OrderStatusConst.Cancelled) && (
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
                  {order.items.map((item) => (
                    <tr key={item.id}>
                      <td>
                        {item.productName}
                        {order.isTab && <span className="badge bg-light text-dark ms-1">Ronda {item.round ?? 1}</span>}
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

            <OrderServiceSection
              order={order}
              servesOrders={hasCapability(capabilities, TenantCapability.Service)}
              canSell={canSell}
              onPieceStatusChange={onPieceStatusChange}
            />

            <OrderMoneyRecords order={order} />

            {order.customerNotes && (
              <div className="mt-3">
                <strong>Notas del cliente:</strong>
                <div className="text-muted">{order.customerNotes}</div>
              </div>
            )}

            {order.cancellationReason && (
              <div className="mt-3">
                <strong>Motivo de cancelación:</strong>
                <div className="text-danger">{order.cancellationReason}</div>
              </div>
            )}
          </>
        )}
      </Modal.Body>
    </Modal>
  );
}
