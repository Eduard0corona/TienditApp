import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import SalesByBranchCard from '../../components/reports/SalesByBranchCard';
import { reportsApi } from '../../api/reportsApi';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import type { OperationalReportDto } from '../../types';
import { getApiErrorMessage } from '../../api/errors';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
import { businessDayEnd, businessDayStart } from '../../utils/dates';

export default function OperationalReports() {
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const canSendCampaigns = getAccess(user, capabilities).campaigns;
  const [report, setReport] = useState<OperationalReportDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');

  const loadReport = useCallback(async () => {
    setLoading(true);
    try {
      const params: { fromDate?: string; toDate?: string } = {};
      if (fromDate) params.fromDate = businessDayStart(fromDate);
      if (toDate) params.toDate = businessDayEnd(toDate);
      const { data } = await reportsApi.getOperationalSummary(params);
      setReport(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo cargar el reporte.'));
    } finally {
      setLoading(false);
    }
  }, [fromDate, toDate]);

  useEffect(() => {
    loadReport();
  }, [loadReport]);

  const paymentRows = report ? Object.entries(report.revenueByPaymentMethod) : [];

  return (
    <>
      <PageTitle title="Reportes" subtitle="Resumen operativo" />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError('')}></button>
        </div>
      )}

      <div className="card mb-3">
        <div className="card-body">
          <div className="row align-items-end">
            <div className="col-md-3 mb-2">
              <label className="form-label">Desde</label>
              <input type="date" className="form-control" value={fromDate} onChange={(e) => setFromDate(e.target.value)} />
            </div>
            <div className="col-md-3 mb-2">
              <label className="form-label">Hasta</label>
              <input type="date" className="form-control" value={toDate} onChange={(e) => setToDate(e.target.value)} />
            </div>
            <div className="col-md-2 mb-2">
              <button className="btn btn-primary w-100" onClick={loadReport}>Actualizar</button>
            </div>
          </div>
        </div>
      </div>

      {loading ? (
        <div className="text-center py-4">
          <div className="spinner-border text-primary" role="status"></div>
        </div>
      ) : report ? (
        <>
          <div className="row mb-3">
            <div className="col-md-3 mb-2">
              <div className="card mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">Ventas netas (sin IVA)</div>
                  <div className="h4 mb-0">{formatMoney(report.netRevenue)}</div>
                  <div className="text-muted small">
                    Con IVA: {formatMoney(report.totalRevenue)} · IVA: {formatMoney(report.totalTax)}
                  </div>
                </div>
              </div>
            </div>
            <div className="col-md-3 mb-2">
              <div className="card mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">Costo de ventas</div>
                  <div className="h4 mb-0">{formatMoney(report.totalCogs)}</div>
                </div>
              </div>
            </div>
            <div className="col-md-3 mb-2">
              <div className="card mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">Utilidad bruta</div>
                  <div className="h4 mb-0">{formatMoney(report.grossProfit)}</div>
                </div>
              </div>
            </div>
            <div className="col-md-3 mb-2">
              <div className="card mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">Compras</div>
                  <div className="h4 mb-0">{formatMoney(report.totalPurchases)}</div>
                </div>
              </div>
            </div>
          </div>

          <SalesByBranchCard />

          <div className="row mb-3">
            <div className="col-md-6">
              <div className="card h-100 mb-0">
                <div className="card-body">
                  <h5 className="mb-3">Operación</h5>
                  <p className="mb-1"><strong>Órdenes completadas:</strong> {report.ordersCompleted}</p>
                  <p className="mb-1"><strong>Citas programadas:</strong> {report.appointmentsScheduled}</p>
                  <p className="mb-0"><strong>Citas completadas:</strong> {report.appointmentsCompleted}</p>
                </div>
              </div>
            </div>
            <div className="col-md-6">
              <div className="card h-100 mb-0">
                <div className="card-body">
                  <h5 className="mb-3">Ingresos por método de pago</h5>
                  {paymentRows.length === 0 ? (
                    <p className="text-muted mb-0">Sin pagos en el periodo.</p>
                  ) : (
                    <div className="table-responsive">
                      <table className="table table-sm table-striped mb-0">
                        <thead>
                          <tr>
                            <th>Método</th>
                            <th className="text-end">Total</th>
                          </tr>
                        </thead>
                        <tbody>
                          {paymentRows.map(([method, total]) => (
                            <tr key={method}>
                              <td>{getPaymentMethodLabel(method)}</td>
                              <td className="text-end">{formatMoney(total)}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              </div>
            </div>
          </div>

          {canSendCampaigns && (
            <div className="card">
              <div className="card-body d-flex flex-wrap justify-content-between align-items-center gap-2">
                <div>
                  <h5 className="mb-1">Promociones por WhatsApp</h5>
                  <p className="text-muted small mb-0">
                    Las campañas se envían en segundo plano, respetan las bajas y guardan el resultado de cada mensaje.
                  </p>
                </div>
                <Link to="/campaigns" className="btn btn-primary">Ir a Campañas</Link>
              </div>
            </div>
          )}
        </>
      ) : null}
    </>
  );
}
