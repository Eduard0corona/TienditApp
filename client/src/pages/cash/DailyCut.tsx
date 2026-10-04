import { useCallback, useEffect, useState } from 'react';
import type { Branch } from '../../api/branchesApi';
import { cashApi } from '../../api/cashApi';
import type { DailyCashCutDto } from '../../types';
import PrintButton from '../../components/common/PrintButton';
import { useTenant } from '../../hooks/useTenant';
import { formatMoney } from '../../utils/money';
import { getTodayInputDate } from './cashFormat';

interface DailyCutProps {
  branches: Branch[];
}

/** Payments of a day by method, user and branch, for every drawer of the business. */
export default function DailyCut({ branches }: DailyCutProps) {
  const { tenant } = useTenant();
  const [date, setDate] = useState(getTodayInputDate());
  const [branchId, setBranchId] = useState('');
  const [userName, setUserName] = useState('');
  const [cut, setCut] = useState<DailyCashCutDto | null>(null);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const { data } = await cashApi.getDailyCut({
        date: date || undefined,
        branchId: branchId || undefined,
        userName: userName.trim() || undefined,
      });
      setCut(data);
    } catch {
      setCut(null);
    } finally {
      setLoading(false);
    }
  }, [branchId, date, userName]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="card">
      <div className="card-body">
        <div className="row g-2 mb-3">
          <div className="col-md-3">
            <label className="form-label" htmlFor="cut-date">Fecha</label>
            <input id="cut-date" type="date" className="form-control" value={date} onChange={(event) => setDate(event.target.value)} />
          </div>
          <div className="col-md-4">
            <label className="form-label" htmlFor="cut-branch">Sucursal</label>
            <select id="cut-branch" className="form-select" value={branchId} onChange={(event) => setBranchId(event.target.value)}>
              <option value="">Todas</option>
              {branches.map((branch) => (
                <option key={branch.id} value={branch.id}>{branch.name}</option>
              ))}
            </select>
          </div>
          <div className="col-md-5">
            <label className="form-label" htmlFor="cut-user">Usuario</label>
            <input id="cut-user" className="form-control" placeholder="Filtrar por usuario" value={userName}
              onChange={(event) => setUserName(event.target.value)} />
          </div>
        </div>

        {loading ? (
          <div className="text-muted">Cargando corte…</div>
        ) : !cut ? (
          <div className="text-muted">No hay información del corte con estos filtros.</div>
        ) : (
          <>
            <div className="row g-3 mb-3">
              {[
                { label: 'Cobrado', value: cut.salesPaymentsTotal, detail: `${cut.salesPaymentsCount} pagos` },
                { label: 'Efectivo', value: cut.cashTotal },
                { label: 'Tarjeta', value: cut.cardTotal },
                { label: 'Transferencia', value: cut.transferTotal },
                { label: 'Otros', value: cut.walletTotal + cut.otherTotal },
                { label: 'Neto en efectivo', value: cut.netCashOnlyFlow, detail: 'Efectivo + depósitos − gastos' },
              ].map((kpi) => (
                <div key={kpi.label} className="col-6 col-lg-2">
                  <div className="text-muted small">{kpi.label}</div>
                  <div className="fw-bold ta-money">{formatMoney(kpi.value)}</div>
                  {kpi.detail && <div className="small text-muted">{kpi.detail}</div>}
                </div>
              ))}
            </div>
            <div className="d-flex justify-content-end mb-2">
              <PrintButton
                label="Imprimir corte del día"
                className="btn btn-sm btn-light"
                render={() => (
                  <div>
                    {tenant?.name && <div className="text-center fw-bold">{tenant.name}</div>}
                    <div className="text-center fw-bold">Corte del día</div>
                    <div className="text-center small mb-2">
                      {date ? new Date(`${date}T12:00:00`).toLocaleDateString('es-MX', { dateStyle: 'long' }) : 'Hoy'}
                      {branchId ? ` · ${branches.find((branch) => branch.id === branchId)?.name ?? ''}` : ' · Todas las sucursales'}
                      {userName.trim() ? ` · ${userName.trim()}` : ''}
                    </div>
                    {[
                      ['Cobrado', cut.salesPaymentsTotal],
                      ['Efectivo', cut.cashTotal],
                      ['Tarjeta', cut.cardTotal],
                      ['Transferencia', cut.transferTotal],
                      ['Otros', cut.walletTotal + cut.otherTotal],
                      ['Neto en efectivo', cut.netCashOnlyFlow],
                    ].map(([label, value]) => (
                      <div key={label as string} className="d-flex justify-content-between gap-2">
                        <span>{label}</span>
                        <span>{formatMoney(value as number)}</span>
                      </div>
                    ))}
                    <div className="small">{cut.salesPaymentsCount} pagos</div>
                    {cut.byUser.length > 0 && <hr className="my-1" />}
                    {cut.byUser.map((item) => (
                      <div key={`${item.userName}-${item.paymentsCount}`} className="d-flex justify-content-between gap-2">
                        <span>{item.userName} ({item.paymentsCount})</span>
                        <span>{formatMoney(item.totalAmount)}</span>
                      </div>
                    ))}
                    <div className="text-center small mt-2">
                      Impreso el {new Date().toLocaleString('es-MX', { dateStyle: 'short', timeStyle: 'short' })}
                    </div>
                  </div>
                )}
              />
            </div>
            <div className="row">
              <div className="col-md-6">
                <h3 className="h6 mb-1">Por usuario</h3>
                <ul className="mb-0 ps-3">
                  {cut.byUser.length === 0 ? <li className="text-muted">Sin datos</li> : cut.byUser.map((item) => (
                    <li key={`${item.userName}-${item.paymentsCount}`}>{item.userName}: {item.paymentsCount} pagos / {formatMoney(item.totalAmount)}</li>
                  ))}
                </ul>
              </div>
              <div className="col-md-6">
                <h3 className="h6 mb-1">Por sucursal</h3>
                <ul className="mb-0 ps-3">
                  {cut.byBranch.length === 0 ? <li className="text-muted">Sin datos</li> : cut.byBranch.map((item) => (
                    <li key={item.branchId || 'none'}>{item.branchName}: {item.paymentsCount} pagos / {formatMoney(item.totalAmount)}</li>
                  ))}
                </ul>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
