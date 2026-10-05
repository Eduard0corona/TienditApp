import { useCallback, useEffect, useRef, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import { useFocusMode } from '../../components/layout/focusMode';
import { kitchenApi } from '../../api/kitchenApi';
import { getApiErrorMessage } from '../../api/errors';
import { useArrivalChime } from '../../hooks/useArrivalChime';
import { useOrderSignalR } from '../../hooks/useOrderSignalR';
import { KitchenTicketStatus, OrderStatus } from '../../types';
import type { KitchenOrderDto, KitchenTicketDto, OrderItemDto } from '../../types';
import { getOrderStatusLabel } from '../../utils/labels';
import { formatTicketQuantity } from '../../utils/ticket';

function KitchenItems({ items }: { items: OrderItemDto[] }) {
  return (
    <ul className="list-unstyled border rounded p-2 mb-3">
      {items.map((item) => (
        <li key={item.id} className="py-1">
          <span className="fw-bold fs-5 me-1">{formatTicketQuantity(item)}</span>
          <span className="fs-5">{item.productName}</span>
          {item.options && item.options.length > 0 && (
            <div className="fw-semibold text-primary-emphasis">{item.options.map((option) => option.optionName).join(' · ')}</div>
          )}
          {item.notes && (
            <div className="alert alert-warning py-1 px-2 mt-1 mb-0 small">{item.notes}</div>
          )}
        </li>
      ))}
    </ul>
  );
}

const TICKET_STATUS_LABELS: Record<number, string> = {
  [KitchenTicketStatus.Pending]: 'Nueva',
  [KitchenTicketStatus.Preparing]: 'Preparando',
  [KitchenTicketStatus.Ready]: 'Lista',
};

// Realtime events are the main signal; polling covers a dropped or blocked connection, and every
// (re)connection reloads because events sent while it was down are lost.
const POLL_INTERVAL_MS = 30_000;
// Several events usually arrive together (status change + payment); one reload covers all of them.
const EVENT_RELOAD_DELAY_MS = 400;
const CLOCK_TICK_MS = 30_000;
const WARNING_AFTER_MINUTES = 10;
const LATE_AFTER_MINUTES = 20;

function getElapsedMinutes(createdAt: string, now: number): number {
  const created = new Date(createdAt).getTime();
  return Number.isNaN(created) ? 0 : Math.max(0, Math.floor((now - created) / 60_000));
}

function formatElapsed(minutes: number): string {
  if (minutes < 1) return 'Recién llegada';
  if (minutes < 60) return `Hace ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  return `Hace ${hours} h ${minutes % 60} min`;
}

function getAgeClass(minutes: number): string {
  if (minutes >= LATE_AFTER_MINUTES) return 'border-danger';
  if (minutes >= WARNING_AFTER_MINUTES) return 'border-warning';
  return 'border-light';
}

function getAgeBadgeClass(minutes: number): string {
  if (minutes >= LATE_AFTER_MINUTES) return 'bg-danger';
  if (minutes >= WARNING_AFTER_MINUTES) return 'bg-warning text-dark';
  return 'bg-light text-dark';
}

export default function KitchenBoard() {
  const [orders, setOrders] = useState<KitchenOrderDto[]>([]);
  const [tickets, setTickets] = useState<KitchenTicketDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [actionError, setActionError] = useState('');
  const [processingOrderId, setProcessingOrderId] = useState('');
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const latestRequestRef = useRef(0);
  const { onOrderCreated, onOrderStatusChanged, onOrderCancelled, onKitchenTicketsChanged, onConnected } = useOrderSignalR();
  const chime = useArrivalChime('tienditapp:kitchen-sound');
  const focus = useFocusMode();
  // Orders and new rounds already on the board; null until the first load (which must not ring).
  const knownTicketIdsRef = useRef<Set<string> | null>(null);
  const playChimeRef = useRef(chime.play);
  useEffect(() => {
    playChimeRef.current = chime.play;
  }, [chime.play]);

  const loadOrders = useCallback(async () => {
    const requestId = ++latestRequestRef.current;
    try {
      // Orders and the rounds of open tabs (each round is its own ticket).
      const [{ data }, { data: rounds }] = await Promise.all([kitchenApi.getOrders(), kitchenApi.getTickets()]);
      // A slower, older reload must not overwrite a newer one.
      if (requestId !== latestRequestRef.current) return;
      const arrivals = new Set([
        ...data.map((order) => order.id),
        ...rounds.filter((ticket) => ticket.status === KitchenTicketStatus.Pending).map((ticket) => ticket.id),
      ]);
      const known = knownTicketIdsRef.current;
      if (known && [...arrivals].some((id) => !known.has(id))) playChimeRef.current();
      knownTicketIdsRef.current = arrivals;
      setOrders(data);
      setTickets(rounds);
      setLoadError('');
      setLastUpdated(new Date());
      setNow(Date.now());
    } catch (err: unknown) {
      if (requestId !== latestRequestRef.current) return;
      setLoadError(getApiErrorMessage(err, 'No se pudieron cargar las comandas. Se reintentará en unos segundos.'));
    } finally {
      if (requestId === latestRequestRef.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    let reloadTimeoutId: number | undefined;
    const scheduleReload = () => {
      window.clearTimeout(reloadTimeoutId);
      reloadTimeoutId = window.setTimeout(() => void loadOrders(), EVENT_RELOAD_DELAY_MS);
    };

    void loadOrders();
    const pollId = window.setInterval(() => void loadOrders(), POLL_INTERVAL_MS);
    const clockId = window.setInterval(() => setNow(Date.now()), CLOCK_TICK_MS);
    const unsubscribers = [
      onOrderCreated(scheduleReload),
      onOrderStatusChanged(scheduleReload),
      onOrderCancelled(scheduleReload),
      onKitchenTicketsChanged(scheduleReload),
      onConnected(scheduleReload),
    ];

    return () => {
      window.clearTimeout(reloadTimeoutId);
      window.clearInterval(pollId);
      window.clearInterval(clockId);
      unsubscribers.forEach((unsubscribe) => unsubscribe());
    };
  }, [loadOrders, onOrderCreated, onOrderStatusChanged, onOrderCancelled, onKitchenTicketsChanged, onConnected]);

  const runAction = async (orderId: string, action: () => Promise<unknown>, fallback: string) => {
    setProcessingOrderId(orderId);
    setActionError('');
    try {
      await action();
      await loadOrders();
    } catch (err: unknown) {
      setActionError(getApiErrorMessage(err, fallback));
      await loadOrders();
    } finally {
      setProcessingOrderId('');
    }
  };

  const handleStart = (orderId: string) =>
    runAction(orderId, () => kitchenApi.startPreparation(orderId), 'No se pudo iniciar la preparación.');

  const handleReady = (orderId: string) =>
    runAction(orderId, () => kitchenApi.markReady(orderId), 'No se pudo marcar la comanda como lista.');

  const moveTicket = (ticket: KitchenTicketDto) => {
    if (ticket.status === KitchenTicketStatus.Pending) {
      return runAction(ticket.id, () => kitchenApi.startTicket(ticket.id), 'No se pudo iniciar la ronda.');
    }
    if (ticket.status === KitchenTicketStatus.Preparing) {
      return runAction(ticket.id, () => kitchenApi.markTicketReady(ticket.id), 'No se pudo marcar la ronda como lista.');
    }
    return runAction(ticket.id, () => kitchenApi.markTicketServed(ticket.id), 'No se pudo marcar la ronda como entregada.');
  };
  const pendingCount = orders.length + tickets.filter((ticket) => ticket.status !== KitchenTicketStatus.Ready).length;

  return (
    <>
      <PageTitle title="Cocina" subtitle="Operación" />

      <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-3">
        <div className="text-muted small" aria-live="polite">
          {lastUpdated
            ? `${pendingCount} ${pendingCount === 1 ? 'comanda' : 'comandas'} · actualizado a las ${lastUpdated.toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}`
            : 'Cargando comandas…'}
        </div>
        <div className="d-flex flex-wrap gap-2">
          <button
            type="button"
            className={`btn btn-sm ${chime.enabled ? 'btn-primary' : 'btn-light'}`}
            onClick={chime.toggle}
            aria-pressed={chime.enabled}
            title="Suena cuando llega una comanda nueva"
          >
            <i className={`${chime.enabled ? 'ri-volume-up-line' : 'ri-volume-mute-line'} me-1`} aria-hidden="true"></i>
            {chime.enabled ? 'Sonido activado' : 'Activar sonido'}
          </button>
          <button type="button" className="btn btn-sm btn-light" onClick={() => void loadOrders()}>
            <i className="ri-refresh-line me-1" aria-hidden="true"></i>Actualizar
          </button>
          {/* Kitchen screens and TVs show the board alone: full screen, dark, with large buttons. */}
          {!focus.isFocused && (
            <button type="button" className="btn btn-sm btn-light" onClick={focus.enter}>
              <i className="ri-fullscreen-line me-1" aria-hidden="true"></i>Modo enfocado
            </button>
          )}
        </div>
      </div>

      {chime.needsTouch && (
        <div className="alert alert-info py-2 small" role="status">
          Toca cualquier parte de la pantalla para que el sonido de comandas nuevas pueda sonar.
        </div>
      )}

      {actionError && (
        <div className="alert alert-danger alert-dismissible fade show" role="alert">
          {actionError}
          <button type="button" className="btn-close" aria-label="Cerrar" onClick={() => setActionError('')}></button>
        </div>
      )}

      {loadError && (
        <div className="alert alert-warning" role="status">
          {loadError}
        </div>
      )}

      {loading ? (
        <div className="text-center py-5">
          <div className="spinner-border text-primary" role="status">
            <span className="visually-hidden">Cargando…</span>
          </div>
        </div>
      ) : orders.length === 0 && tickets.length === 0 ? (
        <div className="card">
          <div className="card-body text-center py-5 text-muted">
            No hay comandas pendientes. Las nuevas aparecerán aquí solas.
          </div>
        </div>
      ) : (
        <div className="row">
          {orders.map((order) => {
            const elapsed = getElapsedMinutes(order.createdAt, now);
            const isPreparing = order.status === OrderStatus.InPreparation;
            const isProcessing = processingOrderId === order.id;

            return (
              <div className="col-xxl-3 col-xl-4 col-md-6" key={order.id}>
                <div className={`card border border-2 ${getAgeClass(elapsed)}`}>
                  <div className="card-body">
                    <div className="d-flex justify-content-between align-items-start gap-2 mb-2">
                      <div>
                        <h4 className="mb-1">{order.orderNumber}</h4>
                        <span className={`badge ${getAgeBadgeClass(elapsed)}`}>{formatElapsed(elapsed)}</span>
                      </div>
                      <span className={`badge ${isPreparing ? 'bg-primary' : 'bg-warning text-dark'}`}>
                        {getOrderStatusLabel(order.status)}
                      </span>
                    </div>

                    <div className="mb-2">
                      <div>
                        <strong>Cliente:</strong> {order.customerName}
                      </div>
                      {order.tableNumber && (
                        <div>
                          <strong>Mesa:</strong> {order.tableNumber}
                        </div>
                      )}
                    </div>

                    {order.customerNotes && (
                      <div className="alert alert-warning py-1 px-2 mb-2">
                        <strong>Nota:</strong> {order.customerNotes}
                      </div>
                    )}

                    <KitchenItems items={order.items} />

                    <div className="d-flex gap-2">
                      <button
                        type="button"
                        className="btn btn-primary flex-fill"
                        onClick={() => void handleStart(order.id)}
                        disabled={isProcessing || isPreparing}
                      >
                        {isPreparing ? 'Preparando' : 'Iniciar'}
                      </button>
                      <button
                        type="button"
                        className="btn btn-success flex-fill"
                        onClick={() => void handleReady(order.id)}
                        disabled={isProcessing}
                      >
                        Lista
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            );
          })}
          {tickets.map((ticket) => {
            const elapsed = getElapsedMinutes(ticket.sentAt, now);
            const isProcessing = processingOrderId === ticket.id;
            const isReady = ticket.status === KitchenTicketStatus.Ready;
            return (
              <div className="col-xxl-3 col-xl-4 col-md-6" key={ticket.id}>
                <div className={`card border border-2 ${isReady ? 'border-success' : getAgeClass(elapsed)}`}>
                  <div className="card-body">
                    <div className="d-flex justify-content-between align-items-start gap-2 mb-2">
                      <div>
                        <h4 className="mb-1">{ticket.tableNumber || ticket.customerName}</h4>
                        <div className="small text-muted">Ronda {ticket.round} · {ticket.orderNumber}</div>
                        {!isReady && <span className={`badge ${getAgeBadgeClass(elapsed)}`}>{formatElapsed(elapsed)}</span>}
                      </div>
                      <span className={`badge ${isReady ? 'bg-success' : ticket.status === KitchenTicketStatus.Preparing ? 'bg-primary' : 'bg-warning text-dark'}`}>
                        {TICKET_STATUS_LABELS[ticket.status]}
                      </span>
                    </div>
                    {ticket.customerNotes && (
                      <div className="alert alert-warning py-1 px-2 mb-2">
                        <strong>Nota:</strong> {ticket.customerNotes}
                      </div>
                    )}
                    <KitchenItems items={ticket.items} />
                    <button
                      type="button"
                      className={`btn w-100 ${isReady ? 'btn-light' : ticket.status === KitchenTicketStatus.Preparing ? 'btn-success' : 'btn-primary'}`}
                      onClick={() => void moveTicket(ticket)}
                      disabled={isProcessing}
                    >
                      {ticket.status === KitchenTicketStatus.Pending ? 'Iniciar' : isReady ? 'Entregada a la mesa' : 'Lista'}
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </>
  );
}
