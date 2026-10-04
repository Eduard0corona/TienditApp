import { useCallback, useEffect, useState } from 'react';
import { cashApi } from '../../api/cashApi';
import type { DailyCashCutDto, InventoryLocationDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { getTodayInputDate } from './cashFormat';

interface DailyCutProps {
  locations: InventoryLocationDto[];
}

/** Payments of a day by method, user and branch, for every drawer of the business. */
export default function DailyCut({ locations }: DailyCutProps) {
  const [date, setDate] = useState(getTodayInputDate());
  const [locationId, setLocationId] = useState('');
  const [userName, setUserName] = useState('');
  const [cut, setCut] = useState<DailyCashCutDto | null>(null);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const { data } = await cashApi.getDailyCut({
        date: date || undefined,
        inventoryLocationId: locationId || undefined,
        userName: userName.trim() || undefined,
      });
      setCut(data);
    } catch {
      setCut(null);
    } finally {
      setLoading(false);
    }
  }, [date, locationId, userName]);

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
            <label className="form-label" htmlFor="cut-location">Sucursal</label>
            <select id="cut-location" className="form-select" value={locationId} onChange={(event) => setLocationId(event.target.value)}>
              <option value="">Todas</option>
              {locations.map((location) => (
                <option key={location.id} value={location.id}>{location.name}</option>
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
                  {cut.byLocation.length === 0 ? <li className="text-muted">Sin datos</li> : cut.byLocation.map((item) => (
                    <li key={`${item.inventoryLocationId || 'none'}-${item.paymentsCount}`}>{item.locationName}: {item.paymentsCount} pagos / {formatMoney(item.totalAmount)}</li>
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
