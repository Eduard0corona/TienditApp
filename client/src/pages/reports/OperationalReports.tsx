import { useCallback, useEffect, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import { reportsApi } from '../../api/reportsApi';
import { getPaymentMethodLabel } from '../../utils/labels';
import { marketingApi } from '../../api/marketingApi';
import type { OperationalReportDto } from '../../types';
import { getApiErrorMessage } from '../../api/errors';

export default function OperationalReports() {
  const [report, setReport] = useState<OperationalReportDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [promoMessage, setPromoMessage] = useState('');
  const [minimumOrders, setMinimumOrders] = useState('1');
  // A second click would message every customer twice.
  const [sendingPromotion, setSendingPromotion] = useState(false);

  const loadReport = useCallback(async () => {
    setLoading(true);
    try {
      const params: { fromDate?: string; toDate?: string } = {};
      if (fromDate) params.fromDate = new Date(`${fromDate}T00:00:00`).toISOString();
      if (toDate) params.toDate = new Date(`${toDate}T23:59:59`).toISOString();
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

  const sendPromotion = async () => {
    if (sendingPromotion) return;
    setError('');
    setSuccess('');
    const minimumCompletedOrders = Number(minimumOrders);
    if (!promoMessage.trim()) {
      setError('El mensaje promocional es obligatorio.');
      return;
    }
    if (Number.isNaN(minimumCompletedOrders) || minimumCompletedOrders <= 0) {
      setError('Cantidad mínima de órdenes inválida.');
      return;
    }

    if (!window.confirm('Se enviara el mensaje por WhatsApp a todos los clientes que cumplan el filtro. ¿Continuar?')) return;

    setSendingPromotion(true);
    try {
      const { data } = await marketingApi.sendPromotion({
        message: promoMessage.trim(),
        minimumCompletedOrders,
        lastPurchaseFrom: fromDate ? new Date(`${fromDate}T00:00:00`).toISOString() : undefined,
      });
      setSuccess(`Promoción enviada a ${data.sent} clientes.`);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo enviar la promoción.'));
    } finally {
      setSendingPromotion(false);
    }
  };

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
      {success && (
        <div className="alert alert-success alert-dismissible fade show">
          {success}
          <button type="button" className="btn-close" onClick={() => setSuccess('')}></button>
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
                  <div className="h4 mb-0">${report.netRevenue.toFixed(2)}</div>
                  <div className="text-muted small">
                    Con IVA: ${report.totalRevenue.toFixed(2)} · IVA: ${report.totalTax.toFixed(2)}
                  </div>
                </div>
              </div>
            </div>
            <div className="col-md-3 mb-2">
              <div className="card mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">Costo de ventas</div>
                  <div className="h4 mb-0">${report.totalCogs.toFixed(2)}</div>
                </div>
              </div>
            </div>
            <div className="col-md-3 mb-2">
              <div className="card mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">Utilidad bruta</div>
                  <div className="h4 mb-0">${report.grossProfit.toFixed(2)}</div>
                </div>
              </div>
            </div>
            <div className="col-md-3 mb-2">
              <div className="card mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">Compras</div>
                  <div className="h4 mb-0">${report.totalPurchases.toFixed(2)}</div>
                </div>
              </div>
            </div>
          </div>

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
                              <td className="text-end">${total.toFixed(2)}</td>
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

          <div className="card">
            <div className="card-body">
              <h5 className="mb-3">Promoción segmentada</h5>
              <div className="row">
                <div className="col-md-2 mb-2">
                  <label className="form-label">Mín. órdenes</label>
                  <input
                    type="number"
                    min={1}
                    className="form-control"
                    value={minimumOrders}
                    onChange={(e) => setMinimumOrders(e.target.value)}
                  />
                </div>
                <div className="col-md-8 mb-2">
                  <label className="form-label">Mensaje</label>
                  <input
                    className="form-control"
                    value={promoMessage}
                    onChange={(e) => setPromoMessage(e.target.value)}
                    placeholder="Ej. Promo de semana: 15% en tu siguiente compra."
                  />
                </div>
                <div className="col-md-2 mb-2 d-flex align-items-end">
                  <button className="btn btn-primary w-100" onClick={sendPromotion} disabled={sendingPromotion}>
                    {sendingPromotion ? 'Enviando...' : 'Enviar'}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </>
      ) : null}
    </>
  );
}
