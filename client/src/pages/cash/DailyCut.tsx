import { useCallback, useEffect, useState } from 'react';
import type { Branch } from '../../api/branchesApi';
import { cashApi } from '../../api/cashApi';
import type { DailyCashCutDto } from '../../types';
import PrintButton from '../../components/common/PrintButton';
import WhatsAppLink from '../../components/common/WhatsAppLink';
import { useTenant } from '../../hooks/useTenant';
import { formatMoney } from '../../utils/money';
import { buildDailyCutText, getDailyCutRows, getOtherMethodsTotal } from './cashCutText';
import { personLabel } from './cashFormat';
import { formatCalendarDate, formatInBusinessZone, getBusinessToday } from '../../utils/dates';

interface DailyCutProps {
  branches: Branch[];
}

interface Figure {
  label: string;
  value: number;
  detail?: string;
  /** A second line under the detail. */
  note?: string;
}

/** Payments of a day by method, user and branch, for every drawer of the business. */
export default function DailyCut({ branches }: DailyCutProps) {
  const { tenant } = useTenant();
  const [date, setDate] = useState(getBusinessToday());
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

  const platformTotal = cut?.platformTotal ?? 0;
  const platformRefundsTotal = cut?.platformRefundsTotal ?? 0;
  const figures: Figure[] = !cut ? [] : [
    { label: 'Cobrado', value: cut.salesPaymentsTotal, detail: `${cut.salesPaymentsCount} pagos` },
    { label: 'Efectivo', value: cut.cashTotal },
    { label: 'Tarjeta', value: cut.cardTotal },
    { label: 'Transferencia', value: cut.transferTotal },
    { label: 'Otros', value: getOtherMethodsTotal(cut) },
    // What the delivery platforms charged in their apps: part of "Cobrado", in no drawer, settled by each platform.
    ...(platformTotal > 0 || platformRefundsTotal > 0
      ? [{
        label: 'Plataformas',
        value: platformTotal,
        detail: 'Lo cobró la plataforma: no entra a la caja',
        note: platformRefundsTotal > 0 ? `Devuelto: ${formatMoney(platformRefundsTotal)}` : undefined,
      }]
      : []),
    { label: 'Neto en efectivo', value: cut.netCashOnlyFlow, detail: cut.cashTipsTotal > 0 ? 'Efectivo + propinas + depósitos − gastos' : 'Efectivo + depósitos − gastos' },
    // Tips are for the team: apart from what was sold.
    ...(cut.tipsTotal > 0 ? [{ label: 'Propinas', value: cut.tipsTotal, detail: `${formatMoney(cut.cashTipsTotal)} en efectivo` }] : []),
  ];
  const dateLabel = date ? formatCalendarDate(date, { dateStyle: 'long' }) : 'Hoy';
  const scopeLabel = (branchId ? branches.find((branch) => branch.id === branchId)?.name ?? 'Sucursal' : 'Todas las sucursales')
    + (userName.trim() ? ` · ${userName.trim()}` : '');

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
            <input id="cut-user" className="form-control" placeholder="Nombre o correo" value={userName}
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
              {figures.map((kpi) => (
                <div key={kpi.label} className="col-6 col-lg-2">
                  <div className="text-muted small">{kpi.label}</div>
                  <div className="fw-bold ta-money">{formatMoney(kpi.value)}</div>
                  {kpi.detail && <div className="small text-muted">{kpi.detail}</div>}
                  {kpi.note && <div className="small text-muted">{kpi.note}</div>}
                </div>
              ))}
            </div>
            <div className="d-flex flex-wrap justify-content-end gap-2 mb-2">
              <WhatsAppLink
                text={buildDailyCutText(cut, { businessName: tenant?.name, dateLabel, scopeLabel })}
                className="btn btn-sm btn-light"
              />
              <PrintButton
                label="Imprimir corte del día"
                className="btn btn-sm btn-light"
                render={() => (
                  <div>
                    {tenant?.name && <div className="text-center fw-bold">{tenant.name}</div>}
                    <div className="text-center fw-bold">Corte del día</div>
                    <div className="text-center small mb-2 ta-ticket-wrap">{dateLabel} · {scopeLabel}</div>
                    {getDailyCutRows(cut).map(([label, value]) => (
                      <div key={label} className="ta-ticket-row">
                        <span>{label}</span>
                        <span className="text-nowrap">{formatMoney(value)}</span>
                      </div>
                    ))}
                    <div className="small">{cut.salesPaymentsCount} pagos</div>
                    {cut.byUser.length > 0 && <hr className="my-1" />}
                    {/* The person on their own line, so a long name or email never pushes the amount past the edge of the ticket. */}
                    {cut.byUser.map((item) => (
                      <div key={`${item.userName}-${item.paymentsCount}`}>
                        <div className="ta-ticket-wrap">{personLabel(item.displayName, item.userName)}</div>
                        <div className="ta-ticket-row">
                          <span>{item.paymentsCount} {item.paymentsCount === 1 ? 'pago' : 'pagos'}</span>
                          <span className="text-nowrap">{formatMoney(item.totalAmount)}</span>
                        </div>
                      </div>
                    ))}
                    <div className="text-center small mt-2">
                      Impreso el {formatInBusinessZone(new Date(), { dateStyle: 'short', timeStyle: 'short' })}
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
                    <li key={`${item.userName}-${item.paymentsCount}`}>{personLabel(item.displayName, item.userName)}: {item.paymentsCount} pagos / {formatMoney(item.totalAmount)}</li>
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
