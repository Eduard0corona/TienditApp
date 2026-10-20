import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useCreditReminders, useSetCustomerCreditTerm, useUpdateCreditLimit } from '../../api/customerCredit';
import { useToast } from '../../hooks/useToast';
import { formatMoney, parseMoneyInput } from '../../utils/money';
import { creditLimitProblem, creditTermProblem, describeTerm } from './customerAccount';

interface CreditLimitModalProps {
  customer: { id: string; name: string; creditLimit?: number | null; creditTermDays?: number | null };
  onClose: () => void;
}

/**
 * How much the business lets a customer owe (owners and admins): with a limit, Vender offers «Fiar» to them; without one,
 * it never does. A limit below what they owe only stops selling them more on credit. Fase 26C: how many days this
 * customer has to pay, the business's term or one of their own, which also counts for orders handed over unpaid.
 */
export default function CreditLimitModal({ customer, onClose }: CreditLimitModalProps) {
  const titleId = useId();
  const ids = { ownTerm: useId(), businessTerm: useId(), days: useId(), termProblem: useId() };
  const toast = useToast();
  const updateLimit = useUpdateCreditLimit();
  const setTerm = useSetCustomerCreditTerm();
  const reminders = useCreditReminders();
  const businessTerm = reminders.data?.creditTermDays ?? null;
  const [typed, setTyped] = useState(customer.creditLimit != null ? String(customer.creditLimit) : '');
  const [ownTerm, setOwnTerm] = useState(customer.creditTermDays != null);
  const [typedDays, setTypedDays] = useState(customer.creditTermDays != null ? String(customer.creditTermDays) : '');
  // A limit saved before the term was refused: a retry sends only what is missing.
  const [savedLimit, setSavedLimit] = useState<number | null>(null);
  const [error, setError] = useState<{ field: 'limit' | 'term'; message: string } | null>(null);
  const busy = updateLimit.isPending || setTerm.isPending;
  // A customer without fiado may still owe orders handed over unpaid: their term can change without a limit.
  const limitProblem = typed.trim() === '' && customer.creditLimit == null ? null : creditLimitProblem(typed);
  const showLimitProblem = typed.trim() !== '' && !!limitProblem;
  const termProblem = ownTerm ? creditTermProblem(typedDays) : null;
  const showTermProblem = ownTerm && typedDays.trim() !== '' && !!termProblem;
  const termDays = ownTerm && !termProblem ? Number(typedDays.trim()) : null;

  const save = async () => {
    setError(null);
    const limit = typed.trim() === '' ? null : parseMoneyInput(typed);
    const changes: string[] = [];
    let limitSaved = false;
    try {
      if (limit != null && limit !== (savedLimit ?? customer.creditLimit)) {
        await updateLimit.mutateAsync({ customerId: customer.id, creditLimit: limit });
        setSavedLimit(limit);
        limitSaved = true;
        changes.push(`puede deber hasta ${formatMoney(limit)}`);
      }
    } catch (err: unknown) {
      setError({ field: 'limit', message: getApiErrorMessage(err, 'No se pudo guardar el límite de fiado.') });
      return;
    }
    try {
      if (termDays !== (customer.creditTermDays ?? null)) {
        await setTerm.mutateAsync({ customerId: customer.id, creditTermDays: termDays });
        changes.push(termDays != null ? `paga en ${describeTerm(termDays)}` : 'paga en el plazo del negocio');
      }
    } catch (err: unknown) {
      const reason = getApiErrorMessage(err, 'No se pudo guardar el plazo.');
      setError({ field: 'term', message: limitSaved ? `Se guardó el límite de ${formatMoney(limit ?? 0)}, pero el plazo no. ${reason}` : reason });
      return;
    }
    if (changes.length > 0) toast.success(`${customer.name} ${changes.join(' y ')}.`);
    onClose();
  };

  const removeCredit = async () => {
    setError(null);
    try {
      await updateLimit.mutateAsync({ customerId: customer.id, creditLimit: null });
      // Their own term stays: it also counts for orders handed over unpaid.
      toast.success(customer.creditTermDays != null
        ? `${customer.name} ya no tiene fiado. Lo que deba lo sigue pagando en ${describeTerm(customer.creditTermDays)}.`
        : `${customer.name} ya no tiene fiado.`);
      onClose();
    } catch (err: unknown) {
      setError({ field: 'limit', message: getApiErrorMessage(err, 'No se pudo guardar el límite de fiado.') });
    }
  };

  return (
    <Modal show onHide={() => !busy && onClose()} centered aria-labelledby={titleId}>
      <Modal.Header closeButton={!busy}>
        <Modal.Title id={titleId} as="h2" className="h5">Fiado de {customer.name}</Modal.Title>
      </Modal.Header>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          if (!limitProblem && !termProblem) void save();
        }}
      >
        <Modal.Body>
          <label className="form-label fw-semibold" htmlFor="credit-limit">Le fías hasta</label>
          <input
            id="credit-limit"
            className={`form-control form-control-lg ta-money ${showLimitProblem ? 'is-invalid' : ''}`}
            inputMode="decimal"
            autoComplete="off"
            placeholder="500"
            value={typed}
            onChange={(event) => setTyped(event.target.value)}
            disabled={busy}
            aria-invalid={showLimitProblem || error?.field === 'limit'}
            aria-describedby={['credit-limit-help', showLimitProblem && 'credit-limit-problem', error?.field === 'limit' && 'credit-limit-error'].filter(Boolean).join(' ')}
          />
          {showLimitProblem && <div id="credit-limit-problem" className="invalid-feedback">{limitProblem}</div>}
          <div id="credit-limit-help" className="form-text">
            En Vender podrás fiarle mientras lo que debe, con la venta, no pase de este límite.
          </div>

          <fieldset className="mt-4">
            <legend className="form-label fw-semibold fs-6 mb-2">Te paga en</legend>
            <div className="form-check">
              <input
                id={ids.businessTerm}
                type="radio"
                name="credit-term"
                className="form-check-input"
                checked={!ownTerm}
                onChange={() => setOwnTerm(false)}
                disabled={busy}
              />
              <label className="form-check-label" htmlFor={ids.businessTerm}>
                El plazo del negocio
                <span className="text-muted">
                  {reminders.data ? (businessTerm != null ? ` · ${describeTerm(businessTerm)}` : ' · sin plazo') : ''}
                </span>
              </label>
            </div>
            <div className="form-check d-flex flex-wrap align-items-center gap-2">
              <input
                id={ids.ownTerm}
                type="radio"
                name="credit-term"
                className="form-check-input"
                checked={ownTerm}
                onChange={() => setOwnTerm(true)}
                disabled={busy}
              />
              <label className="form-check-label" htmlFor={ids.ownTerm}>Un plazo para este cliente</label>
              {ownTerm && (
                <div className="input-group input-group-sm" style={{ maxWidth: '9rem' }}>
                  <input
                    id={ids.days}
                    className={`form-control ${showTermProblem ? 'is-invalid' : ''}`}
                    inputMode="numeric"
                    autoComplete="off"
                    placeholder="7"
                    value={typedDays}
                    onChange={(event) => setTypedDays(event.target.value)}
                    disabled={busy}
                    autoFocus={customer.creditTermDays == null}
                    aria-label="Días para pagar"
                    aria-invalid={showTermProblem || error?.field === 'term'}
                    aria-describedby={[showTermProblem && ids.termProblem, error?.field === 'term' && 'credit-limit-error'].filter(Boolean).join(' ') || undefined}
                  />
                  <span className="input-group-text">días</span>
                </div>
              )}
            </div>
            {showTermProblem && <div id={ids.termProblem} className="invalid-feedback d-block">{termProblem}</div>}
            <div className="form-text">
              Cuenta desde que empieza a deber: el día de la venta fiada o de la entrega sin cobrar. Lo que pase del plazo se
              ve como vencido y, si tienes recordatorios, se le recuerda por WhatsApp.
            </div>
          </fieldset>
          {error && <div id="credit-limit-error" className="alert alert-danger py-2 mt-3 mb-0" role="alert">{error.message}</div>}
        </Modal.Body>
        <Modal.Footer className="flex-wrap">
          {customer.creditLimit != null && (
            <button type="button" className="btn btn-light text-danger me-auto" onClick={() => void removeCredit()} disabled={busy}>
              Quitar fiado
            </button>
          )}
          <button type="button" className="btn btn-light" onClick={onClose} disabled={busy}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={busy || !!limitProblem || !!termProblem}>
            {busy ? 'Guardando…' : 'Guardar'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
