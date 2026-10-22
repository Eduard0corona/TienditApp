import { useEffect, useId, useRef, useState } from 'react';
import { getApiErrorCode, getApiErrorMessage } from '../../api/errors';
import { ordersApi } from '../../api/ordersApi';
import { useConfirm } from '../../hooks/useConfirm';
import type { OrderDto } from '../../types';
import {
  PLATFORM_ORDER_ID_MAX_INPUT_LENGTH,
  getPlatformName,
  getPlatformOrderIdHint,
  getPlatformOrderIdProblem,
  normalizePlatformOrderId,
  type DeliveryPlatform,
} from '../../utils/deliveryPlatforms';

interface PlatformOrderIdEditorProps {
  order: OrderDto;
  platform: DeliveryPlatform;
  /** Whoever takes the orders writes or corrects the long id, at any status. */
  canManage: boolean;
  /** The long id was saved (or cleared): the order is read again. Never rejects. */
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
  const hint = getPlatformOrderIdHint(platform);
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState('');
  const [saving, setSaving] = useState(false);
  // What the server answered: said aloud, and forgotten as the person corrects the value.
  const [serverError, setServerError] = useState('');
  const [tried, setTried] = useState(false);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  // Where the focus goes once the field is enabled again (after an answer) or closed.
  const focusNext = useRef<'input' | 'toggle' | null>(null);
  const problem = getPlatformOrderIdProblem(value);
  const shownProblem = problem && (tried || value.trim().length >= 6) ? problem : '';

  useEffect(() => {
    if (saving || !focusNext.current) return;
    (focusNext.current === 'input' ? inputRef : toggleRef).current?.focus();
    focusNext.current = null;
  }, [saving, editing]);

  const start = () => {
    setValue(current ?? '');
    setServerError('');
    setTried(false);
    setEditing(true);
  };

  const close = () => {
    focusNext.current = 'toggle';
    setEditing(false);
  };

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    if (saving) return;
    setTried(true);
    if (problem) return;
    const externalOrderId = value.trim() ? normalizePlatformOrderId(value) : '';
    // Nothing to write: empty with none saved, or the one saved written again.
    if (externalOrderId === (current ?? '')) {
      close();
      return;
    }
    setSaving(true);
    setServerError('');
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
        if (!confirmed) {
          focusNext.current = 'input';
          return;
        }
        await ordersApi.setPlatformOrderId(order.id, { externalOrderId, confirmPossibleDuplicate: true });
      }
      await onSaved(!externalOrderId);
      close();
    } catch (err: unknown) {
      setServerError(getApiErrorMessage(err, 'No se pudo guardar el ID largo.'));
      focusNext.current = 'input';
    } finally {
      setSaving(false);
    }
  };

  if (editing) {
    const invalid = Boolean(shownProblem || serverError);
    return (
      <form className="mb-2" onSubmit={save} noValidate>
        <label className="form-label small mb-1" htmlFor={`${id}-order-id`}>ID largo del pedido</label>
        <input
          ref={inputRef}
          id={`${id}-order-id`}
          className={`form-control form-control-sm ${invalid ? 'is-invalid' : ''}`}
          maxLength={PLATFORM_ORDER_ID_MAX_INPUT_LENGTH}
          inputMode={hint.inputMode}
          autoCapitalize="characters"
          autoComplete="off"
          spellCheck={false}
          // Opened by the person to type it right away.
          autoFocus
          aria-invalid={invalid ? true : undefined}
          aria-describedby={`${id}-order-id-help`}
          value={value}
          onChange={(event) => {
            setValue(event.target.value);
            setServerError('');
          }}
          onKeyDown={(event) => {
            // Esc leaves the field, not the whole order.
            if (event.key === 'Escape' && !saving) {
              event.preventDefault();
              event.stopPropagation();
              close();
            }
          }}
          disabled={saving}
        />
        <div id={`${id}-order-id-help`} className={invalid ? 'invalid-feedback d-block' : 'form-text'}>
          {serverError
            ? <span role="alert">{serverError}</span>
            : shownProblem || (current ? `${hint.help} Déjalo vacío para quitarlo.` : hint.help)}
        </div>
        <div className="d-flex gap-2 mt-2">
          <button type="submit" className="btn btn-sm btn-primary" disabled={saving}>
            {saving ? 'Guardando…' : 'Guardar'}
          </button>
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={close} disabled={saving}>
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
          ref={toggleRef}
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
