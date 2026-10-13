import { useId, useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { ordersApi } from '../../api/ordersApi';
import { DeliveryCourier, type OrderDto } from '../../types';
import { changeFor, parsePaysWith, paysWithProblem } from '../../utils/courierCash';
import { formatMoney, toCents } from '../../utils/money';
import { FOLIO_MAX_LENGTH, TRACKING_URL_MAX_LENGTH, TRACKING_URL_MESSAGE, isHttpsUrl } from './courierTrip';

interface OrderDispatchFormProps {
  order: OrderDto;
  /** The order went out (or its trip was corrected): it is read again. */
  onSaved: () => Promise<void> | void;
  onCancel: () => void;
}

/**
 * «Salió a entregar» (Fase 22D): the folio of the trip asked for in Paquetenvia and its link, whether the courier collects
 * the balance in cash and the bill the customer pays with, for the change. The same form corrects it while it is on its way.
 */
export default function OrderDispatchForm({ order, onSaved, onCancel }: OrderDispatchFormProps) {
  const id = useId();
  const delivery = order.delivery!;
  const dispatched = !!delivery.dispatchedAt;
  // What the courier collects is the balance when it goes out; once out, the balance it still has to collect.
  const toCollect = order.balance;
  const owes = toCents(toCollect) > 0;
  const [folio, setFolio] = useState(delivery.courierFolio ?? '');
  const [trackingUrl, setTrackingUrl] = useState(delivery.trackingUrl ?? '');
  const [collectsCash, setCollectsCash] = useState(owes && (!dispatched || delivery.cashToCollect != null));
  const [paysWith, setPaysWith] = useState(delivery.paysWith != null ? String(delivery.paysWith) : '');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const paysWithIssue = collectsCash ? paysWithProblem(paysWith, toCollect) : '';
  const bill = collectsCash ? parsePaysWith(paysWith) : null;

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    if (saving) return;
    const link = trackingUrl.trim();
    if (link && !isHttpsUrl(link)) {
      setError(TRACKING_URL_MESSAGE);
      return;
    }
    if (paysWithIssue) {
      setError(paysWithIssue);
      return;
    }
    setSaving(true);
    setError('');
    try {
      await ordersApi.dispatch(order.id, {
        courier: DeliveryCourier.Paquetenvia,
        folio: folio.trim(),
        trackingUrl: link || undefined,
        collectsCash,
        paysWith: typeof bill === 'number' ? bill : undefined,
      });
      await onSaved();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar la salida.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <form onSubmit={(event) => void save(event)} aria-label={dispatched ? 'Corregir salida' : 'Salió a entregar'}>
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
        <div className="col-12">
          <div className="form-check">
            <input
              id={`${id}-cash`}
              className="form-check-input"
              type="checkbox"
              checked={collectsCash}
              disabled={!owes}
              onChange={(event) => setCollectsCash(event.target.checked)}
            />
            <label className="form-check-label" htmlFor={`${id}-cash`}>
              Paquetenvia cobra en efectivo <span className="ta-money">{formatMoney(owes ? toCollect : 0)}</span>
            </label>
          </div>
          {!owes && <p className="small text-muted mb-0">El pedido ya está pagado: el repartidor no cobra nada.</p>}
        </div>
        {collectsCash && (
          <div className="col-sm-5">
            <label className="form-label small mb-1" htmlFor={`${id}-pays-with`}>Paga con (opcional)</label>
            <input
              id={`${id}-pays-with`}
              className="form-control"
              inputMode="decimal"
              autoComplete="off"
              placeholder="$"
              value={paysWith}
              onChange={(event) => setPaysWith(event.target.value)}
              aria-describedby={`${id}-change`}
              aria-invalid={!!paysWithIssue}
            />
            <div id={`${id}-change`} className="small mt-1" role="status">
              {paysWithIssue ? <span className="text-danger">{paysWithIssue}</span>
                : typeof bill === 'number' ? <>Cambio: <span className="ta-money">{formatMoney(changeFor(bill, toCollect))}</span></>
                : <span className="text-muted">Para que lleve el cambio.</span>}
            </div>
          </div>
        )}
      </div>
      {error && <div className="alert alert-danger py-2 mt-2 mb-0" role="alert">{error}</div>}
      <div className="d-flex gap-2 mt-2">
        <button type="submit" className="btn btn-sm btn-primary" disabled={saving || !folio.trim()}>
          {saving ? 'Guardando…' : dispatched ? 'Guardar corrección' : 'Salió a entregar'}
        </button>
        <button type="button" className="btn btn-sm btn-light" onClick={onCancel} disabled={saving}>
          Cancelar
        </button>
      </div>
    </form>
  );
}
