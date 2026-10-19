import { useId, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { reportsApi, type ChannelSales } from '../../api/reportsApi';
import { useActiveBranches } from '../../hooks/useWorkingBranch';
import { getBusinessToday } from '../../utils/dates';
import { getPlatformName, isDeliveryPlatform, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { getChannelLabel } from '../../utils/labels';
import { formatMoney, toCents } from '../../utils/money';
import BranchPicker from '../branches/BranchPicker';
import DeliveryPlatformOrdersModal from './DeliveryPlatformOrdersModal';
import { previousWeek } from './reportPeriods';

/** From the 1st of the month to that day: the period the report opens with, as the server's own default. */
function thisMonth(today: string) {
  return { from: `${today.slice(0, 8)}01`, to: today };
}

/** Two figures of the server shown in one column (the commission and its IVA), added in cents. */
function together(first: number, second: number): number {
  return (toCents(first) + toCents(second)) / 100;
}

function PlatformCells({ row, shown }: { row: ChannelSales; shown: boolean }) {
  // The commission, the withholdings and the deposit belong to the delivery platforms only.
  if (!shown) {
    return (
      <>
        <td className="text-end text-muted">—</td>
        <td className="text-end text-muted">—</td>
        <td className="text-end text-muted">—</td>
      </>
    );
  }
  return (
    <>
      <td className="text-end ta-money">{formatMoney(together(row.commission, row.commissionTax))}</td>
      <td className="text-end ta-money">{formatMoney(together(row.isrWithheld, row.vatWithheld))}</td>
      <td className="text-end ta-money">{formatMoney(row.expectedSettlement)}</td>
    </>
  );
}

/**
 * Ventas por canal: what was charged minus what was given back, by where the orders came from, on the same dates as
 * Inicio and the sales by branch. For Rappi, Uber Eats and DiDi Food the server adds its commission and its IVA, what it
 * withheld and what it should deposit, to compare with each platform's statement and the list of its orders.
 */
export default function SalesByChannelCard() {
  const titleId = useId();
  const today = getBusinessToday();
  const [from, setFrom] = useState(() => thisMonth(today).from);
  const [to, setTo] = useState(today);
  const [branchId, setBranchId] = useState('');
  const [ordersOf, setOrdersOf] = useState<DeliveryPlatform | null>(null);
  const branches = useActiveBranches();
  const report = useQuery({
    queryKey: ['reports', 'sales-by-channel', from, to, branchId],
    queryFn: ({ signal }) => reportsApi.getSalesByChannel(
      { from: from || undefined, to: to || undefined, branchId: branchId || undefined },
      signal,
    ),
  });

  const data = report.data;
  const showsPlatforms = data?.channels.some((row) => row.isDeliveryPlatform) ?? false;
  const choose = (period: { from: string; to: string }) => {
    setFrom(period.from);
    setTo(period.to);
  };

  return (
    <section className="card mb-3" aria-labelledby={titleId}>
      <div className="card-body">
        <div className="d-flex flex-wrap align-items-end justify-content-between gap-2 mb-3">
          <div>
            <h2 id={titleId} className="h5 mb-0">Ventas por canal</h2>
            <p className="text-muted small mb-0">
              Lo cobrado menos lo devuelto por origen del pedido, con la misma fecha de cada cobro que Inicio y las ventas por sucursal.
            </p>
          </div>
          <div className="d-flex flex-wrap align-items-end gap-2">
            <div>
              <label className="form-label small mb-1" htmlFor="sales-channel-from">Desde</label>
              <input id="sales-channel-from" type="date" className="form-control form-control-sm" value={from} max={to || undefined}
                onChange={(event) => setFrom(event.target.value)} />
            </div>
            <div>
              <label className="form-label small mb-1" htmlFor="sales-channel-to">Hasta</label>
              <input id="sales-channel-to" type="date" className="form-control form-control-sm" value={to} min={from || undefined}
                onChange={(event) => setTo(event.target.value)} />
            </div>
            <BranchPicker id="sales-channel-branch" branches={branches.data ?? []} value={branchId} onChange={setBranchId}
              allLabel="Todas las sucursales" />
            <button type="button" className="btn btn-light btn-sm" onClick={() => choose(previousWeek(today))}>Semana pasada</button>
            <button type="button" className="btn btn-light btn-sm" onClick={() => choose(thisMonth(today))}>Este mes</button>
          </div>
        </div>

        {report.isLoading && <p className="text-muted small mb-0" aria-busy="true">Cargando…</p>}
        {report.isError && (
          <div className="alert alert-danger mb-0">{getApiErrorMessage(report.error, 'No se pudo cargar el reporte de ventas por canal.')}</div>
        )}

        {data && (data.channels.length === 0 ? (
          <p className="text-muted mb-0">Sin ventas en el periodo.</p>
        ) : (
          <>
            <div className="table-responsive">
              <table className="table table-sm align-middle mb-0">
                <thead>
                  <tr>
                    <th scope="col">Canal</th>
                    <th scope="col" className="text-end">Pedidos</th>
                    <th scope="col" className="text-end">Cobrado</th>
                    <th scope="col" className="text-end">Devoluciones</th>
                    <th scope="col" className="text-end">Venta neta</th>
                    <th scope="col" className="text-end">Comisión e IVA</th>
                    <th scope="col" className="text-end">Retenciones</th>
                    <th scope="col" className="text-end">Neto esperado</th>
                  </tr>
                </thead>
                <tbody>
                  {data.channels.map((row) => {
                    const platform = isDeliveryPlatform(row.channel) ? row.channel : null;
                    return (
                      <tr key={row.channel ?? 'none'}>
                        <td>
                          <span className="d-inline-flex flex-wrap align-items-center gap-2">
                            <span className="fw-semibold">{getChannelLabel(row.channel)}</span>
                            {platform !== null && (
                              <button
                                type="button"
                                className="btn btn-light btn-sm"
                                aria-label={`Ver pedidos de ${getPlatformName(platform)}`}
                                onClick={() => setOrdersOf(platform)}
                              >
                                Ver pedidos
                              </button>
                            )}
                          </span>
                        </td>
                        <td className="text-end">{row.paidOrders}</td>
                        <td className="text-end ta-money">{formatMoney(row.paymentsAmount)}</td>
                        <td className="text-end ta-money">{formatMoney(row.refundsAmount)}</td>
                        <td className="text-end ta-money">{formatMoney(row.netSales)}</td>
                        <PlatformCells row={row} shown={platform !== null} />
                      </tr>
                    );
                  })}
                </tbody>
                <tfoot>
                  <tr className="fw-semibold">
                    <td>Total</td>
                    <td className="text-end">{data.totals.paidOrders}</td>
                    <td className="text-end ta-money">{formatMoney(data.totals.paymentsAmount)}</td>
                    <td className="text-end ta-money">{formatMoney(data.totals.refundsAmount)}</td>
                    <td className="text-end ta-money">{formatMoney(data.totals.netSales)}</td>
                    <PlatformCells row={data.totals} shown={showsPlatforms} />
                  </tr>
                </tfoot>
              </table>
            </div>
            {/* Paid online in Mercado Pago (Fase 24I): in the business's Mercado Pago account, not in its drawers. */}
            {data.totals.onlineCollected > 0 && (
              <p className="small mt-2 mb-0">
                De tu menú en línea se pagaron en línea {formatMoney(data.totals.onlineCollected)}: están en tu cuenta de Mercado Pago, no en tu caja.
              </p>
            )}
            {data.channels
              .filter((row) => isDeliveryPlatform(row.channel) && row.businessCollected > 0)
              .map((row) => (
                <p key={row.channel} className="small mt-2 mb-0">
                  De {getChannelLabel(row.channel)} cobraste tú {formatMoney(row.businessCollected)}: su comisión e IVA se descuentan de tu depósito.
                </p>
              ))}
            {showsPlatforms && (
              <div className="small text-muted mt-2">
                <p className="mb-1">
                  El neto esperado es lo que la plataforma debería depositarte: lo que cobró, menos la comisión, su IVA y las
                  retenciones. Si es negativo, le debes por pedidos que cobraste tú.
                </p>
                <p className="mb-0">
                  Por lo general las retenciones son pagos a cuenta de tus impuestos, no un costo. Compáralo con el estado de cuenta semanal de
                  cada plataforma (la de Uber Eats va del lunes a las 4:00 al lunes siguiente a las 3:59); puede variar por
                  centavos, promociones o cancelaciones que la plataforma ajuste en su liquidación.
                </p>
              </div>
            )}
          </>
        ))}
      </div>

      {ordersOf !== null && data && (
        <DeliveryPlatformOrdersModal
          platform={ordersOf}
          from={data.from}
          to={data.to}
          branchId={branchId}
          onClose={() => setOrdersOf(null)}
        />
      )}
    </section>
  );
}
