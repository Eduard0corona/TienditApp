import { useId, useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { ordersApi } from '../../api/ordersApi';
import { DeliveryCourier, OrderStatus, type OrderDto } from '../../types';
import { DELIVERY_COURIER_LABELS } from '../../utils/labels';
import { formatDeliveryFee } from '../../utils/ticket';

interface OrderDeliveryPanelProps {
  order: OrderDto;
  /** Whoever takes the orders writes down the courier's trip. */
  canManage: boolean;
  /** The trip was saved: the order is read again. */
  onSaved: () => Promise<void> | void;
}

// The same limits as the API (PlatformOrderNumber.MaxInputLength, CourierShipment.MaxTrackingUrlLength).
const FOLIO_MAX_LENGTH = 60;
const TRACKING_URL_MAX_LENGTH = 500;

/** A link the browser can open safely: written with «https://», as the server keeps them. */
function isHttpsUrl(value: string | null | undefined): value is string {
  if (!value || !/^https:\/\//i.test(value.trim())) return false;
  try {
    return new URL(value).protocol === 'https:';
  } catch {
    return false;
  }
}

/**
 * Where an order sent to the customer's address goes and what it charged for it (Fase 22C), and the courier's trip: the
 * folio of the trip asked for in Paquetenvia and its link, written down (or corrected) by whoever takes the orders.
 */
export default function OrderDeliveryPanel({ order, canManage, onSaved }: OrderDeliveryPanelProps) {
  const id = useId();
  const delivery = order.delivery;
  const [editing, setEditing] = useState(false);
  const [folio, setFolio] = useState('');
  const [trackingUrl, setTrackingUrl] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  if (!delivery) return null;

  const hasTrip = delivery.courier != null && !!delivery.courierFolio;
  const cancelled = order.status === OrderStatus.Cancelled;

  const startEditing = () => {
    setFolio(delivery.courierFolio ?? '');
    setTrackingUrl(delivery.trackingUrl ?? '');
    setError('');
    setEditing(true);
  };

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    if (saving) return;
    const link = trackingUrl.trim();
    if (link && !isHttpsUrl(link)) {
      setError('El enlace del envío debe empezar con https://.');
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
      await onSaved();
      setEditing(false);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar el envío.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <section className="border rounded p-2 mb-3" aria-labelledby={`${id}-title`}>
      <div id={`${id}-title`} className="fw-semibold mb-1">
        <i className="ri-e-bike-2-line me-1" aria-hidden="true"></i>A domicilio
      </div>
      <p className="mb-1">{delivery.addressLine}</p>
      {delivery.references && <p className="mb-1 small text-muted">Referencias: {delivery.references}</p>}
      <p className="mb-2 small">
        CP {delivery.postalCode} · zona {delivery.zoneName} · envío <span className="ta-money">{formatDeliveryFee(delivery.fee)}</span>
      </p>

      {hasTrip && !editing && (
        <p className="mb-2">
          <strong>{DELIVERY_COURIER_LABELS[delivery.courier!]}:</strong> folio {delivery.courierFolio}
          {isHttpsUrl(delivery.trackingUrl) && (
            <a className="ms-2" href={delivery.trackingUrl} target="_blank" rel="noopener noreferrer">Seguir envío</a>
          )}
        </p>
      )}

      {canManage && !cancelled && !editing && (
        <button type="button" className="btn btn-sm btn-outline-primary" onClick={startEditing}>
          {hasTrip ? 'Corregir envío de Paquetenvia' : 'Registrar envío de Paquetenvia'}
        </button>
      )}

      {editing && (
        <form onSubmit={(event) => void save(event)} aria-label="Envío de Paquetenvia">
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
            <button type="button" className="btn btn-sm btn-light" onClick={() => setEditing(false)} disabled={saving}>
              Cancelar
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
