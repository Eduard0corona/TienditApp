import { useId, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useCreateCourierSettlement, type CourierOrder, type CourierSettlementDetail } from '../../api/courierSettlements';
import { CourierSettlementAdjustmentKind, DeliveryCourier, OrderPaymentMethod } from '../../types';
import { formatDateTime } from '../../utils/dates';
import { DELIVERY_COURIER_LABELS } from '../../utils/labels';
import { formatMoney, formatSignedMoney } from '../../utils/money';
import {
  ADJUSTMENT_KIND_LABELS,
  HANDED_AMOUNT_MESSAGE,
  MAX_ADJUSTMENTS,
  MAX_DESCRIPTION_LENGTH,
  MAX_NOTES_LENGTH,
  MAX_REFERENCE_LENGTH,
  NO_ORDERS_MESSAGE,
  adjustmentForDifference,
  checkAdjustment,
  doesNotBalanceMessage,
  isDeduction,
  newAdjustment,
  summarize,
  toBody,
  type AdjustmentRow,
} from './courierSettlementForm';

const KINDS = Object.values(CourierSettlementAdjustmentKind);

interface CourierSettlementModalProps {
  courier: DeliveryCourier;
  /** What the courier owes, the oldest first. */
  owed: CourierOrder[];
  /** More orders are owed than one settlement takes: the oldest come first. */
  truncated: boolean;
  onClose: () => void;
  onSettled: (detail: CourierSettlementDetail) => void;
}

/**
 * The courier hands over the cash it collected (Fase 22D): the orders it pays, what it handed over and how, and the
 * adjustments that explain the rest (its trip, its fee for collecting, a shortage). It saves once it adds up to the cent; the
 * orders are charged in the drawer of whoever receives the cash and the adjustments enter it as movements.
 */
export default function CourierSettlementModal({ courier, owed, truncated, onClose, onSettled }: CourierSettlementModalProps) {
  const id = useId();
  const courierName = DELIVERY_COURIER_LABELS[courier];
  const create = useCreateCourierSettlement();
  const [selected, setSelected] = useState<Set<string>>(() => new Set(owed.map((order) => order.orderId)));
  const [method, setMethod] = useState<OrderPaymentMethod>(OrderPaymentMethod.Cash);
  const [handedText, setHandedText] = useState('');
  const [rows, setRows] = useState<AdjustmentRow[]>([]);
  const [reference, setReference] = useState('');
  const [notes, setNotes] = useState('');
  const [revealed, setRevealed] = useState(false);
  const [error, setError] = useState('');

  const orders = useMemo(() => owed.filter((order) => selected.has(order.orderId)), [owed, selected]);
  const summary = summarize(orders, rows, handedText);
  const balanced = summary.difference === 0;

  const toggle = (orderId: string) => setSelected((current) => {
    const next = new Set(current);
    if (next.has(orderId)) next.delete(orderId);
    else next.add(orderId);
    return next;
  });
  const change = (key: string, patch: Partial<AdjustmentRow>) =>
    setRows((current) => current.map((row) => (row.key === key ? { ...row, ...patch } : row)));

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    setRevealed(true);
    if (orders.length === 0) return setError(NO_ORDERS_MESSAGE);
    if (summary.handed === undefined) return setError(HANDED_AMOUNT_MESSAGE);
    if (!summary.adjustmentsValid) return setError('Revisa los ajustes marcados.');
    if (!balanced) return setError(doesNotBalanceMessage(summary.handed, summary.expected));
    if (create.isPending) return;
    setError('');
    try {
      const detail = await create.mutateAsync(toBody(courier, method, summary.handed, orders, rows, reference, notes));
      onSettled(detail);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar la liquidación.'));
    }
  };

  return (
    <Modal show onHide={onClose} centered size="lg" aria-labelledby={`${id}-title`}>
      <form onSubmit={(event) => void save(event)} noValidate>
        <Modal.Header closeButton>
          <Modal.Title id={`${id}-title`} as="h2" className="h5">Liquidar a {courierName}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <fieldset className="mb-3">
            <legend className="h6">Pedidos que cobró</legend>
            {truncated && (
              <p className="small text-muted">Se muestran los {owed.length} más antiguos: una liquidación lleva hasta {owed.length} pedidos.</p>
            )}
            <ul className="list-unstyled mb-0">
              {owed.map((order) => (
                <li key={order.orderId} className="form-check">
                  <input
                    id={`${id}-order-${order.orderId}`}
                    className="form-check-input"
                    type="checkbox"
                    checked={selected.has(order.orderId)}
                    disabled={create.isPending}
                    onChange={() => toggle(order.orderId)}
                  />
                  <label className="form-check-label d-flex flex-wrap gap-2" htmlFor={`${id}-order-${order.orderId}`}>
                    <span className="fw-semibold">{order.orderNumber}</span>
                    <span>{order.customerName}</span>
                    {order.courierFolio && <span className="text-muted">folio {order.courierFolio}</span>}
                    <span className="text-muted">salió {formatDateTime(order.dispatchedAt)}</span>
                    <span className="ms-auto ta-money">{formatMoney(order.balance)}</span>
                  </label>
                </li>
              ))}
            </ul>
          </fieldset>

          <div className="row g-2 mb-3">
            <div className="col-sm-6">
              <label className="form-label small mb-1" htmlFor={`${id}-handed`}>Entregó</label>
              <input
                id={`${id}-handed`}
                className={`form-control ta-money ${revealed && summary.handed === undefined ? 'is-invalid' : ''}`}
                inputMode="decimal"
                autoComplete="off"
                placeholder={formatMoney(summary.expected)}
                value={handedText}
                disabled={create.isPending}
                onChange={(event) => setHandedText(event.target.value)}
              />
            </div>
            <div className="col-sm-6">
              <span className="form-label small mb-1 d-block" id={`${id}-method`}>Cómo lo entregó</span>
              <div className="btn-group w-100" role="group" aria-labelledby={`${id}-method`}>
                {[
                  { value: OrderPaymentMethod.Cash, label: 'Efectivo' },
                  { value: OrderPaymentMethod.Transfer, label: 'Transferencia' },
                ].map((option) => (
                  <button
                    key={option.value}
                    type="button"
                    className={`btn btn-sm ${method === option.value ? 'btn-primary' : 'btn-outline-secondary'}`}
                    aria-pressed={method === option.value}
                    disabled={create.isPending}
                    onClick={() => setMethod(option.value)}
                  >
                    {option.label}
                  </button>
                ))}
              </div>
            </div>
          </div>

          <fieldset className="mb-3">
            <legend className="h6">Ajustes</legend>
            {rows.length === 0 ? (
              <p className="text-muted small">Sin ajustes. Agrega lo que el repartidor descontó: su viaje, su comisión por cobrar o un faltante.</p>
            ) : (
              <ol className="list-unstyled mb-2">
                {rows.map((row, index) => {
                  const number = index + 1;
                  const check = checkAdjustment(row);
                  const field = (name: string) => `${id}-${name}-${number}`;
                  const amountProblem = revealed || row.amountText.trim() !== '' ? check.amountProblem : '';
                  const kindProblem = revealed ? check.kindProblem : '';
                  const descriptionProblem = revealed || row.description.trim() !== '' ? check.descriptionProblem : '';
                  return (
                    <li key={row.key} className="row g-2 align-items-start mb-2">
                      <div className="col-12 col-md-4">
                        <label className="form-label small mb-1" htmlFor={field('kind')}>Motivo del ajuste {number}</label>
                        <select
                          id={field('kind')}
                          className={`form-select form-select-sm ${kindProblem ? 'is-invalid' : ''}`}
                          value={row.kind}
                          disabled={create.isPending}
                          onChange={(event) => change(row.key, {
                            kind: event.target.value === '' ? '' : (Number(event.target.value) as CourierSettlementAdjustmentKind),
                          })}
                        >
                          <option value="">Elige el motivo</option>
                          {KINDS.map((kind) => <option key={kind} value={kind}>{ADJUSTMENT_KIND_LABELS[kind]}</option>)}
                        </select>
                        {kindProblem && <div className="invalid-feedback d-block">{kindProblem}</div>}
                      </div>
                      <div className="col-12 col-md-4">
                        <label className="form-label small mb-1" htmlFor={field('description')}>
                          Descripción<span className="visually-hidden"> del ajuste {number}</span>
                          {row.kind === CourierSettlementAdjustmentKind.Other ? '' : ' (opcional)'}
                        </label>
                        <input
                          id={field('description')}
                          className={`form-control form-control-sm ${descriptionProblem ? 'is-invalid' : ''}`}
                          maxLength={MAX_DESCRIPTION_LENGTH}
                          autoComplete="off"
                          value={row.description}
                          disabled={create.isPending}
                          onChange={(event) => change(row.key, { description: event.target.value })}
                        />
                        {descriptionProblem && <div className="invalid-feedback d-block">{descriptionProblem}</div>}
                      </div>
                      <div className="col-8 col-md-2">
                        <label className="form-label small mb-1" htmlFor={field('amount')}>
                          Importe<span className="visually-hidden"> del ajuste {number}</span>
                        </label>
                        <input
                          id={field('amount')}
                          className={`form-control form-control-sm text-end ${amountProblem ? 'is-invalid' : ''}`}
                          inputMode="decimal"
                          autoComplete="off"
                          placeholder="0.00"
                          value={row.amountText}
                          disabled={create.isPending}
                          onChange={(event) => change(row.key, { amountText: event.target.value })}
                        />
                        {amountProblem && <div className="invalid-feedback d-block">{amountProblem}</div>}
                      </div>
                      <div className="col-4 col-md-2">
                        <span className="form-label small mb-1 d-inline-block invisible" aria-hidden="true">Quitar</span>
                        <button
                          type="button"
                          className="btn btn-light btn-sm w-100"
                          aria-label={`Quitar el ajuste ${number}`}
                          disabled={create.isPending}
                          onClick={() => setRows((current) => current.filter((other) => other.key !== row.key))}
                        >
                          Quitar
                        </button>
                      </div>
                      {row.kind === CourierSettlementAdjustmentKind.Other && (
                        <div className="col-12">
                          <div className="form-check form-check-inline">
                            <input
                              id={field('takes-off')}
                              className="form-check-input"
                              type="radio"
                              name={field('way')}
                              checked={!row.adds}
                              onChange={() => change(row.key, { adds: false })}
                            />
                            <label className="form-check-label small" htmlFor={field('takes-off')}>Se descuenta</label>
                          </div>
                          <div className="form-check form-check-inline">
                            <input
                              id={field('adds')}
                              className="form-check-input"
                              type="radio"
                              name={field('way')}
                              checked={row.adds}
                              onChange={() => change(row.key, { adds: true })}
                            />
                            <label className="form-check-label small" htmlFor={field('adds')}>Se suma</label>
                          </div>
                        </div>
                      )}
                      {row.kind !== '' && isDeduction(row.kind) && (
                        <p className="col-12 small text-muted mb-0">Se descuenta de lo que entrega.</p>
                      )}
                    </li>
                  );
                })}
              </ol>
            )}
            <button
              type="button"
              className="btn btn-light btn-sm"
              disabled={create.isPending || rows.length >= MAX_ADJUSTMENTS}
              onClick={() => setRows((current) => [...current, newAdjustment()])}
            >
              <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar ajuste
            </button>
          </fieldset>

          <dl className="row small mb-2" aria-label="Cuadre">
            <dt className="col-7">Pedidos ({orders.length})</dt>
            <dd className="col-5 text-end ta-money mb-1">{formatMoney(summary.orders)}</dd>
            <dt className="col-7">Ajustes</dt>
            <dd className="col-5 text-end ta-money mb-1">{formatSignedMoney(summary.adjustments)}</dd>
            <dt className="col-7">Debe entregar</dt>
            <dd className="col-5 text-end ta-money fw-semibold mb-1">{formatMoney(summary.expected)}</dd>
            <dt className="col-7">Diferencia</dt>
            <dd className={`col-5 text-end ta-money fw-semibold mb-1 ${summary.difference ? 'text-danger' : ''}`} role="status">
              {summary.difference === undefined ? '—' : formatSignedMoney(summary.difference)}
            </dd>
          </dl>
          {summary.difference !== undefined && summary.difference !== 0 && summary.adjustmentsValid && (
            <button
              type="button"
              className="btn btn-outline-secondary btn-sm mb-3"
              disabled={create.isPending || rows.length >= MAX_ADJUSTMENTS}
              onClick={() => setRows((current) => [...current, adjustmentForDifference(summary.difference!)])}
            >
              {summary.difference < 0
                ? `Registrar faltante de ${formatMoney(-summary.difference)}`
                : `Registrar sobrante de ${formatMoney(summary.difference)}`}
            </button>
          )}

          <div className="row g-2">
            <div className="col-sm-6">
              <label className="form-label small mb-1" htmlFor={`${id}-reference`}>Referencia (opcional)</label>
              <input
                id={`${id}-reference`}
                className="form-control"
                maxLength={MAX_REFERENCE_LENGTH}
                autoComplete="off"
                value={reference}
                disabled={create.isPending}
                onChange={(event) => setReference(event.target.value)}
              />
            </div>
            <div className="col-sm-6">
              <label className="form-label small mb-1" htmlFor={`${id}-notes`}>Notas (opcional)</label>
              <input
                id={`${id}-notes`}
                className="form-control"
                maxLength={MAX_NOTES_LENGTH}
                autoComplete="off"
                value={notes}
                disabled={create.isPending}
                onChange={(event) => setNotes(event.target.value)}
              />
            </div>
          </div>
          {error && <div className="alert alert-danger py-2 mt-3 mb-0" role="alert">{error}</div>}
          <p className="text-muted small mt-3 mb-0">
            Cada pedido queda cobrado en tu caja y los ajustes entran a ella como movimientos. La liquidación no se puede deshacer.
          </p>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose} disabled={create.isPending}>Volver</button>
          {/* Always pressable: trying to save says what is missing. */}
          <button type="submit" className="btn btn-primary" disabled={create.isPending}>
            {create.isPending ? 'Guardando…' : 'Guardar liquidación'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
