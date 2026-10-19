import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useUpdateCreditLimit } from '../../api/customerCredit';
import { useToast } from '../../hooks/useToast';
import { formatMoney, parseMoneyInput } from '../../utils/money';
import { creditLimitProblem } from './customerAccount';

interface CreditLimitModalProps {
  customer: { id: string; name: string; creditLimit?: number | null };
  onClose: () => void;
}

/**
 * How much the business lets a customer owe (owners and admins): with a limit, Vender offers «Fiar» to them; without one,
 * it never does. A limit below what they owe only stops selling them more on credit.
 */
export default function CreditLimitModal({ customer, onClose }: CreditLimitModalProps) {
  const titleId = useId();
  const toast = useToast();
  const update = useUpdateCreditLimit();
  const [typed, setTyped] = useState(customer.creditLimit != null ? String(customer.creditLimit) : '');
  const [error, setError] = useState('');
  const problem = creditLimitProblem(typed);

  const save = async (creditLimit: number | null) => {
    setError('');
    try {
      await update.mutateAsync({ customerId: customer.id, creditLimit });
      toast.success(creditLimit == null
        ? `${customer.name} ya no tiene fiado.`
        : `${customer.name} puede deber hasta ${formatMoney(creditLimit)}.`);
      onClose();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar el límite de fiado.'));
    }
  };

  return (
    <Modal show onHide={() => !update.isPending && onClose()} centered aria-labelledby={titleId}>
      <Modal.Header closeButton={!update.isPending}>
        <Modal.Title id={titleId} as="h2" className="h5">Fiado de {customer.name}</Modal.Title>
      </Modal.Header>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          if (!problem) void save(parseMoneyInput(typed));
        }}
      >
        <Modal.Body>
          <label className="form-label fw-semibold" htmlFor="credit-limit">Le fías hasta</label>
          <input
            id="credit-limit"
            className={`form-control form-control-lg ta-money ${typed.trim() !== '' && problem ? 'is-invalid' : ''}`}
            inputMode="decimal"
            autoComplete="off"
            placeholder="500"
            value={typed}
            onChange={(event) => setTyped(event.target.value)}
            disabled={update.isPending}
            aria-describedby="credit-limit-help"
          />
          {typed.trim() !== '' && problem && <div className="invalid-feedback">{problem}</div>}
          <div id="credit-limit-help" className="form-text">
            En Vender podrás fiarle mientras lo que debe, con la venta, no pase de este límite.
          </div>
          {error && <div className="alert alert-danger py-2 mt-3 mb-0">{error}</div>}
        </Modal.Body>
        <Modal.Footer className="flex-wrap">
          {customer.creditLimit != null && (
            <button type="button" className="btn btn-light text-danger me-auto" onClick={() => void save(null)} disabled={update.isPending}>
              Quitar fiado
            </button>
          )}
          <button type="button" className="btn btn-light" onClick={onClose} disabled={update.isPending}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={update.isPending || !!problem}>
            {update.isPending ? 'Guardando…' : 'Guardar límite'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
