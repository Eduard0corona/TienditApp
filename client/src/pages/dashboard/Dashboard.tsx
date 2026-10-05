import { useCallback, useEffect, useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { dashboardApi } from '../../api/dashboardApi';
import { tenantApi } from '../../api/tenantApi';
import { getApiErrorMessage } from '../../api/errors';
import { queryKeys, useAttention } from '../../api/queries';
import { useOrderSignalR } from '../../hooks/useOrderSignalR';
import { getAccess } from '../../utils/access';
import { describeAttentionItems } from '../../utils/attention';
import { formatMoney } from '../../utils/money';
import { getCatalogItemTypeLabel, getOrderStatusLabel } from '../../utils/labels';
import type { DashboardMetricsDto, TenantSetupDto, TenantSetupStepKey } from '../../types';

// Several events usually arrive together (sale + payment + close); one reload covers them.
const EVENT_RELOAD_DELAY_MS = 600;

const SETUP_STEPS: Array<{ key: TenantSetupStepKey; label: string; path: string }> = [
  { key: 'salesPolicy', label: 'Define si tus precios incluyen IVA', path: '/setup' },
  { key: 'catalog', label: 'Agrega tus productos', path: '/products/new' },
  { key: 'cash', label: 'Abre la caja', path: '/cash' },
  { key: 'firstSale', label: 'Haz tu primera venta', path: '/sell' },
  { key: 'team', label: 'Invita a tu equipo', path: '/employees' },
  { key: 'logo', label: 'Sube tu logotipo', path: '/settings/business' },
];

function getTodayDateInput() {
  const now = new Date();
  const y = now.getFullYear();
  const m = String(now.getMonth() + 1).padStart(2, '0');
  const d = String(now.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/** Inicio: what was charged, what is still owed and what needs attention, plus the first steps. */
export default function Dashboard() {
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const access = getAccess(user, capabilities);
  const today = getTodayDateInput();
  const [fromDate, setFromDate] = useState(today);
  const [toDate, setToDate] = useState(today);
  const [metrics, setMetrics] = useState<DashboardMetricsDto | null>(null);
  const [setup, setSetup] = useState<TenantSetupDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const { onOrderCreated, onOrderStatusChanged, onOrderCancelled } = useOrderSignalR();
  const queryClient = useQueryClient();
  const attention = useAttention();
  const notices = describeAttentionItems(attention.data?.items ?? []);
  const isToday = fromDate === today && toDate === today;

  const statusRows = useMemo(
    () => Object.entries(metrics?.ordersByStatus ?? {}).sort((a, b) => b[1] - a[1]),
    [metrics],
  );

  const loadMetrics = useCallback(async () => {
    try {
      const from = new Date(`${fromDate}T00:00:00`).toISOString();
      const to = new Date(`${toDate}T23:59:59`).toISOString();
      const { data } = await dashboardApi.getMetrics({ from, to });
      setMetrics(data);
      setError('');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron cargar las cifras de Inicio.'));
    } finally {
      setLoading(false);
    }
  }, [fromDate, toDate]);

  useEffect(() => {
    void loadMetrics();
  }, [loadMetrics]);

  useEffect(() => {
    // The first steps are for whoever configures the business.
    if (!access.businessSettings) return;
    let cancelled = false;
    tenantApi.getSetup()
      .then(({ data }) => {
        if (!cancelled) setSetup(data);
      })
      .catch(() => {
        if (!cancelled) setSetup(null);
      });
    return () => {
      cancelled = true;
    };
  }, [access.businessSettings]);

  useEffect(() => {
    let reloadTimeoutId: number | undefined;
    const scheduleReload = () => {
      window.clearTimeout(reloadTimeoutId);
      reloadTimeoutId = window.setTimeout(() => {
        void loadMetrics();
        void queryClient.invalidateQueries({ queryKey: queryKeys.attention });
      }, EVENT_RELOAD_DELAY_MS);
    };
    const unsubscribers = [onOrderCreated(scheduleReload), onOrderStatusChanged(scheduleReload), onOrderCancelled(scheduleReload)];
    return () => {
      window.clearTimeout(reloadTimeoutId);
      unsubscribers.forEach((unsubscribe) => unsubscribe());
    };
  }, [loadMetrics, onOrderCancelled, onOrderCreated, onOrderStatusChanged, queryClient]);

  const pendingSetupSteps = setup ? SETUP_STEPS.filter((step) => !setup.steps.find((item) => item.key === step.key)?.done) : [];
  const doneSetupSteps = SETUP_STEPS.length - pendingSetupSteps.length;
  const value = (amount: number | undefined) => (loading ? '—' : formatMoney(amount ?? 0));
  const count = (amount: number | undefined) => (loading ? '—' : String(amount ?? 0));

  return (
    <>
      <PageTitle title="Inicio" subtitle="Operación diaria" />

      <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-3">
        <div>
          <h2 className="h4 mb-0">¡Hola, {user?.firstName}!</h2>
          <p className="text-muted mb-0">{isToday ? 'Así va el día.' : 'Cifras del periodo elegido.'}</p>
        </div>
        <div className="d-flex flex-wrap align-items-center gap-2">
          <label className="visually-hidden" htmlFor="dashboard-from">Desde</label>
          <input
            id="dashboard-from"
            type="date"
            className="form-control form-control-sm w-auto"
            value={fromDate}
            max={toDate}
            onChange={(e) => setFromDate(e.target.value)}
          />
          <label className="visually-hidden" htmlFor="dashboard-to">Hasta</label>
          <input
            id="dashboard-to"
            type="date"
            className="form-control form-control-sm w-auto"
            value={toDate}
            min={fromDate}
            onChange={(e) => setToDate(e.target.value)}
          />
          {!isToday && (
            <button
              type="button"
              className="btn btn-sm btn-light"
              onClick={() => {
                setFromDate(today);
                setToDate(today);
              }}
            >
              Hoy
            </button>
          )}
          {access.sell && (
            <Link to="/sell" className="btn btn-sm btn-primary">
              <i className="ri-add-line me-1" aria-hidden="true"></i>Nueva venta
            </Link>
          )}
        </div>
      </div>

      {error && (
        <div className="alert alert-danger d-flex justify-content-between align-items-center gap-2">
          <span>{error}</span>
          <button type="button" className="btn btn-sm btn-light" onClick={() => void loadMetrics()}>Reintentar</button>
        </div>
      )}

      <section className="card" aria-labelledby="attention-title">
        <div className="card-body">
          <h3 id="attention-title" className="h5 mb-2">Requiere atención</h3>
          {attention.isLoading && <p className="text-muted mb-0">Revisando…</p>}
          {attention.isError && <p className="text-muted mb-0">No se pudo revisar lo pendiente.</p>}
          {attention.isSuccess && notices.length === 0 && (
            <p className="text-muted mb-0"><i className="ri-check-double-line me-1" aria-hidden="true"></i>Nada pendiente por ahora.</p>
          )}
          {notices.length > 0 && (
            <ul className="list-unstyled mb-0 d-grid gap-2">
              {notices.map((notice) => (
                <li key={notice.key}>
                  <Link to={notice.path} className={`attention-row attention-${notice.tone}`}>
                    <i className={notice.icon} aria-hidden="true"></i>
                    <span className="flex-grow-1">{notice.text}</span>
                    <i className="ri-arrow-right-s-line" aria-hidden="true"></i>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>

      {setup && pendingSetupSteps.length > 0 && (
        <div className="card border-primary-subtle">
          <div className="card-body">
            <div className="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-2">
              <h3 className="h5 mb-0">Primeros pasos</h3>
              <span className="text-muted small">{doneSetupSteps} de {SETUP_STEPS.length} listos</span>
            </div>
            <div className="progress mb-3" style={{ height: 6 }} aria-hidden="true">
              <div className="progress-bar" style={{ width: `${(doneSetupSteps / SETUP_STEPS.length) * 100}%` }}></div>
            </div>
            <div className="d-flex flex-wrap gap-2">
              {pendingSetupSteps.map((step) => (
                <Link key={step.key} to={step.path} className="btn btn-light btn-sm">
                  <i className="ri-checkbox-blank-circle-line me-1" aria-hidden="true"></i>{step.label}
                </Link>
              ))}
            </div>
          </div>
        </div>
      )}

      <div className="row g-3 mb-3">
        <div className="col-sm-6 col-xl-3">
          <div className="card h-100 mb-0">
            <div className="card-body">
              <div className="text-muted small fw-semibold text-uppercase">Cobrado</div>
              <div className="fs-2 fw-bold ta-money">{value(metrics?.totalRevenue)}</div>
              <div className="small text-muted">
                {loading ? '' : `${metrics?.paidOrders ?? 0} ${metrics?.paidOrders === 1 ? 'pedido pagado' : 'pedidos pagados'}`}
              </div>
            </div>
          </div>
        </div>
        <div className="col-sm-6 col-xl-3">
          <div className="card h-100 mb-0">
            <div className="card-body">
              <div className="text-muted small fw-semibold text-uppercase">Ticket promedio</div>
              <div className="fs-2 fw-bold ta-money">{value(metrics?.averageOrderValue)}</div>
              <div className="small text-muted">Por pedido pagado</div>
            </div>
          </div>
        </div>
        <div className="col-sm-6 col-xl-3">
          <div className={`card h-100 mb-0 ${!loading && (metrics?.outstandingBalance ?? 0) > 0 ? 'border-warning' : ''}`}>
            <div className="card-body">
              <div className="text-muted small fw-semibold text-uppercase">Por cobrar</div>
              <div className="fs-2 fw-bold ta-money">{value(metrics?.outstandingBalance)}</div>
              <div className="small">
                {!loading && (metrics?.unpaidOrders ?? 0) > 0 && access.orders ? (
                  <Link to="/orders">{metrics?.unpaidOrders} {metrics?.unpaidOrders === 1 ? 'pedido con saldo' : 'pedidos con saldo'}</Link>
                ) : (
                  <span className="text-muted">Sin saldos pendientes</span>
                )}
              </div>
            </div>
          </div>
        </div>
        <div className="col-sm-6 col-xl-3">
          <div className="card h-100 mb-0">
            <div className="card-body">
              <div className="text-muted small fw-semibold text-uppercase">Pedidos</div>
              <div className="fs-2 fw-bold">{count(metrics?.totalOrders)}</div>
              <div className="small text-muted">
                {loading ? '' : `${metrics?.inProgressOrders ?? 0} en curso · ${metrics?.pendingOrders ?? 0} nuevos · ${metrics?.cancelledOrders ?? 0} cancelados`}
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="row g-3">
        <div className="col-lg-6">
          <div className="card h-100 mb-0">
            <div className="card-body">
              <h3 className="h5 mb-3">Más vendidos</h3>
              {loading ? (
                <p className="text-muted mb-0">Cargando…</p>
              ) : (metrics?.topSellingItems.length ?? 0) === 0 ? (
                <p className="text-muted mb-0">Aún no hay ventas cobradas en el periodo.</p>
              ) : (
                <div className="table-responsive">
                  <table className="table table-sm mb-0">
                    <thead>
                      <tr>
                        <th>Producto</th>
                        <th>Tipo</th>
                        <th className="text-end">Cantidad</th>
                        <th className="text-end">Importe</th>
                      </tr>
                    </thead>
                    <tbody>
                      {metrics?.topSellingItems.map((item) => (
                        <tr key={item.productId}>
                          <td>{item.productName}</td>
                          <td>{getCatalogItemTypeLabel(item.itemType)}</td>
                          <td className="text-end">{item.quantitySold.toLocaleString('es-MX', { maximumFractionDigits: 3 })}</td>
                          <td className="text-end">{formatMoney(item.totalAmount)}</td>
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
          <div className="card h-100 mb-0">
            <div className="card-body">
              <h3 className="h5 mb-3">Pedidos por estado</h3>
              {loading ? (
                <p className="text-muted mb-0">Cargando…</p>
              ) : statusRows.length === 0 ? (
                <p className="text-muted mb-0">Sin pedidos en el periodo.</p>
              ) : (
                <div className="table-responsive">
                  <table className="table table-sm mb-0">
                    <tbody>
                      {statusRows.map(([status, total]) => (
                        <tr key={status}>
                          <td>{getOrderStatusLabel(status)}</td>
                          <td className="text-end fw-semibold">{total}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              <p className="small text-muted mt-3 mb-0">
                Activos en catálogo: {count(metrics?.activeProducts)} productos y {count(metrics?.activeServices)} servicios ·{' '}
                {count(metrics?.uniqueCustomers)} clientes distintos.
              </p>
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
