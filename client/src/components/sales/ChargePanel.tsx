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
}

export interface ChargeResult {
  payments: ChargePayment[];
  /** Cash handed back to the customer (0 when there is no cash or it was exact). */
  change: number;
  /** Cash the customer handed over, to print on the ticket. */
  cashReceived: number;
}

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
}: ChargePanelProps) {
  const [added, setAdded] = useState<ChargePayment[]>([]);
  const [method, setMethod] = useState<OrderPaymentMethod>(PaymentMethod.Cash);
  const [cashReceived, setCashReceived] = useState('');
  const [amountInput, setAmountInput] = useState('');
  const [reference, setReference] = useState('');
  const [evidence, setEvidence] = useState<{ url: string; fileName: string } | null>(null);
  const [uploading, setUploading] = useState(false);
  const [localError, setLocalError] = useState('');

  const dueCents = toCents(amountDue);
  const addedCents = added.reduce((sum, payment) => sum + toCents(payment.amount), 0);
  const remainingCents = Math.max(0, dueCents - addedCents);
  const isCash = method === PaymentMethod.Cash;

  // Cash: what was handed over covers the remaining amount and the difference is change. Left empty, the
  // customer paid the exact amount (one tap less for the most common sale).
  const cashTyped = cashReceived.trim() !== '';
  const receivedCents = cashTyped ? toCents(parseMoneyInput(cashReceived) ?? 0) : remainingCents;
  const cashAppliedCents = Math.min(receivedCents, remainingCents);
  const changeCents = Math.max(0, receivedCents - remainingCents);
  // Card, transfer and others: the amount typed, never above what is owed (there is no change).
  const typedCents = amountInput.trim() === '' ? remainingCents : toCents(parseMoneyInput(amountInput) ?? -1);
  const currentCents = isCash ? cashAppliedCents : typedCents;
  const currentIsValid = isCash ? true : typedCents >= 0 && typedCents <= remainingCents;
  const coveredCents = addedCents + (currentIsValid ? currentCents : 0);
  const missingCents = Math.max(0, dueCents - coveredCents);
  const canSubmit = !submitting && !uploading && currentIsValid
    && (allowPartial ? coveredCents > 0 : missingCents === 0);
  const canAddAnother = currentIsValid && currentCents > 0 && currentCents < remainingCents;
  const suggestions = useMemo(() => getCashSuggestions(remainingCents), [remainingCents]);

  const resetCurrent = () => {
    setCashReceived('');
    setAmountInput('');
    setReference('');
    setEvidence(null);
    setLocalError('');
  };

  const buildCurrent = (): ChargePayment | null => {
    if (!currentIsValid || currentCents <= 0) return null;
    return {
      method,
      amount: currentCents / 100,
      reference: reference.trim() || undefined,
      evidenceUrl: evidence?.url,
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
      else if (missingCents > 0) setLocalError(`Faltan ${formatMoney(missingCents / 100)} por cobrar.`);
      return;
    }
    const current = buildCurrent();
    const payments = current ? [...added, current] : added;
    onSubmit({
      payments,
      change: isCash ? changeCents / 100 : 0,
      cashReceived: isCash ? receivedCents / 100 : 0,
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
            placeholder={formatMoney(remainingCents / 100)}
            value={cashReceived}
            onChange={(event) => setCashReceived(event.target.value)}
            disabled={submitting}
          />
          <div className="d-flex flex-wrap gap-2 mt-2">
            <button
              type="button"
              className="btn btn-light btn-touch"
              onClick={() => setCashReceived((remainingCents / 100).toFixed(2))}
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
              {receivedCents >= remainingCents ? (
                <>
                  <span>Cambio</span>
                  <strong className="ta-money">{formatMoney(changeCents / 100)}</strong>
                </>
              ) : (
                <>
                  <span>Faltan</span>
                  <strong className="ta-money text-danger">{formatMoney((remainingCents - receivedCents) / 100)}</strong>
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
        <button type="submit" className="btn btn-primary btn-touch btn-touch-lg flex-grow-1" disabled={!canSubmit}>
          {submitting ? 'Cobrando…' : `${confirmLabel} ${formatMoney((allowPartial ? coveredCents : dueCents) / 100)}`}
        </button>
      </div>
    </form>
  );
}
