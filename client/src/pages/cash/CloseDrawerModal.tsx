import { useId, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import type { CashCountLineDto, CashSessionDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { describeDifference, MXN_DENOMINATIONS, parseCashAmount } from './cashFormat';

interface CloseDrawerModalProps {
  session: CashSessionDto;
  submitting: boolean;
  error: string;
  onClose: () => void;
  onSubmit: (closingAmount: number, notes: string | undefined, cashCount: CashCountLineDto[] | undefined) => void;
}

/**
 * Closing a drawer: count by denomination (or type the total), see the difference against the expected
 * cash and explain it. The server keeps both amounts and the difference for the shift history.
 */
export default function CloseDrawerModal({ session, submitting, error, onClose, onSubmit }: CloseDrawerModalProps) {
  const titleId = useId();
  const [byDenomination, setByDenomination] = useState(true);
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [typedTotal, setTypedTotal] = useState('');
  const [notes, setNotes] = useState('');

  const countLines = useMemo<CashCountLineDto[]>(
    () => MXN_DENOMINATIONS
      .map((denomination) => ({ denomination, quantity: Number.parseInt(quantities[String(denomination)] ?? '', 10) || 0 }))
      .filter((line) => line.quantity > 0),
    [quantities],
  );
  // Cents avoid floating point drift when adding coins of $0.50.
  const countedCents = byDenomination
    ? countLines.reduce((sum, line) => sum + Math.round(line.denomination * 100) * line.quantity, 0)
    : Math.round((parseCashAmount(typedTotal) ?? Number.NaN) * 100);
  const counted = Number.isFinite(countedCents) ? countedCents / 100 : null;
  const expectedCents = Math.round(session.expectedBalance * 100);
  const difference = counted === null ? null : (countedCents - expectedCents) / 100;
  const needsNote = difference !== null && difference !== 0;
  const canSubmit = counted !== null && (!needsNote || notes.trim().length > 0) && !submitting;

  return (
    <Modal show onHide={onClose} centered size="lg" aria-labelledby={titleId}>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          if (!canSubmit || counted === null) return;
          onSubmit(counted, notes.trim() || undefined, byDenomination && countLines.length > 0 ? countLines : undefined);
        }}
      >
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">Cerrar caja</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <div className="d-flex flex-wrap gap-3 mb-3">
            <div>
              <div className="text-muted small">Efectivo esperado</div>
              <div className="fs-4 fw-bold ta-money">{formatMoney(session.expectedBalance)}</div>
            </div>
            <div>
              <div className="text-muted small">Contado</div>
              <div className="fs-4 fw-bold ta-money">{counted === null ? '—' : formatMoney(counted)}</div>
            </div>
            <div>
              <div className="text-muted small">Diferencia</div>
              <div className={`fs-4 fw-bold ${difference === null || difference === 0 ? '' : difference < 0 ? 'text-danger' : 'text-warning'}`}>
                {difference === null ? '—' : describeDifference(difference, formatMoney)}
              </div>
            </div>
          </div>

          <div className="btn-group mb-3" role="group" aria-label="Forma de contar">
            <button type="button" className={`btn btn-sm ${byDenomination ? 'btn-primary' : 'btn-outline-secondary'}`} onClick={() => setByDenomination(true)}>
              Contar billetes y monedas
            </button>
            <button type="button" className={`btn btn-sm ${byDenomination ? 'btn-outline-secondary' : 'btn-primary'}`} onClick={() => setByDenomination(false)}>
              Escribir el total
            </button>
          </div>

          {byDenomination ? (
            <div className="row g-2">
              {MXN_DENOMINATIONS.map((denomination) => {
                const key = String(denomination);
                return (
                  <div key={key} className="col-6 col-md-4">
                    <label className="form-label small mb-1" htmlFor={`count-${key}`}>{formatMoney(denomination)}</label>
                    <input
                      id={`count-${key}`}
                      className="form-control"
                      inputMode="numeric"
                      placeholder="0"
                      value={quantities[key] ?? ''}
                      onChange={(event) => {
                        const digits = event.target.value.replace(/\D/g, '').slice(0, 6);
                        setQuantities((current) => ({ ...current, [key]: digits }));
                      }}
                    />
                  </div>
                );
              })}
            </div>
          ) : (
            <div>
              <label className="form-label" htmlFor="close-total">Efectivo contado</label>
              <input
                id="close-total"
                className="form-control form-control-lg ta-money"
                inputMode="decimal"
                placeholder={formatMoney(session.expectedBalance)}
                value={typedTotal}
                onChange={(event) => setTypedTotal(event.target.value)}
                autoFocus
              />
            </div>
          )}

          <div className="mt-3">
            <label className="form-label" htmlFor="close-notes">
              Notas {needsNote ? '(explica la diferencia)' : '(opcional)'}
            </label>
            <textarea
              id="close-notes"
              className={`form-control ${needsNote && notes.trim() === '' ? 'is-invalid' : ''}`}
              rows={2}
              maxLength={2000}
              value={notes}
              onChange={(event) => setNotes(event.target.value)}
            />
            {needsNote && notes.trim() === '' && (
              <div className="invalid-feedback">Para cerrar con diferencia escribe qué pasó.</div>
            )}
          </div>
          {error && <div className="alert alert-danger mt-3 mb-0">{error}</div>}
          <p className="text-muted small mt-3 mb-0">El cierre no se puede deshacer.</p>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose} disabled={submitting}>Volver</button>
          <button type="submit" className={`btn ${needsNote ? 'btn-danger' : 'btn-primary'}`} disabled={!canSubmit}>
            {submitting ? 'Cerrando…' : 'Cerrar caja'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
