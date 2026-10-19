import { useId, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import type { OrderDto, OrderPaymentMethod, RefundOrderRequest, VoidOrderRequest } from '../../types';
import { OrderPaymentMethod as PaymentMethod } from '../../types';
import { getPlatformName, isDeliveryPlatform } from '../../utils/deliveryPlatforms';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney, parseMoneyInput, toCents } from '../../utils/money';
import { allowsFractions, formatQuantity, parseQuantity, roundQuantity } from '../../utils/units';

type RefundModalProps = {
  order: OrderDto;
  submitting: boolean;
  error: string;
  onClose: () => void;
} & (
  | { mode: 'refund'; onSubmit: (request: RefundOrderRequest) => void }
  | { mode: 'void'; onSubmit: (request: VoidOrderRequest) => void }
);

// What the business gives back with its own money. «Plataforma» and «En línea» are never among them: only what a platform
// charged, or the customer paid online, goes back that way, outside every drawer, and then it is the only way.
const METHODS: OrderPaymentMethod[] = [
  PaymentMethod.Cash,
  PaymentMethod.Card,
  PaymentMethod.Transfer,
  PaymentMethod.DigitalWallet,
  PaymentMethod.Other,
];

/** What each payment method still has to give back: paid with it minus refunded with it. */
function remainingByMethod(order: OrderDto): Array<{ method: OrderPaymentMethod; amount: number }> {
  const cents = new Map<OrderPaymentMethod, number>();
  for (const payment of order.payments) cents.set(payment.method, (cents.get(payment.method) ?? 0) + toCents(payment.amount));
  for (const refund of order.refunds ?? []) {
    if (cents.has(refund.method)) cents.set(refund.method, (cents.get(refund.method) ?? 0) - toCents(refund.amount));
  }
  return [...cents.entries()].filter(([, value]) => value > 0).map(([method, value]) => ({ method, amount: value / 100 }));
}

/**
 * Refund (money back, optionally for returned units) or void (everything back and the sale cancelled).
 * Refunds never edit the sale: they are recorded apart with their reason. What a delivery platform charged goes back
 * as «Plataforma», outside the drawer, to square it with the platform's settlement.
 */
export default function RefundOrderModal(props: RefundModalProps) {
  const titleId = useId();
  const { order, submitting, error, onClose } = props;
  const netPaid = order.netPaidAmount ?? order.paidAmount;
  const remaining = useMemo(() => remainingByMethod(order), [order]);
  const platformName = isDeliveryPlatform(order.channel) ? getPlatformName(order.channel) : null;
  // The platform charged it: its money goes back the way it came (the server refuses any other method).
  const platformCharged = order.payments.some((payment) => payment.method === PaymentMethod.Platform);
  const voidingPlatformOrder = props.mode === 'void' && platformName !== null;
  // Paid online (Fase 24I): it goes back «En línea», through Mercado Pago, and only that way (the server refuses any other
  // method): never out of a drawer it never entered.
  const paidOnline = order.payments.some((payment) => payment.method === PaymentMethod.Online);
  const fixedMethod = platformCharged ? PaymentMethod.Platform : paidOnline ? PaymentMethod.Online : null;
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  // Weighed lines take a typed quantity ("0.5"); the text is kept while it is being written.
  const [quantityTexts, setQuantityTexts] = useState<Record<string, string>>({});
  const [amountInput, setAmountInput] = useState('');
  const [method, setMethod] = useState<OrderPaymentMethod>(remaining[0]?.method ?? PaymentMethod.Cash);
  const [reason, setReason] = useState('');
  // An order of a platform is voided mostly because the platform cancelled it or it was registered twice: the food was
  // usually cooked, so its supplies stay out unless someone says otherwise.
  const [restock, setRestock] = useState(!voidingPlatformOrder);

  // Units cancelled from a tab were never charged: they are not returned.
  const returnableOf = (item: OrderDto['items'][number]) =>
    roundQuantity(item.quantity - (item.returnedQuantity ?? 0) - (item.cancelledQuantity ?? 0));
  const returnable = order.items.filter((item) => returnableOf(item) > 0);
  const linesTotalCents = order.items.reduce((sum, item) => sum + toCents(item.chargedPrice ?? item.totalPrice), 0);
  // The share of the order total of the units chosen (the total already has its IVA).
  const suggestedCents = Math.min(
    toCents(netPaid),
    linesTotalCents === 0
      ? 0
      : Math.round(order.items.reduce((sum, item) => {
        const units = quantities[item.id] ?? 0;
        return sum + (toCents(item.totalPrice) * units) / item.quantity;
      }, 0) * toCents(order.total) / linesTotalCents),
  );
  const typedAmount = amountInput.trim() === '' ? null : parseMoneyInput(amountInput);
  const amountCents = typedAmount === null ? suggestedCents : toCents(typedAmount);
  const anyUnits = Object.values(quantities).some((value) => value > 0);
  const amountValid = amountCents > 0 && amountCents <= toCents(netPaid) && (amountInput.trim() === '' || typedAmount !== null);
  const reasonValid = reason.trim().length >= 3;
  const canSubmit = !submitting && reasonValid && (props.mode === 'void' || amountValid);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (!canSubmit) return;
    if (props.mode === 'void') {
      props.onSubmit({ reason: reason.trim(), restockItems: restock });
      return;
    }
    props.onSubmit({
      amount: amountCents / 100,
      method: fixedMethod ?? method,
      reason: reason.trim(),
      restockItems: restock && anyUnits,
      items: Object.entries(quantities)
        .filter(([, quantity]) => quantity > 0)
        .map(([orderItemId, quantity]) => ({ orderItemId, quantity })),
    });
  };

  return (
    <Modal show onHide={onClose} centered size="lg" aria-labelledby={titleId}>
      <form onSubmit={submit}>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">
            {props.mode === 'void' ? `Anular la venta ${order.orderNumber}` : `Reembolsar ${order.orderNumber}`}
          </Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="text-muted">
            Pagado {formatMoney(order.paidAmount)}
            {(order.refundedAmount ?? 0) > 0 && <> · ya reembolsado {formatMoney(order.refundedAmount ?? 0)}</>}
            {' · '}disponible para reembolsar <strong>{formatMoney(netPaid)}</strong>
          </p>

          {voidingPlatformOrder && (
            <p>
              Anula el pedido si {platformName} lo canceló o si lo registraste dos veces.
              {platformCharged && <> Lo que cobró {platformName} no sale de tu caja.</>}
            </p>
          )}

          {props.mode === 'void' ? (
            <div className="alert alert-warning">
              Se devolverá todo lo pagado con la misma forma de pago:
              <ul className="mb-0 mt-1">
                {remaining.map((item) => (
                  <li key={item.method}>{getPaymentMethodLabel(item.method)}: {formatMoney(item.amount)}</li>
                ))}
              </ul>
              El pedido se cancela si su estado lo permite; si ya se entregó, queda entregado y reembolsado.
            </div>
          ) : (
            <>
              {returnable.length > 0 && (
                <div className="mb-3">
                  <div className="fw-semibold mb-1">Artículos devueltos (opcional)</div>
                  <ul className="list-unstyled mb-0">
                    {returnable.map((item) => {
                      const max = returnableOf(item);
                      const value = quantities[item.id] ?? 0;
                      if (allowsFractions(item.unit)) {
                        const text = quantityTexts[item.id] ?? '';
                        const parsed = text.trim() === '' ? 0 : parseQuantity(text, item.unit);
                        const invalid = parsed === null || parsed > max;
                        return (
                          <li key={item.id} className="d-flex align-items-center justify-content-between gap-2 border-bottom py-1">
                            <label htmlFor={`refund-qty-${item.id}`}>
                              {item.productName}{' '}
                              <span className="text-muted small">({formatQuantity(max, item.unit)} disponibles)</span>
                            </label>
                            <input
                              id={`refund-qty-${item.id}`}
                              className={`form-control form-control-sm ta-money text-end ${invalid ? 'is-invalid' : ''}`}
                              style={{ maxWidth: '7rem' }}
                              inputMode="decimal"
                              placeholder="0"
                              value={text}
                              onChange={(event) => {
                                const next = event.target.value;
                                setQuantityTexts((current) => ({ ...current, [item.id]: next }));
                                const quantity = next.trim() === '' ? 0 : parseQuantity(next, item.unit);
                                setQuantities((current) => ({
                                  ...current,
                                  [item.id]: quantity !== null && quantity <= max ? quantity : 0,
                                }));
                              }}
                            />
                          </li>
                        );
                      }
                      return (
                        <li key={item.id} className="d-flex align-items-center justify-content-between gap-2 border-bottom py-1">
                          <span>{item.productName} <span className="text-muted small">({max} disponibles)</span></span>
                          <span className="d-flex align-items-center gap-1" role="group" aria-label={`Devolver ${item.productName}`}>
                            <button type="button" className="btn btn-sm btn-light" disabled={value === 0}
                              onClick={() => setQuantities((current) => ({ ...current, [item.id]: Math.max(0, value - 1) }))}
                              aria-label={`Uno menos de ${item.productName}`}>−</button>
                            <span className="px-2" aria-live="polite">{value}</span>
                            <button type="button" className="btn btn-sm btn-light" disabled={value >= max}
                              onClick={() => setQuantities((current) => ({ ...current, [item.id]: Math.min(max, value + 1) }))}
                              aria-label={`Uno más de ${item.productName}`}>+</button>
                          </span>
                        </li>
                      );
                    })}
                  </ul>
                </div>
              )}
              <div className="row g-2 mb-3">
                <div className="col-sm-6">
                  <label className="form-label" htmlFor="refund-amount">Monto a devolver</label>
                  <input
                    id="refund-amount"
                    className={`form-control form-control-lg ta-money ${amountValid || amountCents === 0 ? '' : 'is-invalid'}`}
                    inputMode="decimal"
                    placeholder={formatMoney(suggestedCents / 100)}
                    value={amountInput}
                    onChange={(event) => setAmountInput(event.target.value)}
                  />
                  {!amountValid && amountCents > 0 && (
                    <div className="invalid-feedback">No puede ser mayor a {formatMoney(netPaid)}.</div>
                  )}
                  <div className="form-text">Vacío usa lo que corresponde a los artículos elegidos.</div>
                </div>
                <div className="col-sm-6">
                  <label className="form-label" htmlFor="refund-method">Se devuelve en</label>
                  {fixedMethod !== null ? (
                    <>
                      <input id="refund-method" className="form-control form-control-lg" readOnly
                        value={getPaymentMethodLabel(fixedMethod)} aria-describedby="refund-method-help" />
                      <div id="refund-method-help" className="form-text">
                        {fixedMethod === PaymentMethod.Platform
                          ? <>La devolución queda registrada para cuadrarla con la liquidación de {platformName ?? 'la plataforma'}; no sale de la caja.</>
                          : 'Mercado Pago se lo devuelve al cliente a su tarjeta o cuenta; no sale de tu caja.'}
                      </div>
                    </>
                  ) : (
                    <>
                      <select id="refund-method" className="form-select form-select-lg" value={method}
                        onChange={(event) => setMethod(Number(event.target.value) as OrderPaymentMethod)}>
                        {METHODS.map((item) => (
                          <option key={item} value={item}>{getPaymentMethodLabel(item)}</option>
                        ))}
                      </select>
                      {method === PaymentMethod.Cash && <div className="form-text">Sale del efectivo de tu caja.</div>}
                    </>
                  )}
                </div>
              </div>
            </>
          )}

          <div className="mb-3">
            <label className="form-label" htmlFor="refund-reason">Motivo</label>
            <input
              id="refund-reason"
              className="form-control"
              maxLength={500}
              placeholder={props.mode === 'void' ? 'Ej. cobro duplicado' : 'Ej. producto en mal estado'}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              autoFocus
            />
            {voidingPlatformOrder && (
              // The usual reasons, one tap away; the text stays editable.
              <div className="d-flex flex-wrap gap-2 mt-2">
                {[`${platformName} lo canceló`, 'Lo registré dos veces', 'No llegó el repartidor'].map((text) => (
                  <button key={text} type="button" className="btn btn-light btn-sm" onClick={() => setReason(text)}>
                    {text}
                  </button>
                ))}
              </div>
            )}
          </div>
          <div className="form-check form-switch">
            <input id="refund-restock" type="checkbox" role="switch" className="form-check-input" checked={restock}
              onChange={(event) => setRestock(event.target.checked)}
              aria-describedby={voidingPlatformOrder ? 'refund-restock-help' : undefined} />
            <label className="form-check-label" htmlFor="refund-restock">Regresar los insumos al inventario</label>
            {voidingPlatformOrder && (
              <div id="refund-restock-help" className="form-text">Márcalo solo si la comida no se preparó.</div>
            )}
          </div>
          {error && <div className="alert alert-danger mt-3 mb-0">{error}</div>}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose} disabled={submitting}>Volver</button>
          <button type="submit" className="btn btn-danger" disabled={!canSubmit}>
            {submitting
              ? 'Guardando…'
              : props.mode === 'void' ? 'Anular venta' : `Reembolsar ${formatMoney(amountCents / 100)}`}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
