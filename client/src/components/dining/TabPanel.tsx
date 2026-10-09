import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ordersApi } from '../../api/ordersApi';
import { getApiErrorMessage } from '../../api/errors';
import type { DiningTable } from '../../api/tablesApi';
import ChargePanel, { type ChargeResult } from '../sales/ChargePanel';
import SaleTicket from '../sales/SaleTicket';
import CancelTabItemForm from './CancelTabItemForm';
import { KitchenTicketStatus, type CancelTabItemRequest, type OrderDto, type OrderItemDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { describeItem } from '../../utils/productOptions';
import {
  activeQuantity,
  cancellableQuantity,
  chargedPrice,
  formatCancelled,
  formatTicketQuantity,
  printTicket,
} from '../../utils/ticket';
import { useToast } from '../../hooks/useToast';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';

interface TabPanelProps {
  orderId: string;
  /** Free tables the tab can move to. */
  freeTables: DiningTable[];
  businessName: string;
  onClose: () => void;
}

const ROUND_STATUS: Record<number, { label: string; className: string }> = {
  [KitchenTicketStatus.Pending]: { label: 'En cocina', className: 'ta-chip-new' },
  [KitchenTicketStatus.Preparing]: { label: 'Preparando', className: 'ta-chip-preparing' },
  [KitchenTicketStatus.Ready]: { label: 'Lista para llevar', className: 'ta-chip-ready' },
  [KitchenTicketStatus.Served]: { label: 'Servida', className: 'ta-chip-done' },
};

/** An open tab: its rounds, what is paid and what is left, and the actions of the table. */
export default function TabPanel({ orderId, freeTables, businessName, onClose }: TabPanelProps) {
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const { capabilities } = useTenant();
  // A waiter takes the rounds and cancels what the table no longer wants; the cashier charges the table.
  const access = getAccess(user, capabilities);
  const canCharge = access.charge;
  const [mode, setMode] = useState<'detail' | 'charge' | 'move' | 'bill' | 'cancel-item'>('detail');
  const [cancelling, setCancelling] = useState<OrderItemDto | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const tab = useQuery({
    queryKey: ['orders', orderId],
    queryFn: async () => (await ordersApi.getById(orderId)).data,
    refetchInterval: 15_000,
  });
  const order = tab.data;

  const refresh = async (updated?: OrderDto) => {
    if (updated) queryClient.setQueryData(['orders', orderId], updated);
    await queryClient.invalidateQueries({ queryKey: ['tables'] });
    await queryClient.invalidateQueries({ queryKey: ['orders', orderId] });
  };

  const run = async (action: () => Promise<OrderDto | void>, success?: string) => {
    setBusy(true);
    setError('');
    try {
      const updated = await action();
      if (success) toast.success(success);
      await refresh(updated ?? undefined);
      return true;
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo completar la acción.'));
      return false;
    } finally {
      setBusy(false);
    }
  };

  const charge = async (result: ChargeResult) => {
    const ok = await run(async () => {
      let updated: OrderDto | undefined;
      for (const payment of result.payments) {
        updated = (await ordersApi.addPayment(orderId, {
          amount: payment.amount,
          method: payment.method,
          reference: payment.reference,
          evidenceUrl: payment.evidenceUrl,
          tip: payment.tip,
          cashReceived: payment.cashReceived,
        })).data;
      }
      return updated;
    }, result.change > 0 ? `Cobrado. Cambio: ${formatMoney(result.change)}` : 'Cobro registrado.');
    if (ok) setMode('detail');
  };

  const cancelItem = async (item: OrderItemDto, request: Omit<CancelTabItemRequest, 'orderItemId'>) => {
    let updated: OrderDto | undefined;
    const ok = await run(async () => {
      updated = (await ordersApi.cancelTabItem(orderId, { orderItemId: item.id, ...request })).data;
      return updated;
    });
    if (ok && updated) {
      toast.success(`Platillo cancelado. La cuenta quedó en ${formatMoney(updated.total)}.`);
      setCancelling(null);
      setMode('detail');
    }
  };

  const closeTab = async () => {
    const ok = await run(async () => (await ordersApi.closeTab(orderId)).data, 'Cuenta cerrada. La mesa quedó libre.');
    if (ok) onClose();
  };

  const rounds = order
    ? [...new Set(order.items.map((item) => item.round ?? 1))].sort((a, b) => a - b)
    : [];
  const ticketOf = (round: number) => order?.kitchenTickets?.find((ticket) => ticket.round === round);
  const cancellationsOf = (item: OrderItemDto) => order?.itemCancellations?.filter((c) => c.orderItemId === item.id) ?? [];

  return (
    <Modal show onHide={onClose} centered size="lg" fullscreen="md-down" scrollable>
      <Modal.Header closeButton>
        <Modal.Title as="h2" className="h5">
          {order ? `${order.tableNumber || order.customerName} · ${order.orderNumber}` : 'Cuenta'}
        </Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {tab.isPending && <p className="text-muted">Cargando la cuenta…</p>}
        {tab.isError && <div className="alert alert-danger">{getApiErrorMessage(tab.error, 'No se pudo cargar la cuenta.')}</div>}
        {error && <div className="alert alert-danger">{error}</div>}

        {order && mode === 'detail' && (
          <>
            {rounds.map((round) => {
              const ticket = ticketOf(round);
              const status = ticket ? ROUND_STATUS[ticket.status] : null;
              return (
                <section key={round} className="mb-3">
                  <div className="d-flex align-items-center justify-content-between gap-2 mb-1">
                    <h3 className="h6 mb-0">Ronda {round}</h3>
                    <div className="d-flex align-items-center gap-2">
                      {status && <span className={`badge ${status.className}`}>{status.label}</span>}
                      {ticket?.status === KitchenTicketStatus.Ready && order.isOpenTab && (
                        <button
                          type="button"
                          className="btn btn-sm btn-success"
                          disabled={busy}
                          onClick={() => void run(async () => { await ordersApi.markRoundServed(ticket.id); }, `Ronda ${round} servida.`)}
                        >
                          Servida
                        </button>
                      )}
                    </div>
                  </div>
                  <ul className="list-unstyled border rounded p-2 mb-0">
                    {order.items.filter((item) => (item.round ?? 1) === round).map((item) => {
                      const cancelled = item.cancelledQuantity ?? 0;
                      const allCancelled = activeQuantity(item) <= 0;
                      return (
                        <li key={item.id} className="d-flex justify-content-between align-items-start gap-2 py-1">
                          <span className={allCancelled ? 'text-muted' : undefined}>
                            {allCancelled ? (
                              <s>{describeItem(item)}</s>
                            ) : (
                              <>
                                <span className="fw-semibold me-1">{formatTicketQuantity(item)}</span>
                                {describeItem(item)}
                              </>
                            )}
                            {item.notes && <span className="d-block small text-muted">{item.notes}</span>}
                            {cancelled > 0 && (
                              <span className="d-block small text-danger">
                                {allCancelled ? 'Cancelado' : formatCancelled(item)}
                                {': '}
                                {cancellationsOf(item).map((c) => c.reason).join(' · ')}
                              </span>
                            )}
                          </span>
                          <span className="d-flex align-items-center gap-2">
                            {!allCancelled && <span className="ta-money">{formatMoney(chargedPrice(item))}</span>}
                            {order.isOpenTab && access.sell && cancellableQuantity(item) > 0 && (
                              <button
                                type="button"
                                className="btn btn-sm btn-link text-danger p-0"
                                aria-label={`Cancelar ${describeItem(item)}`}
                                onClick={() => {
                                  setError('');
                                  setCancelling(item);
                                  setMode('cancel-item');
                                }}
                              >
                                Cancelar
                              </button>
                            )}
                          </span>
                        </li>
                      );
                    })}
                  </ul>
                </section>
              );
            })}

            <div className="border-top pt-2">
              <div className="d-flex justify-content-between"><span>Total</span><strong className="ta-money">{formatMoney(order.total)}</strong></div>
              {order.paidAmount > 0 && (
                <div className="d-flex justify-content-between text-muted"><span>Pagado</span><span className="ta-money">{formatMoney(order.paidAmount)}</span></div>
              )}
              <div className="d-flex justify-content-between fs-5">
                <span>Por cobrar</span><strong className="ta-money">{formatMoney(order.balance)}</strong>
              </div>
            </div>
            {!order.isOpenTab && <div className="alert alert-secondary mt-3 mb-0">Esta cuenta ya está cerrada.</div>}
          </>
        )}

        {order && mode === 'charge' && (
          <ChargePanel
            amountDue={order.balance}
            allowPartial
            submitting={busy}
            error={error}
            confirmLabel="Registrar cobro"
            onSubmit={(result) => void charge(result)}
            onCancel={() => setMode('detail')}
            uploadEvidence={async (file) => {
              const { data } = await ordersApi.uploadPaymentEvidence(file);
              return { url: data.url, fileName: data.fileName };
            }}
          />
        )}

        {order && mode === 'move' && (
          <>
            <p className="mb-2">¿A qué mesa pasa la cuenta?</p>
            {freeTables.length === 0 ? (
              <p className="text-muted">No hay mesas libres.</p>
            ) : (
              <div className="d-flex flex-wrap gap-2">
                {freeTables.map((table) => (
                  <button
                    key={table.id}
                    type="button"
                    className="btn btn-light btn-touch"
                    disabled={busy}
                    onClick={() => void run(async () => (await ordersApi.moveTab(orderId, table.id)).data, `La cuenta pasó a ${table.name}.`)
                      .then((ok) => ok && setMode('detail'))}
                  >
                    {table.name}
                  </button>
                ))}
              </div>
            )}
          </>
        )}

        {order && mode === 'bill' && (
          <SaleTicket order={order} businessName={businessName} />
        )}

        {order && mode === 'cancel-item' && cancelling && (
          <CancelTabItemForm
            item={cancelling}
            roundStatus={ticketOf(cancelling.round ?? 1)?.status}
            submitting={busy}
            onSubmit={(request) => void cancelItem(cancelling, request)}
            onBack={() => {
              setCancelling(null);
              setMode('detail');
            }}
          />
        )}
      </Modal.Body>
      {order && mode !== 'charge' && mode !== 'cancel-item' && (
        <Modal.Footer className="flex-wrap">
          {mode !== 'detail' ? (
            <>
              {mode === 'bill' && (
                <button type="button" className="btn btn-primary" onClick={printTicket}>
                  <i className="ri-printer-line me-1" aria-hidden="true"></i>Imprimir
                </button>
              )}
              <button type="button" className="btn btn-light" onClick={() => setMode('detail')}>Volver</button>
            </>
          ) : order.isOpenTab ? (
            <>
              {/* Secondary: Cobrar (or Cerrar cuenta) is the main action of the tab. */}
              <button
                type="button"
                className="btn btn-outline-primary btn-touch"
                onClick={() => navigate(`/sell?tab=${order.id}&label=${encodeURIComponent(order.tableNumber || order.customerName)}`)}
              >
                <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar ronda
              </button>
              <button type="button" className="btn btn-light btn-touch" onClick={() => setMode('bill')}>Ver cuenta</button>
              {order.diningTableId && (
                <button type="button" className="btn btn-light btn-touch" onClick={() => setMode('move')}>Cambiar de mesa</button>
              )}
              {order.balance > 0 ? (
                canCharge ? (
                  <button type="button" className="btn btn-primary btn-touch ms-md-auto" onClick={() => setMode('charge')}>
                    Cobrar {formatMoney(order.balance)}
                  </button>
                ) : (
                  <span className="small text-muted ms-md-auto align-self-center">
                    La caja cobra {formatMoney(order.balance)}.
                  </span>
                )
              ) : (
                <button type="button" className="btn btn-primary btn-touch ms-md-auto" disabled={busy} onClick={() => void closeTab()}>
                  Cerrar cuenta
                </button>
              )}
            </>
          ) : (
            <button type="button" className="btn btn-light" onClick={onClose}>Cerrar</button>
          )}
        </Modal.Footer>
      )}
    </Modal>
  );
}
