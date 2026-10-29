import { useId, useState } from 'react';
import type { CustomerAccount } from '../../api/customerCredit';
import { OrderWriteOffReason } from '../../api/generated/enums';
import { formatMoney, parseMoneyInput, toCents } from '../../utils/money';
import { WRITE_OFF_REASONS } from '../../utils/writeOffReasons';
import { writeOffProblem } from './customerAccount';

/** What the owner decided to forgive: of one order, or of the whole account from the oldest. */
export interface WriteOffChoice {
  orderId: string | null;
  amount: number;
  reason: OrderWriteOffReason;
  notes: string | null;
}

interface WriteOffPanelProps {
  account: CustomerAccount;
  submitting: boolean;
  error?: string;
  onSubmit: (choice: WriteOffChoice) => void;
  onCancel: () => void;
}

const NOTES_MAX_LENGTH = 500;
const NOTES_NEEDED = 'Escribe por qué condonas cuando el motivo es «Otro».';

/**
 * Condonar (Fase 26B): the owner forgives part or all of what a customer owes, of one order or of the whole account from
 * the oldest order, with a reason. No money moves, and the panel says so before the button; a mistake is undone from «Lo
 * condonado» in the account.
 */
export default function WriteOffPanel({ account, submitting, error, onSubmit, onCancel }: WriteOffPanelProps) {
  const ids = {
    scope: useId(), amount: useId(), reason: useId(), reasonHelp: useId(), notes: useId(), amountHelp: useId(), notesHelp: useId(),
  };
  const [orderId, setOrderId] = useState('');
  const maxCents = toCents(orderId ? account.orders.find((o) => o.orderId === orderId)?.balance ?? 0 : account.owed);
  // It starts empty: the owner writes how much, even when it is all of it, so a slip never forgives the whole account.
  const [amount, setAmount] = useState('');
  const [reason, setReason] = useState<OrderWriteOffReason>(OrderWriteOffReason.WontBeCollected);
  const [notes, setNotes] = useState('');

  const amountProblem = writeOffProblem(amount, maxCents);
  const showAmountProblem = amount.trim() !== '' && !!amountProblem;
  const notesProblem = reason === OrderWriteOffReason.Other && notes.trim().length < 3 ? NOTES_NEEDED : null;
  const showNotesProblem = notes.trim() !== '' && !!notesProblem;
  const typedCents = amountProblem ? 0 : toCents(parseMoneyInput(amount) ?? 0);
  const canSubmit = !submitting && !amountProblem && !notesProblem;

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (!canSubmit) return;
        onSubmit({ orderId: orderId || null, amount: typedCents / 100, reason, notes: notes.trim() || null });
      }}
      noValidate
    >
      <p className="small text-muted">
        Condonar no mueve dinero de la caja: baja lo que debe y queda registrado quién lo condonó y por qué. Si es un error, se deshace desde «Lo condonado».
      </p>

      <div className="mb-3">
        <label className="form-label" htmlFor={ids.scope}>¿Qué condonas?</label>
        <select id={ids.scope} className="form-select" value={orderId} onChange={(event) => setOrderId(event.target.value)} disabled={submitting}>
          <option value="">Toda la cuenta · {formatMoney(account.owed)}</option>
          {account.orders.map((order) => (
            <option key={order.orderId} value={order.orderId}>{order.orderNumber} · {formatMoney(order.balance)}</option>
          ))}
        </select>
        {!orderId && account.orders.length > 1 && (
          <div className="form-text">Se condona del pedido más antiguo al más nuevo.</div>
        )}
      </div>

      <div className="mb-3">
        <label className="form-label" htmlFor={ids.amount}>Cuánto</label>
        <div className="input-group">
          <span className="input-group-text">$</span>
          <input
            id={ids.amount}
            className={`form-control ta-money ${showAmountProblem ? 'is-invalid' : ''}`}
            inputMode="decimal"
            autoComplete="off"
            // The panel replaces the account and its buttons: the focus starts on what the owner writes first.
            autoFocus
            value={amount}
            onChange={(event) => setAmount(event.target.value)}
            aria-describedby={ids.amountHelp}
            aria-invalid={showAmountProblem}
            disabled={submitting}
          />
        </div>
        <div id={ids.amountHelp} className={showAmountProblem ? 'invalid-feedback d-block' : 'form-text'}>
          {showAmountProblem ? amountProblem : `Hasta ${formatMoney(maxCents / 100)}.`}
        </div>
      </div>

      <div className="mb-3">
        <label className="form-label" htmlFor={ids.reason}>Motivo</label>
        <select
          id={ids.reason}
          className="form-select"
          value={reason}
          onChange={(event) => setReason(Number(event.target.value) as OrderWriteOffReason)}
          aria-describedby={reason === OrderWriteOffReason.Discount ? ids.reasonHelp : undefined}
          disabled={submitting}
        >
          {WRITE_OFF_REASONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
        </select>
        {reason === OrderWriteOffReason.Discount && (
          <div id={ids.reasonHelp} className="form-text">
            Baja lo que se debe. Si el cliente ya pagó, registra un reembolso en el pedido.
          </div>
        )}
      </div>

      <div className="mb-3">
        <label className="form-label" htmlFor={ids.notes}>
          Nota {reason === OrderWriteOffReason.Other ? '' : <span className="text-muted">(opcional)</span>}
        </label>
        <textarea
          id={ids.notes}
          className={`form-control ${showNotesProblem ? 'is-invalid' : ''}`}
          rows={2}
          maxLength={NOTES_MAX_LENGTH}
          value={notes}
          onChange={(event) => setNotes(event.target.value)}
          aria-describedby={notesProblem ? ids.notesHelp : undefined}
          aria-invalid={showNotesProblem}
          disabled={submitting}
        />
        {notesProblem && <div id={ids.notesHelp} className={showNotesProblem ? 'invalid-feedback d-block' : 'form-text'}>{notesProblem}</div>}
      </div>

      {error && <div className="alert alert-danger py-2" role="alert">{error}</div>}

      <div className="d-flex flex-wrap justify-content-end gap-2">
        <button type="button" className="btn btn-light btn-touch" onClick={onCancel} disabled={submitting}>Cancelar</button>
        <button type="submit" className="btn btn-danger btn-touch" disabled={!canSubmit}>
          {submitting ? 'Condonando…' : <>Condonar <span className="ta-money">{formatMoney(typedCents / 100)}</span></>}
        </button>
      </div>
    </form>
  );
}
