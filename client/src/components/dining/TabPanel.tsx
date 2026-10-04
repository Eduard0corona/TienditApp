import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ordersApi } from '../../api/ordersApi';
import { getApiErrorMessage } from '../../api/errors';
import type { DiningTable } from '../../api/tablesApi';
import ChargePanel, { type ChargeResult } from '../sales/ChargePanel';
import SaleTicket from '../sales/SaleTicket';
import { KitchenTicketStatus, type OrderDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { describeItem } from '../../utils/productOptions';
import { formatTicketQuantity, printTicket } from '../../utils/ticket';
import { useToast } from '../../hooks/useToast';

interface TabPanelProps {
  orderId: string;
  /** Free tables the tab can move to. */
  freeTables: DiningTable[];
  businessName: string;
  onClose: () => void;
}

const ROUND_STATUS: Record<number, { label: string; className: string }> = {
  [KitchenTicketStatus.Pending]: { label: 'En cocina', className: 'bg-warning text-dark' },
  [KitchenTicketStatus.Preparing]: { label: 'Preparando', className: 'bg-primary' },
  [KitchenTicketStatus.Ready]: { label: 'Lista para llevar', className: 'bg-success' },
  [KitchenTicketStatus.Served]: { label: 'Servida', className: 'bg-light text-dark' },
};

/** An open tab: its rounds, what is paid and what is left, and the actions of the table. */
export default function TabPanel({ orderId, freeTables, businessName, onClose }: TabPanelProps) {
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<'detail' | 'charge' | 'move' | 'bill'>('detail');
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
        })).data;
      }
      return updated;
    }, result.change > 0 ? `Cobrado. Cambio: ${formatMoney(result.change)}` : 'Cobro registrado.');
    if (ok) setMode('detail');
  };

  const closeTab = async () => {
    const ok = await run(async () => (await ordersApi.closeTab(orderId)).data, 'Cuenta cerrada. La mesa quedó libre.');
    if (ok) onClose();
  };

  const rounds = order
    ? [...new Set(order.items.map((item) => item.round ?? 1))].sort((a, b) => a - b)
    : [];
  const ticketOf = (round: number) => order?.kitchenTickets?.find((ticket) => ticket.round === round);

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
                    {order.items.filter((item) => (item.round ?? 1) === round).map((item) => (
                      <li key={item.id} className="d-flex justify-content-between gap-2 py-1">
                        <span>
                          <span className="fw-semibold me-1">{formatTicketQuantity(item)}</span>
                          {describeItem(item)}
                          {item.notes && <span className="d-block small text-muted">{item.notes}</span>}
                        </span>
                        <span className="ta-money">{formatMoney(item.totalPrice)}</span>
                      </li>
                    ))}
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
      </Modal.Body>
      {order && mode !== 'charge' && (
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
              <button
                type="button"
                className="btn btn-primary btn-touch"
                onClick={() => navigate(`/sell?tab=${order.id}&label=${encodeURIComponent(order.tableNumber || order.customerName)}`)}
              >
                <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar ronda
              </button>
              <button type="button" className="btn btn-light btn-touch" onClick={() => setMode('bill')}>Ver cuenta</button>
              {order.diningTableId && (
                <button type="button" className="btn btn-light btn-touch" onClick={() => setMode('move')}>Cambiar de mesa</button>
              )}
              {order.balance > 0 ? (
                <button type="button" className="btn btn-success btn-touch ms-md-auto" onClick={() => setMode('charge')}>
                  Cobrar {formatMoney(order.balance)}
                </button>
              ) : (
                <button type="button" className="btn btn-success btn-touch ms-md-auto" disabled={busy} onClick={() => void closeTab()}>
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
