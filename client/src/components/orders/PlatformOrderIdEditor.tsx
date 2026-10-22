import { useId, useState } from 'react';
import { getApiErrorCode, getApiErrorMessage } from '../../api/errors';
import { ordersApi } from '../../api/ordersApi';
import { useConfirm } from '../../hooks/useConfirm';
import type { OrderDto } from '../../types';
import {
  PLATFORM_ORDER_ID_MAX_INPUT_LENGTH,
  getPlatformName,
  getPlatformOrderIdHelp,
  getPlatformOrderIdProblem,
  type DeliveryPlatform,
} from '../../utils/deliveryPlatforms';

interface PlatformOrderIdEditorProps {
  order: OrderDto;
  platform: DeliveryPlatform;
  /** Whoever takes the orders writes or corrects the long id, at any status. */
  canManage: boolean;
  /** The long id was saved (or cleared): the order is read again. */
  onSaved: (cleared: boolean) => Promise<void> | void;
}

/**
 * The platform's long id of an order (Fase 22I), in its detail: what its statements name the order by. Whoever takes the
 * orders adds it or corrects it later, at any status; empty clears it. Another order of the platform with the same one
 * asks first, as Vender does; the server checks everything again.
 */
export default function PlatformOrderIdEditor({ order, platform, canManage, onSaved }: PlatformOrderIdEditorProps) {
  const id = useId();
  const { confirm } = useConfirm();
  const current = order.platformOrder?.externalOrderId ?? null;
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const problem = getPlatformOrderIdProblem(value);

  const start = () => {
    setValue(current ?? '');
    setError('');
    setEditing(true);
  };

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    if (saving) return;
    if (problem) {
      setError(problem);
      return;
    }
    setSaving(true);
    setError('');
    const externalOrderId = value.trim();
    try {
      try {
        await ordersApi.setPlatformOrderId(order.id, { externalOrderId });
      } catch (err: unknown) {
        if (getApiErrorCode(err) !== 'possible_duplicate_order') throw err;
        const confirmed = await confirm({
          title: '¿Es el ID correcto?',
          message: `Otro pedido de ${getPlatformName(platform)} ya tiene el ID largo ${externalOrderId}. `
            + 'Los IDs largos no se repiten: revisa que no sea el mismo pedido capturado dos veces.',
          confirmLabel: 'Guardar de todos modos',
          cancelLabel: 'Revisar',
        });
        if (!confirmed) return;
        await ordersApi.setPlatformOrderId(order.id, { externalOrderId, confirmPossibleDuplicate: true });
      }
      await onSaved(!externalOrderId);
      setEditing(false);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar el ID largo.'));
    } finally {
      setSaving(false);
    }
  };

  if (editing) {
    const shownError = error || (value.trim() ? problem : '');
    return (
      <form className="mb-2" onSubmit={save} noValidate>
        <label className="form-label small mb-1" htmlFor={`${id}-order-id`}>ID largo del pedido</label>
        <input
          id={`${id}-order-id`}
          className={`form-control form-control-sm ${shownError ? 'is-invalid' : ''}`}
          maxLength={PLATFORM_ORDER_ID_MAX_INPUT_LENGTH}
          autoCapitalize="characters"
          autoComplete="off"
          spellCheck={false}
          // Opened by the person to type it right away.
          autoFocus
          aria-invalid={shownError ? true : undefined}
          aria-describedby={`${id}-order-id-help`}
          value={value}
          onChange={(event) => setValue(event.target.value)}
          disabled={saving}
        />
        <div id={`${id}-order-id-help`} className={shownError ? 'invalid-feedback d-block' : 'form-text'}>
          {shownError || (current
            ? `${getPlatformOrderIdHelp(platform)} Déjalo vacío para quitarlo.`
            : getPlatformOrderIdHelp(platform))}
        </div>
        <div className="d-flex gap-2 mt-2">
          <button type="submit" className="btn btn-sm btn-primary" disabled={saving}>
            {saving ? 'Guardando…' : 'Guardar'}
          </button>
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setEditing(false)} disabled={saving}>
            Cancelar
          </button>
        </div>
      </form>
    );
  }

  return (
    <p className="mb-1">
      <strong>ID largo:</strong>{' '}
      {current ? <span className="text-break">{current}</span> : <span className="text-muted">sin capturar</span>}
      {canManage && (
        <button
          type="button"
          className="btn btn-link btn-sm p-0 ms-2 align-baseline"
          aria-label={`${current ? 'Corregir' : 'Agregar'} el ID largo del pedido ${order.orderNumber}`}
          onClick={start}
        >
          {current ? 'Corregir' : 'Agregar'}
        </button>
      )}
    </p>
  );
}
