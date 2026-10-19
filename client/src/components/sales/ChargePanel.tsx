import { useMemo, useState } from 'react';
import type { OrderPaymentMethod } from '../../types';
import { OrderPaymentMethod as PaymentMethod } from '../../types';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney, parseMoneyInput, toCents } from '../../utils/money';

export interface ChargePayment {
  method: OrderPaymentMethod;
  amount: number;
  reference?: string;
  evidenceUrl?: string;
  /** On top of the amount, for the team (kept with the payment, never part of the sale). */
  tip?: number;
  /** Cash handed over for this payment (cash only); the change is the rest. */
  cashReceived?: number;
}

export interface ChargeResult {
  payments: ChargePayment[];
  /** Cash handed back to the customer (0 when there is no cash or it was exact). */
  change: number;
  /** Cash the customer handed over, to print on the ticket. */
  cashReceived: number;
  /** Tip given with the charge (0 without one). */
  tip: number;
  /** The rest was sold on credit (fiado): the payments are what the customer paid now, possibly none. */
  onCredit?: boolean;
}

/** The customer of the sale may owe the rest (fiado, Fase 26A): who, and how much they may still owe. */
export interface ChargeCredit {
  name: string;
  available: number;
}

type TipChoice = 'none' | 10 | 15 | 'other';

const TIP_CHOICES: Array<{ value: TipChoice; label: string }> = [
  { value: 'none', label: 'Sin propina' },
  { value: 10, label: '10 %' },
  { value: 15, label: '15 %' },
  { value: 'other', label: 'Otra' },
];

interface ChargePanelProps {
  /** What has to be collected now: the sale total or an order's balance. */
  amountDue: number;
  submitting: boolean;
  /** Existing orders accept a partial payment ("abono"); a sale is charged in full. */
  allowPartial?: boolean;
  confirmLabel?: string;
  error?: string;
  onSubmit: (result: ChargeResult) => void;
  onCancel: () => void;
  /** Uploads a transfer receipt; without it the panel does not offer one. */
  uploadEvidence?: (file: File) => Promise<{ url: string; fileName: string }>;
  /** Offers a tip on top of what is owed (the counter, the tables and the orders). */
  allowTip?: boolean;
  /** Offers «Fiar» the rest to this customer, within what they may still owe (the server checks it again). */
  credit?: ChargeCredit;
}

const METHODS: Array<{ value: OrderPaymentMethod; icon: string }> = [
  { value: PaymentMethod.Cash, icon: 'ri-money-dollar-circle-line' },
  { value: PaymentMethod.Card, icon: 'ri-bank-card-line' },
  { value: PaymentMethod.Transfer, icon: 'ri-exchange-dollar-line' },
  { value: PaymentMethod.DigitalWallet, icon: 'ri-smartphone-line' },
  { value: PaymentMethod.Other, icon: 'ri-more-line' },
];

const BILLS = [20, 50, 100, 200, 500, 1000];

/** Quick amounts for cash: exact, the next round amounts and the bills above what is owed. */
function getCashSuggestions(remainingCents: number): number[] {
  if (remainingCents <= 0) return [];
  const remaining = remainingCents / 100;
  const candidates = [
    Math.ceil(remaining / 10) * 10,
    Math.ceil(remaining / 50) * 50,
    ...BILLS.filter((bill) => bill > remaining),
  ];
  return [...new Set(candidates)].filter((amount) => toCents(amount) > remainingCents).sort((a, b) => a - b).slice(0, 4);
}

/**
 * Collects a payment at the counter: cash received and change, card or transfer with reference, and
 * mixed payments. Amounts are handled in cents; the server validates them again.
 */
export default function ChargePanel({
  amountDue,
  submitting,
  allowPartial = false,
  confirmLabel = 'Cobrar',
  error,
  onSubmit,
  onCancel,
  uploadEvidence,
  allowTip = true,
  credit,
}: ChargePanelProps) {
  const [added, setAdded] = useState<ChargePayment[]>([]);
  const [method, setMethod] = useState<OrderPaymentMethod>(PaymentMethod.Cash);
  const [cashReceived, setCashReceived] = useState('');
  const [amountInput, setAmountInput] = useState('');
  const [reference, setReference] = useState('');
  const [evidence, setEvidence] = useState<{ url: string; fileName: string } | null>(null);
  const [uploading, setUploading] = useState(false);
  const [localError, setLocalError] = useState('');
  const [tipChoice, setTipChoice] = useState<TipChoice>('none');
  const [tipInput, setTipInput] = useState('');

  const dueCents = toCents(amountDue);
  const addedCents = added.reduce((sum, payment) => sum + toCents(payment.amount), 0);
  const remainingCents = Math.max(0, dueCents - addedCents);
  const isCash = method === PaymentMethod.Cash;

  // The tip goes on top of what is owed, with the last payment of the charge.
  const tipCents = !allowTip || tipChoice === 'none'
    ? 0
    : tipChoice === 'other'
      ? Math.max(0, toCents(parseMoneyInput(tipInput) ?? 0))
      : Math.round((dueCents * tipChoice) / 100);
  const tipIsValid = tipChoice !== 'other' || tipInput.trim() === '' || parseMoneyInput(tipInput) !== null;

  // Cash: what was handed over covers the remaining amount (and the tip) and the difference is change. Left empty,
  // the customer paid the exact amount (one tap less for the most common sale).
  const cashTyped = cashReceived.trim() !== '';
  const receivedCents = cashTyped ? toCents(parseMoneyInput(cashReceived) ?? 0) : remainingCents + tipCents;
  const cashAppliedCents = Math.min(Math.max(0, receivedCents - tipCents), remainingCents);
  const changeCents = Math.max(0, receivedCents - remainingCents - tipCents);
  // Card, transfer and others: the amount typed, never above what is owed (there is no change).
  const typedCents = amountInput.trim() === '' ? remainingCents : toCents(parseMoneyInput(amountInput) ?? -1);
  const currentCents = isCash ? cashAppliedCents : typedCents;
  const currentIsValid = isCash ? true : typedCents >= 0 && typedCents <= remainingCents;
  const coveredCents = addedCents + (currentIsValid ? currentCents : 0);
  const missingCents = Math.max(0, dueCents - coveredCents);
  // More tip than bill is a typing mistake (the server refuses it too).
  const tipFits = tipCents <= dueCents;
  const canSubmit = !submitting && !uploading && currentIsValid && tipIsValid && tipFits
    && (allowPartial ? coveredCents > 0 : missingCents === 0);
  const canAddAnother = currentIsValid && currentCents > 0 && currentCents < remainingCents;
  // On credit, what the customer pays now is what was added and typed: an empty field is no payment here (charging in
  // full reads it as the exact amount). The rest is what they would owe; a tip has no payment to go with.
  const typedNowCents = !currentIsValid ? 0 : isCash ? (cashTyped ? cashAppliedCents : 0) : (amountInput.trim() === '' ? 0 : typedCents);
  const creditCents = credit ? Math.max(0, dueCents - addedCents - typedNowCents) : 0;
  const creditShortCents = credit ? Math.max(0, creditCents - toCents(credit.available)) : 0;
  const canSellOnCredit = !!credit && !submitting && !uploading && currentIsValid && creditCents > 0
    && creditShortCents === 0 && tipCents === 0;
  const suggestions = useMemo(() => getCashSuggestions(remainingCents + tipCents), [remainingCents, tipCents]);

  const resetCurrent = () => {
    setCashReceived('');
    setAmountInput('');
    setReference('');
    setEvidence(null);
    setLocalError('');
  };

  const buildCurrent = (withTip = false): ChargePayment | null => {
    if (!currentIsValid || currentCents <= 0) return null;
    const tip = withTip ? tipCents : 0;
    return {
      method,
      amount: currentCents / 100,
      reference: reference.trim() || undefined,
      evidenceUrl: evidence?.url,
      tip: tip > 0 ? tip / 100 : undefined,
      // What was handed over for this payment: typed, or exactly the amount plus the tip.
      cashReceived: isCash ? (cashTyped ? receivedCents : currentCents + tip) / 100 : undefined,
    };
  };

  const addAnother = () => {
    const current = buildCurrent();
    if (!current) return;
    setAdded((payments) => [...payments, current]);
    resetCurrent();
    // The rest is usually paid another way: offer cash after a card, card after cash.
    setMethod(method === PaymentMethod.Cash ? PaymentMethod.Card : PaymentMethod.Cash);
  };

  const submit = () => {
    if (!canSubmit) {
      if (!currentIsValid) setLocalError('El monto no puede ser mayor a lo que falta por cobrar.');
      else if (!tipIsValid) setLocalError('Escribe una propina válida, por ejemplo 20 o 15.50.');
      else if (!tipFits) setLocalError('La propina no puede ser mayor que el total de la cuenta.');
      else if (missingCents > 0) setLocalError(`Faltan ${formatMoney(missingCents / 100)} por cobrar.`);
      return;
    }
    const current = buildCurrent(true);
    const payments = current ? [...added, current] : added;
    onSubmit({
      payments,
      change: isCash ? changeCents / 100 : 0,
      cashReceived: isCash ? receivedCents / 100 : 0,
      tip: current?.tip ?? 0,
    });
  };

  const submitOnCredit = () => {
    if (!canSellOnCredit) return;
    const current = typedNowCents > 0 ? buildCurrent() : null;
    onSubmit({
      payments: current ? [...added, current] : added,
      change: isCash && current ? changeCents / 100 : 0,
      cashReceived: isCash && current ? receivedCents / 100 : 0,
      tip: 0,
      onCredit: true,
    });
  };

  const handleEvidence = async (file: File | undefined) => {
    if (!file || !uploadEvidence) return;
    setUploading(true);
    setLocalError('');
    try {
      setEvidence(await uploadEvidence(file));
    } catch {
      setLocalError('No se pudo subir el comprobante. Intenta de nuevo.');
    } finally {
      setUploading(false);
    }
  };

  return (
    <form
      className="charge-panel"
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      <div className="charge-due">
        <div className="text-muted small">{added.length > 0 ? 'Falta por cobrar' : 'Total a cobrar'}</div>
        <div className="charge-due-amount ta-money">{formatMoney(remainingCents / 100)}</div>
        {added.length > 0 && (
          <div className="small text-muted">
            Total {formatMoney(amountDue)} · pagado {formatMoney(addedCents / 100)}
          </div>
        )}
      </div>

      {added.length > 0 && (
        <ul className="list-unstyled charge-added mb-3">
          {added.map((payment, index) => (
            <li key={`${payment.method}-${index}`} className="d-flex align-items-center justify-content-between gap-2">
              <span>
                {getPaymentMethodLabel(payment.method)}
                {payment.reference && <span className="text-muted small"> · {payment.reference}</span>}
              </span>
              <span className="d-flex align-items-center gap-2">
                <strong className="ta-money">{formatMoney(payment.amount)}</strong>
                <button
                  type="button"
                  className="btn btn-sm btn-light"
                  aria-label={`Quitar pago con ${getPaymentMethodLabel(payment.method)}`}
                  onClick={() => setAdded((payments) => payments.filter((_, i) => i !== index))}
                  disabled={submitting}
                >
                  <i className="ri-close-line" aria-hidden="true"></i>
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}

      <div className="charge-methods mb-3" role="radiogroup" aria-label="Forma de pago">
        {METHODS.map((item) => (
          <button
            key={item.value}
            type="button"
            role="radio"
            aria-checked={method === item.value}
            className={`btn btn-touch ${method === item.value ? 'btn-primary' : 'btn-outline-secondary'}`}
            onClick={() => {
              setMethod(item.value);
              resetCurrent();
            }}
            disabled={submitting}
          >
            <i className={`${item.icon} me-1`} aria-hidden="true"></i>
            {getPaymentMethodLabel(item.value)}
          </button>
        ))}
      </div>

      {isCash ? (
        <div className="mb-2">
          <label className="form-label fw-semibold" htmlFor="charge-cash-received">Efectivo recibido</label>
          <input
            id="charge-cash-received"
            className="form-control form-control-lg ta-money"
            inputMode="decimal"
            autoComplete="off"
            autoFocus
            placeholder={formatMoney((remainingCents + tipCents) / 100)}
            value={cashReceived}
            onChange={(event) => setCashReceived(event.target.value)}
            disabled={submitting}
          />
          <div className="d-flex flex-wrap gap-2 mt-2">
            <button
              type="button"
              className="btn btn-light btn-touch"
              onClick={() => setCashReceived(((remainingCents + tipCents) / 100).toFixed(2))}
              disabled={submitting || remainingCents === 0}
            >
              Exacto
            </button>
            {suggestions.map((amount) => (
              <button
                key={amount}
                type="button"
                className="btn btn-light btn-touch ta-money"
                onClick={() => setCashReceived(String(amount))}
                disabled={submitting}
              >
                {formatMoney(amount)}
              </button>
            ))}
          </div>
          {cashReceived.trim() !== '' && parseMoneyInput(cashReceived) === null && (
            <div className="text-danger small mt-1">Escribe un monto válido, por ejemplo 200 o 150.50.</div>
          )}
          {cashTyped && receivedCents > 0 && (
            <div className={`charge-change mt-3 ${changeCents > 0 ? 'has-change' : ''}`} aria-live="polite">
              {receivedCents >= remainingCents + tipCents ? (
                <>
                  <span>Cambio</span>
                  <strong className="ta-money">{formatMoney(changeCents / 100)}</strong>
                </>
              ) : (
                <>
                  <span>Faltan</span>
                  <strong className="ta-money text-danger">{formatMoney((remainingCents + tipCents - receivedCents) / 100)}</strong>
                </>
              )}
            </div>
          )}
        </div>
      ) : (
        <div className="row g-2 mb-2">
          <div className="col-sm-6">
            <label className="form-label fw-semibold" htmlFor="charge-amount">Monto</label>
            <input
              id="charge-amount"
              className={`form-control form-control-lg ta-money ${currentIsValid ? '' : 'is-invalid'}`}
              inputMode="decimal"
              autoComplete="off"
              placeholder={(remainingCents / 100).toFixed(2)}
              value={amountInput}
              onChange={(event) => setAmountInput(event.target.value)}
              disabled={submitting}
            />
            {!currentIsValid && <div className="invalid-feedback">No puede ser mayor a lo que falta.</div>}
          </div>
          <div className="col-sm-6">
            <label className="form-label fw-semibold" htmlFor="charge-reference">
              {method === PaymentMethod.Card ? 'Autorización o últimos 4 dígitos' : 'Referencia'} (opcional)
            </label>
            <input
              id="charge-reference"
              className="form-control form-control-lg"
              maxLength={120}
              value={reference}
              onChange={(event) => setReference(event.target.value)}
              disabled={submitting}
            />
          </div>
          {method === PaymentMethod.Transfer && uploadEvidence && (
            <div className="col-12">
              <label className="form-label" htmlFor="charge-evidence">Comprobante (imagen o PDF, opcional)</label>
              <input
                id="charge-evidence"
                type="file"
                className="form-control"
                accept="image/*,.pdf"
                disabled={submitting || uploading}
                onChange={(event) => {
                  void handleEvidence(event.target.files?.[0]);
                  event.currentTarget.value = '';
                }}
              />
              {uploading && <div className="small text-muted mt-1">Subiendo comprobante…</div>}
              {evidence && <div className="small text-success mt-1">Listo: {evidence.fileName}</div>}
            </div>
          )}
        </div>
      )}

      {allowTip && (
        <fieldset className="charge-tip mt-3">
          <legend className="form-label fw-semibold fs-6 mb-1">Propina</legend>
          <div className="d-flex flex-wrap gap-2" role="radiogroup" aria-label="Propina">
            {TIP_CHOICES.map((choice) => (
              <button
                key={String(choice.value)}
                type="button"
                role="radio"
                aria-checked={tipChoice === choice.value}
                className={`btn btn-sm ${tipChoice === choice.value ? 'btn-primary' : 'btn-outline-secondary'}`}
                onClick={() => {
                  setTipChoice(choice.value);
                  setLocalError('');
                }}
                disabled={submitting}
              >
                {choice.label}
              </button>
            ))}
          </div>
          {tipChoice === 'other' && (
            <input
              className={`form-control mt-2 ta-money ${tipIsValid ? '' : 'is-invalid'}`}
              inputMode="decimal"
              autoComplete="off"
              aria-label="Monto de la propina"
              placeholder="0.00"
              value={tipInput}
              onChange={(event) => setTipInput(event.target.value)}
              disabled={submitting}
            />
          )}
          {tipCents > 0 && (
            <div className="small text-muted mt-1">
              Propina <span className="ta-money">{formatMoney(tipCents / 100)}</span> · total con propina{' '}
              <span className="ta-money">{formatMoney((remainingCents + tipCents) / 100)}</span>
            </div>
          )}
        </fieldset>
      )}

      {credit && (
        <p className="charge-credit small mt-3 mb-0">
          <i className="ri-hand-coin-line me-1" aria-hidden="true"></i>
          <strong>{credit.name}</strong> tiene fiado: le quedan <span className="ta-money">{formatMoney(credit.available)}</span>.
          {creditShortCents > 0 && (
            <> Para fiarle el resto, cobra al menos <span className="ta-money">{formatMoney(creditShortCents / 100)}</span>.</>
          )}
          {creditShortCents === 0 && tipCents > 0 && <> Para fiar, quita la propina.</>}
        </p>
      )}

      {(localError || error) && <div className="alert alert-danger py-2 mt-2 mb-0">{localError || error}</div>}

      <div className="charge-actions mt-3">
        <button type="button" className="btn btn-light btn-touch" onClick={onCancel} disabled={submitting}>
          Volver
        </button>
        {canAddAnother && (
          <button type="button" className="btn btn-outline-primary btn-touch" onClick={addAnother} disabled={submitting}>
            <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar otro pago
          </button>
        )}
        {credit && creditCents > 0 && (
          <button type="button" className="btn btn-outline-primary btn-touch" onClick={submitOnCredit} disabled={!canSellOnCredit}>
            <i className="ri-hand-coin-line me-1" aria-hidden="true"></i>Fiar <span className="ta-money">{formatMoney(creditCents / 100)}</span>
          </button>
        )}
        <button type="submit" className="btn btn-primary btn-touch btn-touch-lg flex-grow-1" disabled={!canSubmit}>
          {/* What the customer pays now: the tip goes on top of the sale. */}
          {submitting ? 'Cobrando…' : `${confirmLabel} ${formatMoney(((allowPartial ? coveredCents : dueCents) + tipCents) / 100)}`}
        </button>
      </div>
    </form>
  );
}
