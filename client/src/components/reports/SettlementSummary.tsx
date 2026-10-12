import { useId } from 'react';
import { formatMoney } from '../../utils/money';

interface SettlementSummaryProps {
  pickedCount: number;
  ordersCents: number;
  adjustmentsCents: number;
  expectedCents: number;
  /** Undefined while the box of the deposit holds something the API would refuse. */
  depositedCents?: number;
  differenceCents?: number;
  /** «Registrar la diferencia como ajuste», while the settlement is being written and the difference fits in one. */
  onExplainDifference?: () => void;
}

const money = (cents: number) => formatMoney(cents / 100);

/**
 * The accounts of a settlement: what its orders and adjustments say the platform should have deposited, what it did and the
 * difference, which must be $0.00 to close it.
 */
export default function SettlementSummary({
  pickedCount,
  ordersCents,
  adjustmentsCents,
  expectedCents,
  depositedCents,
  differenceCents,
  onExplainDifference,
}: SettlementSummaryProps) {
  const titleId = useId();
  const balanced = differenceCents === 0;

  return (
    <section className="card mb-3" aria-labelledby={titleId}>
      <div className="card-body">
        <h2 id={titleId} className="h5 mb-3">Cuentas</h2>
        <dl className="settlement-accounts mb-0">
          <dt>{pickedCount === 1 ? '1 pedido' : `${pickedCount} pedidos`}</dt>
          <dd className="ta-money">{money(ordersCents)}</dd>
          <dt>Ajustes</dt>
          <dd className="ta-money">{money(adjustmentsCents)}</dd>
          <dt>Debió depositar</dt>
          <dd className="ta-money">{money(expectedCents)}</dd>
          <dt>Depositó</dt>
          <dd className="ta-money">{depositedCents === undefined ? '—' : money(depositedCents)}</dd>
          <dt className="settlement-accounts-difference">Diferencia</dt>
          <dd className="settlement-accounts-difference ta-money">{differenceCents === undefined ? '—' : money(differenceCents)}</dd>
        </dl>
        {/* Not a live region: it changes with every key of the deposit, and the bar below says what is missing. */}
        {differenceCents !== undefined && (
          <div className="d-flex flex-wrap align-items-center gap-2 mt-3">
            {balanced ? (
              <span className="ta-chip ta-chip-ready">Cuadra</span>
            ) : (
              <>
                <span className="small me-auto">
                  {differenceCents < 0
                    ? `Llegó ${money(-differenceCents)} menos de lo esperado.`
                    : `Llegó ${money(differenceCents)} más de lo esperado.`}
                  {onExplainDifference && ' Explícalo con un ajuste para cerrar.'}
                </span>
                {onExplainDifference && (
                  <button type="button" className="btn btn-light btn-sm" onClick={onExplainDifference}>
                    Registrar la diferencia como ajuste
                  </button>
                )}
              </>
            )}
          </div>
        )}
      </div>
    </section>
  );
}
