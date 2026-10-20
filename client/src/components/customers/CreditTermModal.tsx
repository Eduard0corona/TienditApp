import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useUpdateCreditTerm } from '../../api/customerCredit';
import { useToast } from '../../hooks/useToast';
import { creditTermProblem, describeTerm } from './customerAccount';

interface CreditTermModalProps {
  /** The term the business has now; null without one. */
  creditTermDays: number | null;
  onClose: () => void;
}

/**
 * How many days the business gives its customers to pay what they owe (fiado, Fase 26B; owners and admins). It only says
 * what is overdue in Por cobrar, the account and Vender: it charges no interest and stops no sale.
 */
export default function CreditTermModal({ creditTermDays, onClose }: CreditTermModalProps) {
  const titleId = useId();
  const toast = useToast();
  const update = useUpdateCreditTerm();
  const [typed, setTyped] = useState(creditTermDays != null ? String(creditTermDays) : '');
  const [error, setError] = useState('');
  const problem = creditTermProblem(typed);
  const showProblem = typed.trim() !== '' && !!problem;

  const save = async (days: number | null) => {
    setError('');
    try {
      await update.mutateAsync(days);
      toast.success(days == null ? 'El fiado ya no tiene plazo.' : `El plazo del fiado es de ${describeTerm(days)}.`);
      onClose();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar el plazo del fiado.'));
    }
  };

  return (
    <Modal show onHide={() => !update.isPending && onClose()} centered aria-labelledby={titleId}>
      <Modal.Header closeButton={!update.isPending}>
        <Modal.Title id={titleId} as="h2" className="h5">Plazo del fiado</Modal.Title>
      </Modal.Header>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          if (!problem) void save(Number(typed.trim()));
        }}
      >
        <Modal.Body>
          <label className="form-label fw-semibold" htmlFor="credit-term">Te pagan en</label>
          <div className="input-group input-group-lg">
            <input
              id="credit-term"
              className={`form-control ${showProblem ? 'is-invalid' : ''}`}
              inputMode="numeric"
              autoComplete="off"
              placeholder="15"
              value={typed}
              onChange={(event) => setTyped(event.target.value)}
              disabled={update.isPending}
              aria-invalid={showProblem || !!error}
              aria-describedby={['credit-term-help', showProblem && 'credit-term-problem', error && 'credit-term-error'].filter(Boolean).join(' ')}
            />
            <span className="input-group-text">días</span>
          </div>
          {showProblem && <div id="credit-term-problem" className="invalid-feedback d-block">{problem}</div>}
          <div id="credit-term-help" className="form-text">
            Se cuentan desde el día de la venta. Lo que pase del plazo se ve como vencido en Por cobrar, el estado de cuenta y
            Vender; no cobra intereses ni impide fiar.
          </div>
          {error && <div id="credit-term-error" className="alert alert-danger py-2 mt-3 mb-0">{error}</div>}
        </Modal.Body>
        <Modal.Footer className="flex-wrap">
          {creditTermDays != null && (
            <button type="button" className="btn btn-light text-danger me-auto" onClick={() => void save(null)} disabled={update.isPending}>
              Quitar plazo
            </button>
          )}
          <button type="button" className="btn btn-light" onClick={onClose} disabled={update.isPending}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={update.isPending || !!problem}>
            {update.isPending ? 'Guardando…' : 'Guardar plazo'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
