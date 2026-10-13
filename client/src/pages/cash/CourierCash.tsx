import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import {
  useCourierReceivables,
  useCourierSettlement,
  useCourierSettlements,
  usePendingCourierOrders,
  type CourierReceivable,
  type CourierSettlementDetail,
} from '../../api/courierSettlements';
import { useCurrentCashSession } from '../../api/queries';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import { useToast } from '../../hooks/useToast';
import { DeliveryCourier, OrderPaymentMethod } from '../../types';
import { describeCollection, stillToCollect } from '../../utils/courierCash';
import { formatDateTime } from '../../utils/dates';
import { DELIVERY_COURIER_LABELS } from '../../utils/labels';
import { formatMoney, formatSignedMoney } from '../../utils/money';
import CourierSettlementModal from './CourierSettlementModal';
import { ADJUSTMENT_KIND_LABELS } from './courierSettlementForm';

const PAGE_SIZE = 10;

interface CourierCashProps {
  /** Charging orders as well as handling the cash: whoever has both settles. */
  canSettle: boolean;
  /** Takes the person to Mi caja, to open their drawer. */
  onOpenDrawer: () => void;
}

/**
 * Caja › Repartidores (Fase 22D): what Paquetenvia owes for the orders it collected in cash and delivered, what it carries on
 * its way, its settlements, and «Liquidar», which charges those orders in the drawer of whoever receives the cash.
 */
export default function CourierCash({ canSettle, onOpenDrawer }: CourierCashProps) {
  const toast = useToast();
  const courier = DeliveryCourier.Paquetenvia;
  const courierName = DELIVERY_COURIER_LABELS[courier];
  const receivables = useCourierReceivables();
  const pending = usePendingCourierOrders(courier);
  const drawer = useCurrentCashSession(canSettle);
  const [page, setPage] = useState(1);
  const settlements = useCourierSettlements(page, PAGE_SIZE);
  const [settling, setSettling] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);

  const receivable: CourierReceivable | undefined = receivables.data?.find((row) => row.courier === courier);
  const owed = pending.data?.owed ?? [];
  const onTheWay = pending.data?.onTheWay ?? [];
  const ownDrawer = drawer.data?.isMine === true;

  if (receivables.isLoading || pending.isLoading) return <LoadingSpinner />;
  if (receivables.isError || pending.isError) {
    return <div className="alert alert-danger" role="alert">No se pudo leer lo que deben los repartidores. Intenta de nuevo.</div>;
  }

  return (
    <>
      <section className="card mb-3" aria-labelledby="courier-owes">
        <div className="card-body">
          <h2 id="courier-owes" className="h5">{courierName}</h2>
          <div className="d-flex flex-wrap gap-4 mb-2">
            <div>
              <div className="text-muted small">Te debe</div>
              <div className="fs-4 fw-bold ta-money">{formatMoney(receivable?.owedAmount ?? 0)}</div>
              <div className="small text-muted">
                {receivable?.owedOrders ? `${receivable.owedOrders} ${receivable.owedOrders === 1 ? 'pedido entregado' : 'pedidos entregados'}` : 'Nada pendiente'}
                {receivable?.oldestDispatchedAt && ` · el más antiguo salió ${formatDateTime(receivable.oldestDispatchedAt)}`}
              </div>
            </div>
            <div>
              <div className="text-muted small">En camino</div>
              <div className="fs-4 fw-bold ta-money">{formatMoney(receivable?.onTheWayAmount ?? 0)}</div>
              <div className="small text-muted">
                {receivable?.onTheWayOrders ? `${receivable.onTheWayOrders} por cobrar` : 'Ninguno'}
              </div>
            </div>
          </div>
          {receivable?.lastSettledAt && (
            <p className="small text-muted mb-2">Última liquidación: {formatDateTime(receivable.lastSettledAt)}</p>
          )}

          {canSettle && owed.length > 0 && (
            ownDrawer ? (
              <button type="button" className="btn btn-primary" onClick={() => setSettling(true)}>
                Liquidar a {courierName}
              </button>
            ) : (
              <div className="alert alert-warning py-2 mb-0 d-flex flex-wrap align-items-center gap-2" role="status">
                <span>Para liquidar, abre tu caja: el efectivo que entrega el repartidor entra a tu caja.</span>
                <button type="button" className="btn btn-sm btn-outline-dark" onClick={onOpenDrawer}>Ir a Mi caja</button>
              </div>
            )
          )}
          {!canSettle && owed.length > 0 && (
            <p className="small text-muted mb-0">Liquida quien cobra pedidos y maneja la caja.</p>
          )}
        </div>
      </section>

      <section className="mb-3" aria-labelledby="courier-owed-orders">
        <h2 id="courier-owed-orders" className="h6">Pedidos que debe</h2>
        {owed.length === 0 ? (
          <p className="text-muted small">Ningún pedido entregado espera el efectivo del repartidor.</p>
        ) : (
          <div className="table-responsive">
            <table className="table table-sm align-middle">
              <thead>
                <tr>
                  <th scope="col">Pedido</th>
                  <th scope="col">Cliente</th>
                  <th scope="col">Folio</th>
                  <th scope="col">Salió</th>
                  <th scope="col" className="text-end">Debe</th>
                </tr>
              </thead>
              <tbody>
                {owed.map((order) => (
                  <tr key={order.orderId}>
                    <td className="fw-semibold">{order.orderNumber}</td>
                    <td>{order.customerName}</td>
                    <td>{order.courierFolio ?? '—'}</td>
                    <td>{formatDateTime(order.dispatchedAt)}</td>
                    <td className="text-end ta-money">{formatMoney(order.balance)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {pending.data?.truncated && (
          <p className="small text-muted">Hay más pedidos: se muestran los más antiguos, los que entran en una liquidación.</p>
        )}
      </section>

      {onTheWay.length > 0 && (
        <section className="mb-3" aria-labelledby="courier-on-the-way">
          <h2 id="courier-on-the-way" className="h6">En camino</h2>
          <ul className="list-unstyled small">
            {onTheWay.map((order) => (
              <li key={order.orderId} className="mb-1">
                <span className="fw-semibold">{order.orderNumber}</span> · {order.customerName} · salió {formatDateTime(order.dispatchedAt)} ·{' '}
                <span className="ta-money">{describeCollection(stillToCollect(order.cashToCollect, order.balance), order.paysWith)}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section aria-labelledby="courier-history">
        <h2 id="courier-history" className="h6">Liquidaciones</h2>
        {settlements.isLoading ? <LoadingSpinner /> : (settlements.data?.items.length ?? 0) === 0 ? (
          <p className="text-muted small">Todavía no hay liquidaciones.</p>
        ) : (
          <>
            <div className="table-responsive">
              <table className="table table-sm align-middle">
                <thead>
                  <tr>
                    <th scope="col">Fecha</th>
                    <th scope="col">Recibió</th>
                    <th scope="col">Cómo</th>
                    <th scope="col" className="text-end">Pedidos</th>
                    <th scope="col" className="text-end">Ajustes</th>
                    <th scope="col" className="text-end">Entregó</th>
                    <th scope="col"><span className="visually-hidden">Acciones</span></th>
                  </tr>
                </thead>
                <tbody>
                  {settlements.data!.items.map((settlement) => (
                    <tr key={settlement.id}>
                      <td>{formatDateTime(settlement.settledAt)}</td>
                      <td>{settlement.settledBy ?? '—'}</td>
                      <td>{methodLabel(settlement.method)}</td>
                      <td className="text-end">
                        {settlement.orderCount} · <span className="ta-money">{formatMoney(settlement.ordersTotal)}</span>
                      </td>
                      <td className="text-end ta-money">{formatSignedMoney(settlement.adjustmentsTotal)}</td>
                      <td className="text-end ta-money fw-semibold">{formatMoney(settlement.handedAmount)}</td>
                      <td className="text-end">
                        <button type="button" className="btn btn-link btn-sm p-0" onClick={() => setOpenId(settlement.id)}>
                          Ver<span className="visually-hidden"> la liquidación del {formatDateTime(settlement.settledAt)}</span>
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {(settlements.data!.totalPages ?? 1) > 1 && (
              <nav className="d-flex gap-2 justify-content-end" aria-label="Páginas de liquidaciones">
                <button type="button" className="btn btn-light btn-sm" disabled={!settlements.data!.hasPreviousPage} onClick={() => setPage((p) => p - 1)}>
                  Anterior
                </button>
                <button type="button" className="btn btn-light btn-sm" disabled={!settlements.data!.hasNextPage} onClick={() => setPage((p) => p + 1)}>
                  Siguiente
                </button>
              </nav>
            )}
          </>
        )}
      </section>

      {settling && (
        <CourierSettlementModal
          courier={courier}
          owed={owed}
          truncated={pending.data?.truncated ?? false}
          onClose={() => setSettling(false)}
          onSettled={(detail) => {
            setSettling(false);
            toast.success(`Liquidación guardada: ${formatMoney(detail.settlement.handedAmount)} entraron a tu caja.`);
            void drawer.refetch();
          }}
        />
      )}
      {openId && <SettlementDetailModal id={openId} onClose={() => setOpenId(null)} />}
    </>
  );
}

function methodLabel(method: OrderPaymentMethod): string {
  return method === OrderPaymentMethod.Transfer ? 'Transferencia' : 'Efectivo';
}

/** A settlement with the orders it paid and its adjustments: it never changes. */
function SettlementDetailModal({ id, onClose }: { id: string; onClose: () => void }) {
  const detail = useCourierSettlement(id);
  return (
    <Modal show onHide={onClose} centered size="lg" aria-labelledby="courier-settlement-title">
      <Modal.Header closeButton>
        <Modal.Title id="courier-settlement-title" as="h2" className="h5">Liquidación de Paquetenvia</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {detail.isLoading ? <LoadingSpinner /> : detail.isError || !detail.data ? (
          <div className="alert alert-danger mb-0" role="alert">No se pudo leer la liquidación.</div>
        ) : <SettlementDetail detail={detail.data} />}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose}>Cerrar</button>
      </Modal.Footer>
    </Modal>
  );
}

function SettlementDetail({ detail }: { detail: CourierSettlementDetail }) {
  const { settlement } = detail;
  return (
    <>
      <p className="mb-2">
        {formatDateTime(settlement.settledAt)}{settlement.settledBy ? ` · recibió ${settlement.settledBy}` : ''} · {methodLabel(settlement.method)}
        {settlement.reference && <> · {settlement.reference}</>}
      </p>
      {settlement.notes && <p className="small text-muted">{settlement.notes}</p>}
      <div className="table-responsive">
        <table className="table table-sm align-middle">
          <thead>
            <tr>
              <th scope="col">Pedido</th>
              <th scope="col">Cliente</th>
              <th scope="col">Folio</th>
              <th scope="col" className="text-end">Pagó</th>
            </tr>
          </thead>
          <tbody>
            {detail.orders.map((order) => (
              <tr key={order.orderId}>
                <td className="fw-semibold">{order.orderNumber}</td>
                <td>{order.customerName}</td>
                <td>{order.courierFolio ?? '—'}</td>
                <td className="text-end ta-money">{formatMoney(order.amount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {detail.adjustments.length > 0 && (
        <ul className="list-unstyled small">
          {detail.adjustments.map((adjustment) => (
            <li key={adjustment.position}>
              {ADJUSTMENT_KIND_LABELS[adjustment.kind] ?? adjustment.kind}
              {adjustment.description && `: ${adjustment.description}`} · <span className="ta-money">{formatSignedMoney(adjustment.amount)}</span>
            </li>
          ))}
        </ul>
      )}
      <dl className="row small mb-0">
        <dt className="col-7">Pedidos</dt>
        <dd className="col-5 text-end ta-money mb-1">{formatMoney(settlement.ordersTotal)}</dd>
        <dt className="col-7">Ajustes</dt>
        <dd className="col-5 text-end ta-money mb-1">{formatSignedMoney(settlement.adjustmentsTotal)}</dd>
        <dt className="col-7">Entregó</dt>
        <dd className="col-5 text-end ta-money fw-semibold mb-0">{formatMoney(settlement.handedAmount)}</dd>
      </dl>
    </>
  );
}
