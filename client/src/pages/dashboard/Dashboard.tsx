import { useCallback, useEffect, useMemo, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import { useAuth } from '../../hooks/useAuth';
import { dashboardApi } from '../../api/dashboardApi';
import { useOrderSignalR } from '../../hooks/useOrderSignalR';
import type { DashboardMetricsDto } from '../../types';

function getTodayDateInput() {
  const now = new Date();
  const y = now.getFullYear();
  const m = String(now.getMonth() + 1).padStart(2, '0');
  const d = String(now.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

export default function Dashboard() {
  const { user } = useAuth();
  const today = getTodayDateInput();
  const [fromDate, setFromDate] = useState(today);
  const [toDate, setToDate] = useState(today);
  const [metrics, setMetrics] = useState<DashboardMetricsDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [realtimeMessage, setRealtimeMessage] = useState('');
  const { onOrderCreated, onOrderStatusChanged, onOrderCancelled } = useOrderSignalR();

  const statusRows = useMemo(
    () => Object.entries(metrics?.ordersByStatus ?? {}).sort((a, b) => b[1] - a[1]),
    [metrics],
  );

  const loadMetrics = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const from = new Date(`${fromDate}T00:00:00`).toISOString();
      const to = new Date(`${toDate}T23:59:59`).toISOString();
      const { data } = await dashboardApi.getMetrics({ from, to });
      setMetrics(data);
    } catch {
      setError('No se pudieron cargar las metricas del dashboard.');
    } finally {
      setLoading(false);
    }
  }, [fromDate, toDate]);

  useEffect(() => {
    loadMetrics();
  }, [loadMetrics]);

  useEffect(() => {
    const createdOff = onOrderCreated((payload) => {
      setRealtimeMessage(`Nueva orden: ${payload.orderNumber}`);
      loadMetrics();
    });
    const changedOff = onOrderStatusChanged(() => {
      setRealtimeMessage('Cambio de estado de orden recibido');
      loadMetrics();
    });
    const cancelledOff = onOrderCancelled(() => {
      setRealtimeMessage('Se recibio una cancelacion de orden');
      loadMetrics();
    });

    return () => {
      createdOff();
      changedOff();
      cancelledOff();
    };
  }, [loadMetrics, onOrderCancelled, onOrderCreated, onOrderStatusChanged]);

  return (
    <>
      <PageTitle title="Dashboard" subtitle="Operacion" />

      <div className="row">
        <div className="col-12">
          <div className="card widget-inline">
            <div className="card-body d-flex justify-content-between flex-wrap gap-3">
              <div>
                <h4 className="mt-0 mb-1">Bienvenido, {user?.firstName}!</h4>
                <p className="text-muted mb-0">
                  Vista operativa para monitorear ordenes, ingresos y productos vendidos.
                </p>
              </div>
              <div className="d-flex flex-wrap gap-2">
                <input
                  type="date"
                  className="form-control form-control-sm w-auto flex-grow-1"
                  aria-label="Desde"
                  value={fromDate}
                  onChange={(e) => setFromDate(e.target.value)}
                />
                <input
                  type="date"
                  className="form-control form-control-sm w-auto flex-grow-1"
                  aria-label="Hasta"
                  value={toDate}
                  onChange={(e) => setToDate(e.target.value)}
                />
                <button className="btn btn-sm btn-primary" onClick={loadMetrics}>
                  Actualizar
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError('')}></button>
        </div>
      )}

      {realtimeMessage && (
        <div className="alert alert-info alert-dismissible fade show">
          {realtimeMessage}
          <button type="button" className="btn-close" onClick={() => setRealtimeMessage('')}></button>
        </div>
      )}

      <div className="row">
        <div className="col-sm-6 col-xl-3">
          <div className="card tilebox-one">
            <div className="card-body">
              <i className="ri-shopping-basket-2-line float-end text-muted"></i>
              <h6 className="text-muted text-uppercase mt-0">Ordenes</h6>
              <h2 className="m-b-20">{loading ? '-' : metrics?.totalOrders ?? 0}</h2>
              <span className="badge bg-primary">
                En progreso: {loading ? '-' : metrics?.inProgressOrders ?? 0}
              </span>
            </div>
          </div>
        </div>

        <div className="col-sm-6 col-xl-3">
          <div className="card tilebox-one">
            <div className="card-body">
              <i className="ri-archive-line float-end text-muted"></i>
              <h6 className="text-muted text-uppercase mt-0">Ingresos</h6>
              <h2 className="m-b-20">${loading ? '-' : (metrics?.totalRevenue ?? 0).toFixed(2)}</h2>
              <span className="badge bg-success">
                Ticket prom.: ${loading ? '-' : (metrics?.averageOrderValue ?? 0).toFixed(2)}
              </span>
            </div>
          </div>
        </div>

        <div className="col-sm-6 col-xl-3">
          <div className="card tilebox-one">
            <div className="card-body">
              <i className="ri-vip-diamond-line float-end text-muted"></i>
              <h6 className="text-muted text-uppercase mt-0">Catalogo activo</h6>
              <h2 className="m-b-20">
                {loading ? '-' : `${metrics?.activeProducts ?? 0} / ${metrics?.activeServices ?? 0}`}
              </h2>
              <span className="badge bg-info">Productos / Servicios</span>
            </div>
          </div>
        </div>

        <div className="col-sm-6 col-xl-3">
          <div className="card tilebox-one">
            <div className="card-body">
              <i className="ri-group-line float-end text-muted"></i>
              <h6 className="text-muted text-uppercase mt-0">Clientes unicos</h6>
              <h2 className="m-b-20">{loading ? '-' : metrics?.uniqueCustomers ?? 0}</h2>
              <span className="badge bg-warning">
                Completadas: {loading ? '-' : metrics?.completedOrders ?? 0}
              </span>
            </div>
          </div>
        </div>
      </div>

      <div className="row">
        <div className="col-lg-6">
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mb-3">Ordenes por estado</h4>
              {loading ? (
                <p className="text-muted mb-0">Cargando...</p>
              ) : statusRows.length === 0 ? (
                <p className="text-muted mb-0">Sin datos en el rango seleccionado.</p>
              ) : (
                <div className="table-responsive">
                  <table className="table table-sm mb-0">
                    <thead>
                      <tr>
                        <th>Estado</th>
                        <th className="text-end">Total</th>
                      </tr>
                    </thead>
                    <tbody>
                      {statusRows.map(([status, count]) => (
                        <tr key={status}>
                          <td>{status}</td>
                          <td className="text-end fw-semibold">{count}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </div>
        </div>

        <div className="col-lg-6">
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mb-3">Top vendidos</h4>
              {loading ? (
                <p className="text-muted mb-0">Cargando...</p>
              ) : (metrics?.topSellingItems.length ?? 0) === 0 ? (
                <p className="text-muted mb-0">No hay ventas completadas en el rango seleccionado.</p>
              ) : (
                <div className="table-responsive">
                  <table className="table table-sm mb-0">
                    <thead>
                      <tr>
                        <th>Item</th>
                        <th>Tipo</th>
                        <th className="text-end">Cantidad</th>
                        <th className="text-end">Monto</th>
                      </tr>
                    </thead>
                    <tbody>
                      {metrics?.topSellingItems.map((item) => (
                        <tr key={item.productId}>
                          <td>{item.productName}</td>
                          <td>{item.itemType}</td>
                          <td className="text-end">{item.quantitySold}</td>
                          <td className="text-end">${item.totalAmount.toFixed(2)}</td>
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
    </>
  );
}
