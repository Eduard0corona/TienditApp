import { useId, useState } from 'react';
import type { OrderPaymentMethod } from '../../types';
import { OrderPaymentMethod as PaymentMethod } from '../../types';
import { IN_PERSON_METHODS } from '../sales/paymentMethods';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney, parseMoneyInput, toCents } from '../../utils/money';

/** What the panel does with the balance in the customer's favor (Fase 26D). */
export type BalanceMovementKind = 'deposit' | 'use' | 'return';

export interface BalanceMovement {
  amount: number;
  /** How the money enters or leaves the drawer; the balance itself when it is used. */
  method: OrderPaymentMethod;
  reference?: string;
  notes?: string;
}

interface BalanceMovementPanelProps {
  kind: BalanceMovementKind;
  /** The most it moves: what they owe and have (use), what they have (return); a deposit has none. */
  max?: number;
  submitting: boolean;
  error?: string;
  onSubmit: (movement: BalanceMovement) => void;
  onCancel: () => void;
}

// The most one movement moves, as the server takes it.
const MAX_AMOUNT_CENTS = 100_000_000;

const COPY: Record<BalanceMovementKind, { lead: string; confirm: string }> = {
  deposit: {
    lead: 'Lo que deje queda a su favor para pagar después. Entra a tu caja, pero no es una venta hasta que lo use.',
    confirm: 'Dejar a favor',
  },
  use: {
    lead: 'Paga lo que debe con su saldo a favor, desde el pedido más antiguo. No entra dinero a la caja: entró cuando lo dejó.',
    confirm: 'Usar',
  },
  return: {
    lead: 'Se le devuelve de su saldo a favor y sale de tu caja.',
    confirm: 'Devolver',
  },
};

/**
 * Leaves money in the customer's favor, pays what they owe with it, or gives it back (Fase 26D): the amount, how it enters
 * or leaves the drawer (not when it is used) and a reference and a note. Amounts in cents; the server checks them again.
 */
export default function BalanceMovementPanel({ kind, max, submitting, error, onSubmit, onCancel }: BalanceMovementPanelProps) {
  const ids = useId();
  const [amountInput, setAmountInput] = useState('');
  const [method, setMethod] = useState<OrderPaymentMethod>(PaymentMethod.Cash);
  const [reference, setReference] = useState('');
  const [notes, setNotes] = useState('');
  const inPerson = kind !== 'use';
  const limitCents = Math.min(max != null ? toCents(max) : MAX_AMOUNT_CENTS, MAX_AMOUNT_CENTS);

  // Empty pays all it may with the balance (what they owe or have); leaving it and giving it back, money that changes
  // hands, need the amount written.
  const typed = amountInput.trim();
  const amountCents = typed === '' ? (kind === 'use' ? limitCents : 0) : toCents(parseMoneyInput(typed) ?? -1);
  const amountIsValid = amountCents > 0 && amountCents <= limitCents;
  const amountProblem = typed === '' || amountIsValid
    ? ''
    : amountCents <= 0
      ? 'Escribe un monto válido, por ejemplo 200 o 150.50.'
      : `No puede ser mayor a ${formatMoney(limitCents / 100)}.`;

  const submit = () => {
    if (!amountIsValid) return;
    onSubmit({
      amount: amountCents / 100,
      method: inPerson ? method : PaymentMethod.CustomerBalance,
      reference: inPerson ? reference.trim() || undefined : undefined,
      notes: inPerson ? notes.trim() || undefined : undefined,
    });
  };

  return (
    <form
      className="charge-panel"
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      <p className="small text-muted">{COPY[kind].lead}</p>
      <div className="mb-3">
        <label className="form-label fw-semibold" htmlFor={`${ids}-amount`}>Monto</label>
        <input
          id={`${ids}-amount`}
          className={`form-control form-control-lg ta-money ${amountProblem ? 'is-invalid' : ''}`}
          aria-invalid={amountProblem ? true : undefined}
          aria-describedby={amountProblem ? `${ids}-amount-problem` : undefined}
          inputMode="decimal"
          autoComplete="off"
          autoFocus
          placeholder={kind === 'use' ? (limitCents / 100).toFixed(2) : '0.00'}
          value={amountInput}
          onChange={(event) => setAmountInput(event.target.value)}
          disabled={submitting}
        />
        {amountProblem && <div id={`${ids}-amount-problem`} className="invalid-feedback">{amountProblem}</div>}
        {max != null && (
          <div className="form-text">
            {kind === 'use' ? 'Hasta' : 'Tiene'} <span className="ta-money">{formatMoney(max)}</span>
            {kind === 'use' ? ': lo que debe o lo que tiene a favor, lo que sea menos.' : ' a favor.'}
          </div>
        )}
      </div>

      {inPerson && (
        <>
          <div className="charge-methods mb-3" role="radiogroup" aria-label={kind === 'deposit' ? 'Cómo lo deja' : 'Cómo se le devuelve'}>
            {IN_PERSON_METHODS.map((item) => (
              <button
                key={item.value}
                type="button"
                role="radio"
                aria-checked={method === item.value}
                className={`btn btn-touch ${method === item.value ? 'btn-primary' : 'btn-outline-secondary'}`}
                onClick={() => setMethod(item.value)}
                disabled={submitting}
              >
                <i className={`${item.icon} me-1`} aria-hidden="true"></i>
                {getPaymentMethodLabel(item.value)}
              </button>
            ))}
          </div>
          <div className="row g-2">
            <div className="col-sm-6">
              <label className="form-label" htmlFor={`${ids}-reference`}>Referencia (opcional)</label>
              <input
                id={`${ids}-reference`}
                className="form-control"
                maxLength={100}
                value={reference}
                onChange={(event) => setReference(event.target.value)}
                disabled={submitting}
              />
            </div>
            <div className="col-sm-6">
              <label className="form-label" htmlFor={`${ids}-notes`}>Nota (opcional)</label>
              <input
                id={`${ids}-notes`}
                className="form-control"
                maxLength={500}
                placeholder={kind === 'deposit' ? 'Por ejemplo: anticipo del pastel' : undefined}
                value={notes}
                onChange={(event) => setNotes(event.target.value)}
                disabled={submitting}
              />
            </div>
          </div>
        </>
      )}

      {error && <div className="alert alert-danger py-2 mt-3 mb-0">{error}</div>}

      <div className="charge-actions mt-3">
        <button type="button" className="btn btn-light btn-touch" onClick={onCancel} disabled={submitting}>
          Volver
        </button>
        <button type="submit" className="btn btn-primary btn-touch btn-touch-lg flex-grow-1" disabled={submitting || !amountIsValid}>
          {submitting ? 'Guardando…' : `${COPY[kind].confirm} ${formatMoney((amountIsValid ? amountCents : 0) / 100)}`}
        </button>
      </div>
    </form>
  );
}
