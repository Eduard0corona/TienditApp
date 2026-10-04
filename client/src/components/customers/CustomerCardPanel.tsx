import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../../api/errors';
import { useCustomerCard } from '../../api/queries';
import type { Schemas } from '../../api/typedClient';
import StatusChip from '../common/StatusChip';
import { OrderChannel } from '../../types';
import { formatDateTime } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

interface CustomerCardPanelProps {
  phone?: string;
  customerId?: string;
  /** Shown while the card loads or when nothing is known yet. */
  fallbackName?: string;
  /** Starts a sale for this customer; omitted where it makes no sense. */
  onNewOrder?: (card: Schemas['CustomerCardDto']) => void;
  /** Hides the "open chat" link (already in the chat). */
  inChat?: boolean;
}

/** What the business knows about a customer: purchases, balance, favorites, appointments and opt-out. */
export default function CustomerCardPanel({ phone, customerId, fallbackName, onNewOrder, inChat = false }: CustomerCardPanelProps) {
  const { data: card, error, isPending } = useCustomerCard(phone, customerId);

  if (error) return <div className="alert alert-warning mb-0">{getApiErrorMessage(error, 'No se pudo cargar la ficha del cliente.')}</div>;
  if (isPending || !card) return <p className="text-muted mb-0">Cargando ficha…</p>;

  return (
    <div className="customer-card">
      <div className="d-flex justify-content-between align-items-start gap-2 mb-2">
        <div>
          <div className="fw-semibold fs-5">{card.name ?? fallbackName ?? 'Cliente'}</div>
          <div className="text-muted small">{card.phone ?? 'Sin teléfono'}{card.email ? ` · ${card.email}` : ''}</div>
        </div>
        {card.optedOut && (
          <span className="badge bg-secondary-subtle text-secondary-emphasis" title={`Desde ${formatDateTime(card.optedOutAt)}`}>
            No recibe campañas
          </span>
        )}
      </div>

      <div className="row g-2 mb-3 text-center">
        <div className="col-4">
          <div className="border rounded p-2 h-100">
            <div className="small text-muted">Pedidos</div>
            <div className="fw-semibold">{card.ordersCount}</div>
          </div>
        </div>
        <div className="col-4">
          <div className="border rounded p-2 h-100">
            <div className="small text-muted">Ha pagado</div>
            <div className="fw-semibold ta-money">{formatMoney(card.totalSpent)}</div>
          </div>
        </div>
        <div className="col-4">
          <div className={`border rounded p-2 h-100 ${card.balance > 0 ? 'border-danger' : ''}`}>
            <div className="small text-muted">Debe</div>
            <div className={`fw-semibold ta-money ${card.balance > 0 ? 'text-danger' : ''}`}>{formatMoney(card.balance)}</div>
          </div>
        </div>
      </div>
      {card.ordersCount > 0 && (
        <p className="small text-muted mb-3">
          Ticket promedio {formatMoney(card.averageTicket)} · cliente desde {formatDateTime(card.firstOrderAt)} · última compra {formatDateTime(card.lastOrderAt)}
        </p>
      )}

      <div className="d-flex flex-wrap gap-2 mb-3">
        {onNewOrder && (
          <button type="button" className="btn btn-primary btn-sm" onClick={() => onNewOrder(card)}>
            <i className="ri-shopping-basket-2-line me-1" aria-hidden="true"></i>Nuevo pedido
          </button>
        )}
        {!inChat && card.phone && (
          <Link className="btn btn-light btn-sm" to={`/whatsapp/chat?phone=${encodeURIComponent(card.phone)}`}>
            <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>{card.conversationId ? 'Abrir chat' : 'Escribir por WhatsApp'}
          </Link>
        )}
      </div>

      {(card.favoriteProducts ?? []).length > 0 && (
        <div className="mb-3">
          <div className="fw-semibold small mb-1">Lo que más pide</div>
          <div className="d-flex flex-wrap gap-1">
            {(card.favoriteProducts ?? []).map((product) => (
              <span key={product.productId} className="badge bg-primary-subtle text-primary-emphasis">
                {product.productName} × {product.quantity}
              </span>
            ))}
          </div>
        </div>
      )}

      {(card.upcomingAppointments ?? []).length > 0 && (
        <div className="mb-3">
          <div className="fw-semibold small mb-1">Próximas citas</div>
          <ul className="list-unstyled small mb-0">
            {(card.upcomingAppointments ?? []).map((appointment) => (
              <li key={appointment.id}>{formatDateTime(appointment.startAt)} · {appointment.serviceName}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="fw-semibold small mb-1">Últimos pedidos</div>
      {(card.recentOrders ?? []).length === 0 ? (
        <p className="small text-muted mb-0">Todavía no tiene pedidos.</p>
      ) : (
        <ul className="list-unstyled small mb-0">
          {(card.recentOrders ?? []).map((order) => (
            <li key={order.id} className="d-flex justify-content-between align-items-center gap-2 border-bottom py-1">
              <span>
                <Link to={`/orders?search=${encodeURIComponent(order.orderNumber)}`}>{order.orderNumber}</Link>
                {order.channel === OrderChannel.WhatsApp && <i className="ri-whatsapp-line text-success ms-1" title="Pedido por WhatsApp" aria-label="Pedido por WhatsApp"></i>}
                <span className="text-muted d-block">{formatDateTime(order.createdAt)}</span>
              </span>
              <span className="text-end">
                <StatusChip status={order.status} />
                <span className="d-block ta-money">{formatMoney(order.total)}</span>
                {order.balance > 0 && <span className="d-block text-danger">Debe {formatMoney(order.balance)}</span>}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
