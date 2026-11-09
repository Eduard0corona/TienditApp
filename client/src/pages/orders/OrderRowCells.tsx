import { useEffect, useState } from 'react';
import ChannelBadge from '../../components/common/ChannelBadge';
import StatusChip from '../../components/common/StatusChip';
import type { OrderDto } from '../../types';
import { OrderChannel, OrderStatus as OrderStatusConst, OrderType as OrderTypeConst, PlatformCollectedBy } from '../../types';
import { isDeliveryPlatform } from '../../utils/deliveryPlatforms';
import { ORDER_TYPE_LABELS, getCollectedByLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import { confirmInTimeLabel } from '../../utils/orderConfirmInTime';
import { chargedByPlatform, isOnTheWay, toLocalDateTime } from './ordersListModel';

/**
 * By when an order of the online menu still waiting is confirmed in time (Fase 29X); it reads the clock every minute, so it
 * says the half hour passed with Pedidos open.
 */
function ConfirmInTime({ confirmBy }: { confirmBy: string }) {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 60_000);
    return () => window.clearInterval(timer);
  }, []);
  const { text, late } = confirmInTimeLabel(confirmBy, now);
  return <small className={`d-block ${late ? 'text-danger' : 'text-muted'}`}>{text}</small>;
}

interface OrderRowCellsProps {
  order: OrderDto;
  /** The list mixes sales and services and shows every kind: each order says its own. */
  showTypeColumn: boolean;
  canCharge: boolean;
  onOpen: (order: OrderDto) => void;
  onCharge: (order: OrderDto) => void;
}

/** The cells of an order in Pedidos: where it came from, who, its state, when, what is owed and its action. */
export default function OrderRowCells({ order, showTypeColumn, canCharge, onOpen, onCharge }: OrderRowCellsProps) {
  return (
    <>
      <td>
        <button
          type="button"
          className="btn btn-link p-0 fw-semibold text-decoration-none text-nowrap"
          onClick={() => onOpen(order)}
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
        {order.delivery && (
          <div>
            <span className="badge mt-1 text-bg-light border">
              <i className="ri-e-bike-2-line me-1" aria-hidden="true"></i>A domicilio
            </span>
            {isOnTheWay(order) && <span className="badge mt-1 ms-1 text-bg-info">En camino</span>}
          </div>
        )}
        {order.soldOnCredit && (
          <div>
            <span className="badge mt-1 bg-warning-subtle text-warning-emphasis">
              <i className="ri-hand-coin-line me-1" aria-hidden="true"></i>Fiado
            </span>
          </div>
        )}
        {isDeliveryPlatform(order.channel) && (
          <div>
            <ChannelBadge channel={order.channel} className="mt-1" />
            {order.platformOrder && (
              <small className="d-block text-muted">Folio {order.platformOrder.externalOrderNumber}</small>
            )}
            {order.platformOrder?.externalOrderId && (
              <small className="d-block text-muted text-break">ID {order.platformOrder.externalOrderId}</small>
            )}
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
      <td className="text-center">
        {toLocalDateTime(order.createdAt)}
        {order.confirmBy && <ConfirmInTime confirmBy={order.confirmBy} />}
      </td>
      <td className="text-end fw-semibold">
        <div className="ta-money">{formatMoney(order.total)}</div>
        <small className={`d-block ${order.balance > 0 ? 'text-danger' : 'text-success'}`}>
          {order.delivery?.awaitsCourierCash
            ? `Lo debe Paquetenvia ${formatMoney(order.balance)}`
            : order.balance > 0
            ? `Por cobrar ${formatMoney(order.balance)}`
            : chargedByPlatform(order)
              ? `Cobró ${getCollectedByLabel(PlatformCollectedBy.Platform, order.channel)}`
              : (order.writtenOffAmount ?? 0) > 0 ? 'Saldado' : 'Pagado'}
        </small>
        {(order.refundedAmount ?? 0) > 0 && (
          <small className="d-block text-warning">Reembolsado {formatMoney(order.refundedAmount ?? 0)}</small>
        )}
        {(order.writtenOffAmount ?? 0) > 0 && (
          <small className="d-block text-muted">Condonado {formatMoney(order.writtenOffAmount ?? 0)}</small>
        )}
      </td>
      <td className="text-center">
        {canCharge && order.balance > 0 && order.status !== OrderStatusConst.Cancelled ? (
          <div className="d-flex justify-content-center">
            <button
              type="button"
              className="btn btn-sm btn-soft-primary"
              onClick={() => onCharge(order)}
            >
              Cobrar
            </button>
          </div>
        ) : (
          <button
            type="button"
            className="btn btn-sm btn-light"
            onClick={() => onOpen(order)}
          >
            Ver detalle
          </button>
        )}
      </td>
    </>
  );
}
