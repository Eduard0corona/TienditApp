import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { reportsApi } from '../../api/reportsApi';
import { useActiveBranches } from '../../hooks/useWorkingBranch';
import { formatMoney } from '../../utils/money';
import BranchPicker from '../branches/BranchPicker';
import './SalesByBranchCard.css';

function toInputDate(date: Date) {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

function shortDay(isoDate: string) {
  const [year, month, day] = isoDate.split('-').map(Number);
  return new Date(year, month - 1, day).toLocaleDateString('es-MX', { day: 'numeric', month: 'short' });
}

/**
 * Sales by branch and by day: what each branch charged by method, what it gave back and its average ticket.
 * Closed days come from the daily summary; today is computed at the moment.
 */
export default function SalesByBranchCard() {
  const today = new Date();
  const [from, setFrom] = useState(toInputDate(new Date(today.getFullYear(), today.getMonth(), 1)));
  const [to, setTo] = useState(toInputDate(today));
  const [branchId, setBranchId] = useState('');
  const branches = useActiveBranches();
  const report = useQuery({
    queryKey: ['reports', 'sales-by-branch', from, to, branchId],
    queryFn: () => reportsApi.getSalesByBranch({ from: from || undefined, to: to || undefined, branchId: branchId || undefined }),
  });

  const data = report.data;
  const maxDay = Math.max(0, ...(data?.days.map((day) => day.netSales) ?? []));

  return (
    <div className="card mb-3">
      <div className="card-body">
        <div className="d-flex flex-wrap align-items-end justify-content-between gap-2 mb-3">
          <div>
            <h2 className="h5 mb-0">Ventas por sucursal</h2>
            <p className="text-muted small mb-0">Lo cobrado menos lo devuelto, por día y por sucursal.</p>
          </div>
          <div className="d-flex flex-wrap align-items-end gap-2">
            <div>
              <label className="form-label small mb-1" htmlFor="sales-branch-from">Desde</label>
              <input id="sales-branch-from" type="date" className="form-control form-control-sm" value={from} max={to || undefined}
                onChange={(event) => setFrom(event.target.value)} />
            </div>
            <div>
              <label className="form-label small mb-1" htmlFor="sales-branch-to">Hasta</label>
              <input id="sales-branch-to" type="date" className="form-control form-control-sm" value={to} min={from || undefined}
                onChange={(event) => setTo(event.target.value)} />
            </div>
            <BranchPicker branches={branches.data ?? []} value={branchId} onChange={setBranchId} allLabel="Todas las sucursales" />
          </div>
        </div>

        {report.isLoading && <div className="text-center py-3"><div className="spinner-border text-primary" role="status"></div></div>}
        {report.isError && <div className="alert alert-danger mb-0">{getApiErrorMessage(report.error, 'No se pudo cargar el reporte por sucursal.')}</div>}

        {data && (
          <>
            <div className="row g-2 mb-3">
              <div className="col-6 col-lg-3">
                <div className="ta-kpi">
                  <div className="text-muted small">Ventas netas</div>
                  <div className="h4 mb-0 ta-money">{formatMoney(data.totals.netSales)}</div>
                </div>
              </div>
              <div className="col-6 col-lg-3">
                <div className="ta-kpi">
                  <div className="text-muted small">Tickets cobrados</div>
                  <div className="h4 mb-0">{data.totals.paidOrders}</div>
                </div>
              </div>
              <div className="col-6 col-lg-3">
                <div className="ta-kpi">
                  <div className="text-muted small">Ticket promedio</div>
                  <div className="h4 mb-0 ta-money">{formatMoney(data.totals.averageTicket)}</div>
                </div>
              </div>
              <div className="col-6 col-lg-3">
                <div className="ta-kpi">
                  <div className="text-muted small">Devoluciones</div>
                  <div className="h4 mb-0 ta-money">{formatMoney(data.totals.refundsAmount)}</div>
                </div>
              </div>
            </div>

            <div className="ta-day-bars mb-3" role="img"
              aria-label={`Ventas netas por día del ${shortDay(data.from)} al ${shortDay(data.to)}`}>
              {data.days.map((day) => (
                <div key={day.date} className="ta-day-bar" title={`${shortDay(day.date)}: ${formatMoney(day.netSales)} · ${day.paidOrders} tickets`}>
                  <span style={{ height: `${maxDay > 0 ? Math.max(2, (Math.max(0, day.netSales) / maxDay) * 100) : 2}%` }}></span>
                </div>
              ))}
            </div>
            <div className="d-flex justify-content-between text-muted small mb-3">
              <span>{shortDay(data.from)}</span>
              <span>{shortDay(data.to)}</span>
            </div>

            {data.branches.length === 0 ? (
              <p className="text-muted mb-0">Sin ventas en el periodo.</p>
            ) : (
              <div className="table-responsive">
                <table className="table table-sm align-middle mb-0">
                  <thead>
                    <tr>
                      <th>Sucursal</th>
                      <th className="text-end">Ventas netas</th>
                      <th className="text-end">Tickets</th>
                      <th className="text-end">Ticket promedio</th>
                      <th className="text-end">Efectivo</th>
                      <th className="text-end">Tarjeta</th>
                      <th className="text-end">Transferencia</th>
                      <th className="text-end">Otros</th>
                      <th className="text-end">Devoluciones</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.branches.map((row) => (
                      <tr key={row.branchId ?? 'none'}>
                        <td className="fw-semibold">{row.branchName}</td>
                        <td className="text-end ta-money">{formatMoney(row.figures.netSales)}</td>
                        <td className="text-end">{row.figures.paidOrders}</td>
                        <td className="text-end ta-money">{formatMoney(row.figures.averageTicket)}</td>
                        <td className="text-end ta-money">{formatMoney(row.figures.cashAmount)}</td>
                        <td className="text-end ta-money">{formatMoney(row.figures.cardAmount)}</td>
                        <td className="text-end ta-money">{formatMoney(row.figures.transferAmount)}</td>
                        <td className="text-end ta-money">{formatMoney(row.figures.walletAmount + row.figures.otherAmount)}</td>
                        <td className="text-end ta-money">{formatMoney(row.figures.refundsAmount)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            <p className="text-muted small mt-2 mb-0">
              Los días cerrados se leen del resumen diario ({data.snapshotDays}); {data.liveDays === 1 ? 'un día se calculó' : `${data.liveDays} días se calcularon`} al momento.
            </p>
          </>
        )}
      </div>
    </div>
  );
}
