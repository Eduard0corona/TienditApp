import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { quotesApi, type Quote } from '../../api/quotesApi';
import { businessDateTimeToIso } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

interface ConvertQuoteDialogProps {
  quote: Quote;
  /** Whether the person may charge: applying what was paid for the visit is a payment of the order (the server checks it). */
  canCharge: boolean;
  onClose: () => void;
  /** It became its service order: the quote as the server has it now. */
  onConverted: (converted: Quote) => void;
}

/**
 * The accepted quote becomes the job (Fase 29E, ADR 0053 point 17): a service order with its lines at the prices the customer
 * accepted, once. What was paid for the visit becomes a payment of the order, up to its total. When the job is promised is
 * optional, as in any service order, and is read in the business's time zone.
 */
export default function ConvertQuoteDialog({ quote, canCharge, onClose, onConverted }: ConvertQuoteDialogProps) {
  const titleId = useId();
  const hintId = useId();
  const queryClient = useQueryClient();
  const [promisedAt, setPromisedAt] = useState('');
  const [error, setError] = useState('');
  const lines = quote.lines.length;
  // What the order takes as paid: what is taken off for the visit, never more than the total.
  const credit = quote.visitCredit?.applies ? Math.min(quote.visitCredit.amount, quote.total) : 0;
  const needsCharge = credit > 0 && !canCharge;

  const convert = useMutation({
    mutationFn: () => quotesApi.convertToOrder(quote.id, promisedAt ? businessDateTimeToIso(promisedAt) : null),
    onSuccess: onConverted,
    onError: (err) => {
      setError(getApiErrorMessage(err, 'No se pudo convertir la cotización.'));
      // Someone else may have converted it meanwhile: the page shows the quote as it is now.
      void queryClient.invalidateQueries({ queryKey: ['quotes', 'detail', quote.id] });
    },
  });

  return (
    <Modal show onHide={() => !convert.isPending && onClose()} centered aria-labelledby={titleId}>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          setError('');
          convert.mutate();
        }}
      >
        <Modal.Header closeButton={!convert.isPending}>
          <Modal.Title id={titleId} as="h2" className="h5">Convertir en orden de servicio</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="mb-2">
            La orden lleva {lines === 1 ? 'su renglón' : `sus ${lines} renglones`} a los precios que el cliente aceptó:{' '}
            <span className="fw-semibold">{formatMoney(quote.total)}</span>.
          </p>
          {credit > 0 && (
            <p className="mb-2">
              Lo que pagó por la visita ({formatMoney(credit)}) queda como pago de la orden: falta cobrar{' '}
              <span className="fw-semibold">{formatMoney(quote.toPay)}</span>.
            </p>
          )}
          {needsCharge && (
            <div className="alert alert-warning small" role="status">
              Aplicar lo que pagó por la visita es cobrar: conviértela con alguien que pueda cobrar.
            </div>
          )}
          <fieldset disabled={convert.isPending}>
            <label className="form-label" htmlFor="quote-promised-at">Para cuándo es el trabajo</label>
            <input
              id="quote-promised-at"
              className="form-control"
              type="datetime-local"
              aria-describedby={hintId}
              value={promisedAt}
              onChange={(event) => setPromisedAt(event.target.value)}
            />
            <p id={hintId} className="form-text mb-0">Opcional: la orden sale con esa fecha en el tablero de servicios.</p>
          </fieldset>
          {error && <div className="alert alert-danger mt-3 mb-0" role="alert">{error}</div>}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose} disabled={convert.isPending}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={convert.isPending || needsCharge}>
            {convert.isPending ? 'Convirtiendo…' : 'Convertir'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
