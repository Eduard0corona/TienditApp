import { useId, useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { ordersApi } from '../../api/ordersApi';
import { DeliveryCourier, OrderStatus, type OrderDto } from '../../types';
import { changeFor, describeCollection } from '../../utils/courierCash';
import { formatTime } from '../../utils/dates';
import { DELIVERY_COURIER_LABELS } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import { formatDeliveryFee } from '../../utils/ticket';
import OrderDispatchForm from './OrderDispatchForm';
import { FOLIO_MAX_LENGTH, TRACKING_URL_MAX_LENGTH, TRACKING_URL_MESSAGE, isHttpsUrl } from './courierTrip';

interface OrderDeliveryPanelProps {
  order: OrderDto;
  /** Whoever takes the orders writes down the courier's trip and sends the order out. */
  canManage: boolean;
  /** The trip was saved or the order went out: the order is read again. */
  onSaved: (change: 'shipment' | 'dispatch') => Promise<void> | void;
}

type Editing = 'shipment' | 'dispatch' | null;

/**
 * Where an order sent to the customer's address goes and what it charged for it (Fase 22C), the courier's trip in
 * Paquetenvia, and its way out (Fase 22D): «Salió a entregar» with what the courier collects in cash and the customer's bill,
 * «En camino» while it goes, and what Paquetenvia owes once it was delivered and not handed over yet.
 */
export default function OrderDeliveryPanel({ order, canManage, onSaved }: OrderDeliveryPanelProps) {
  const id = useId();
  const delivery = order.delivery;
  const [editing, setEditing] = useState<Editing>(null);
  const [folio, setFolio] = useState('');
  const [trackingUrl, setTrackingUrl] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  if (!delivery) return null;

  const hasTrip = delivery.courier != null && !!delivery.courierFolio;
  const dispatched = !!delivery.dispatchedAt;
  const closed = order.status === OrderStatus.Delivered || order.status === OrderStatus.Completed;
  const cancelled = order.status === OrderStatus.Cancelled;
  const onTheWay = dispatched && !closed && !cancelled;
  // It goes out once confirmed and until it is delivered.
  const canSendOut = canManage && !cancelled && !closed && order.status !== OrderStatus.Pending;

  const startShipment = () => {
    setFolio(delivery.courierFolio ?? '');
    setTrackingUrl(delivery.trackingUrl ?? '');
    setError('');
    setEditing('shipment');
  };

  const saveShipment = async (event: React.FormEvent) => {
    event.preventDefault();
    if (saving) return;
    const link = trackingUrl.trim();
    if (link && !isHttpsUrl(link)) {
      setError(TRACKING_URL_MESSAGE);
      return;
    }
    setSaving(true);
    setError('');
    try {
      await ordersApi.registerCourierShipment(order.id, {
        courier: DeliveryCourier.Paquetenvia,
        folio: folio.trim(),
        trackingUrl: link || undefined,
      });
      await onSaved('shipment');
      setEditing(null);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar el envío.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <section className="border rounded p-2 mb-3" aria-labelledby={`${id}-title`}>
      <div id={`${id}-title`} className="fw-semibold mb-1 d-flex align-items-center gap-2 flex-wrap">
        <span><i className="ri-e-bike-2-line me-1" aria-hidden="true"></i>A domicilio</span>
        {onTheWay && <span className="badge text-bg-info">En camino</span>}
      </div>
      <p className="mb-1">{delivery.addressLine}</p>
      {delivery.references && <p className="mb-1 small text-muted">Referencias: {delivery.references}</p>}
      <p className="mb-2 small">
        CP {delivery.postalCode} · zona {delivery.zoneName} · envío <span className="ta-money">{formatDeliveryFee(delivery.fee)}</span>
      </p>

      {!dispatched && delivery.paysWith != null && order.balance > 0 && (
        <p className="mb-2 small">
          Paga con <span className="ta-money">{formatMoney(delivery.paysWith)}</span> · cambio{' '}
          <span className="ta-money">{formatMoney(changeFor(delivery.paysWith, order.balance))}</span>
        </p>
      )}

      {hasTrip && editing === null && (
        <p className="mb-2">
          <strong>{DELIVERY_COURIER_LABELS[delivery.courier!]}:</strong> folio {delivery.courierFolio}
          {isHttpsUrl(delivery.trackingUrl) && (
            <a className="ms-2" href={delivery.trackingUrl} target="_blank" rel="noopener noreferrer">Seguir envío</a>
          )}
        </p>
      )}

      {dispatched && editing === null && (
        <p className="mb-2 small">
          Salió a las {formatTime(delivery.dispatchedAt)}{delivery.dispatchedBy ? ` (${delivery.dispatchedBy})` : ''}.{' '}
          {delivery.cashToCollect != null
            ? <span className="ta-money">{describeCollection(delivery.cashToCollect, delivery.paysWith)}</span>
            : 'El repartidor no cobra nada.'}
        </p>
      )}

      {delivery.awaitsCourierCash && (
        <div className="alert alert-warning py-2 mb-2" role="status">
          Paquetenvia te debe <strong className="ta-money">{formatMoney(order.balance)}</strong> de este pedido: se liquida en
          Caja › Repartidores.
        </div>
      )}

      {canManage && !cancelled && editing === null && (
        <>
          {!closed && order.status === OrderStatus.Pending && (
            <p className="small text-muted mb-2">Confirma el pedido antes de mandarlo con el repartidor.</p>
          )}
          <div className="d-flex gap-2 flex-wrap">
            {canSendOut && (
              <button
                type="button"
                className={`btn btn-sm ${dispatched ? 'btn-outline-primary' : 'btn-primary'}`}
                onClick={() => setEditing('dispatch')}
              >
                {dispatched ? 'Corregir salida' : 'Salió a entregar'}
              </button>
            )}
            {/* On its way, correcting the way out corrects the trip too. */}
            {!onTheWay && (
              <button type="button" className="btn btn-sm btn-outline-secondary" onClick={startShipment}>
                {hasTrip ? 'Corregir envío de Paquetenvia' : 'Registrar envío de Paquetenvia'}
              </button>
            )}
          </div>
        </>
      )}

      {editing === 'dispatch' && (
        <OrderDispatchForm
          order={order}
          onSaved={async () => {
            await onSaved('dispatch');
            setEditing(null);
          }}
          onCancel={() => setEditing(null)}
        />
      )}

      {editing === 'shipment' && (
        <form onSubmit={(event) => void saveShipment(event)} aria-label="Envío de Paquetenvia">
          <div className="row g-2">
            <div className="col-sm-5">
              <label className="form-label small mb-1" htmlFor={`${id}-folio`}>Folio de Paquetenvia</label>
              <input
                id={`${id}-folio`}
                className="form-control"
                required
                maxLength={FOLIO_MAX_LENGTH}
                autoComplete="off"
                value={folio}
                onChange={(event) => setFolio(event.target.value)}
              />
            </div>
            <div className="col-sm-7">
              <label className="form-label small mb-1" htmlFor={`${id}-link`}>Enlace para seguirlo (opcional)</label>
              <input
                id={`${id}-link`}
                className="form-control"
                type="url"
                inputMode="url"
                maxLength={TRACKING_URL_MAX_LENGTH}
                placeholder="https://"
                value={trackingUrl}
                onChange={(event) => setTrackingUrl(event.target.value)}
              />
            </div>
          </div>
          {error && <div className="alert alert-danger py-2 mt-2 mb-0" role="alert">{error}</div>}
          <div className="d-flex gap-2 mt-2">
            <button type="submit" className="btn btn-sm btn-primary" disabled={saving || !folio.trim()}>
              {saving ? 'Guardando…' : 'Guardar envío'}
            </button>
            <button type="button" className="btn btn-sm btn-light" onClick={() => setEditing(null)} disabled={saving}>
              Cancelar
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
