import { useId } from 'react';
import type { PlatformSettlementAdjustment } from '../../api/platformSettlements';
import { PlatformSettlementAdjustmentKind } from '../../types';
import { formatMoney } from '../../utils/money';
import { MAX_ADJUSTMENTS, MAX_DESCRIPTION_LENGTH, checkAdjustment, newAdjustmentKey, type AdjustmentRow } from './settlementEdits';
import { ADJUSTMENT_KIND_LABELS } from './settlementLabels';

const KINDS = Object.values(PlatformSettlementAdjustmentKind);

interface SettlementAdjustmentsProps {
  rows: AdjustmentRow[];
  onChange: (rows: AdjustmentRow[]) => void;
  disabled: boolean;
  /** Every problem in red, once the person tried to save; before that, only those of boxes already written. */
  revealed: boolean;
}

/**
 * What explains the rest of the deposit: promotions, claims, cancellations, the platform's charges or a balance of another
 * week, each with its signed amount («-» when the platform takes it off) and «Otro» with what it was.
 */
export default function SettlementAdjustments({ rows, onChange, disabled, revealed }: SettlementAdjustmentsProps) {
  const id = useId();
  const change = (key: string, patch: Partial<AdjustmentRow>) =>
    onChange(rows.map((row) => (row.key === key ? { ...row, ...patch } : row)));

  return (
    <>
      {rows.length === 0 ? (
        <p className="text-muted small">Sin ajustes. Agrega uno por cada cosa que la plataforma descontó o sumó aparte de los pedidos.</p>
      ) : (
        <ol className="list-unstyled mb-2">
          {rows.map((row, index) => {
            const check = checkAdjustment(row);
            const number = index + 1;
            // A new row is not red before it is written: each problem shows once its box has something, or on saving.
            const kindProblem = revealed ? check.kindProblem : '';
            const amountProblem = revealed || row.amountText.trim() !== '' ? check.amountProblem : '';
            const descriptionProblem = revealed || row.description.trim() !== '' ? check.descriptionProblem : '';
            const field = (name: string) => `${id}-${name}-${number}`;
            return (
              <li key={row.key} className="row g-2 align-items-start mb-2">
                <div className="col-12 col-md-4">
                  <label className="form-label small mb-1" htmlFor={field('kind')}>Motivo del ajuste {number}</label>
                  <select
                    id={field('kind')}
                    className={`form-select form-select-sm ${kindProblem ? 'is-invalid' : ''}`}
                    value={row.kind}
                    disabled={disabled}
                    aria-invalid={kindProblem ? true : undefined}
                    aria-describedby={kindProblem ? field('kind-problem') : undefined}
                    onChange={(event) => change(row.key, {
                      kind: event.target.value === '' ? '' : (Number(event.target.value) as PlatformSettlementAdjustmentKind),
                    })}
                  >
                    <option value="">Elige el motivo</option>
                    {KINDS.map((kind) => <option key={kind} value={kind}>{ADJUSTMENT_KIND_LABELS[kind]}</option>)}
                  </select>
                  {kindProblem && <div id={field('kind-problem')} className="invalid-feedback d-block">{kindProblem}</div>}
                </div>
                <div className="col-12 col-md-4">
                  {/* Each box says its row to a screen reader, as «Motivo del ajuste 1» says it to everyone. */}
                  <label className="form-label small mb-1" htmlFor={field('description')}>
                    Descripción<span className="visually-hidden"> del ajuste {number}</span>
                    {row.kind === PlatformSettlementAdjustmentKind.Other ? '' : ' (opcional)'}
                  </label>
                  <input
                    id={field('description')}
                    className={`form-control form-control-sm ${descriptionProblem ? 'is-invalid' : ''}`}
                    maxLength={MAX_DESCRIPTION_LENGTH}
                    autoComplete="off"
                    value={row.description}
                    disabled={disabled}
                    aria-invalid={descriptionProblem ? true : undefined}
                    aria-describedby={descriptionProblem ? field('description-problem') : undefined}
                    onChange={(event) => change(row.key, { description: event.target.value })}
                  />
                  {descriptionProblem && <div id={field('description-problem')} className="invalid-feedback d-block">{descriptionProblem}</div>}
                </div>
                <div className="col-8 col-md-2">
                  <label className="form-label small mb-1" htmlFor={field('amount')}>
                    Importe<span className="visually-hidden"> del ajuste {number}</span>
                  </label>
                  {/* No decimal keyboard: on an iPhone it has no minus, and most adjustments take one. */}
                  <input
                    id={field('amount')}
                    className={`form-control form-control-sm text-end ${amountProblem ? 'is-invalid' : ''}`}
                    autoComplete="off"
                    placeholder="-0.00"
                    aria-invalid={amountProblem ? true : undefined}
                    aria-describedby={amountProblem ? `${field('amount-problem')} ${id}-sign` : `${id}-sign`}
                    value={row.amountText}
                    disabled={disabled}
                    onChange={(event) => change(row.key, { amountText: event.target.value })}
                  />
                  {amountProblem && <div id={field('amount-problem')} className="invalid-feedback d-block">{amountProblem}</div>}
                </div>
                <div className="col-4 col-md-2">
                  {/* A label's box (inline-block, as labels are), so the button lines up with the boxes beside it. */}
                  <span className="form-label small mb-1 d-inline-block invisible" aria-hidden="true">Quitar</span>
                  <button
                    type="button"
                    className="btn btn-light btn-sm w-100"
                    aria-label={`Quitar el ajuste ${number}`}
                    disabled={disabled}
                    onClick={() => onChange(rows.filter((other) => other.key !== row.key))}
                  >
                    Quitar
                  </button>
                </div>
              </li>
            );
          })}
        </ol>
      )}
      <div className="d-flex flex-wrap align-items-center gap-2">
        <button
          type="button"
          className="btn btn-light btn-sm"
          disabled={disabled || rows.length >= MAX_ADJUSTMENTS}
          onClick={() => onChange([...rows, { key: newAdjustmentKey(), kind: '', description: '', amountText: '' }])}
        >
          <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar ajuste
        </button>
        <span id={`${id}-sign`} className="small text-muted">Con «-» lo que la plataforma te descontó; sin signo, lo que te sumó.</span>
      </div>
    </>
  );
}

/** The adjustments of a settlement that no longer changes, as a list. */
export function SettlementAdjustmentList({ adjustments }: { adjustments: PlatformSettlementAdjustment[] }) {
  if (adjustments.length === 0) return <p className="text-muted small mb-0">Sin ajustes.</p>;
  return (
    <div className="table-responsive">
      <table className="table table-sm align-middle mb-0">
        <thead>
          <tr>
            <th scope="col">Motivo</th>
            <th scope="col">Descripción</th>
            <th scope="col" className="text-end">Importe</th>
          </tr>
        </thead>
        <tbody>
          {adjustments.map((adjustment, index) => (
            <tr key={index}>
              <td>{ADJUSTMENT_KIND_LABELS[adjustment.kind] ?? adjustment.kind}</td>
              <td>{adjustment.description || <span className="text-muted">—</span>}</td>
              <td className="text-end ta-money">{formatMoney(adjustment.amount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
