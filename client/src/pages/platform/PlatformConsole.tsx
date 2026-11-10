import { useEffect, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import PageTitle from '../../components/common/PageTitle';
import FakeRequestsWatch from '../../components/platform/FakeRequestsWatch';
import TenantBillingModal from '../../components/platform/TenantBillingModal';
import { getApiErrorMessage } from '../../api/errors';
import {
  platformApi,
  SUBSCRIPTION_STATUS_LABELS,
  SUBSCRIPTION_STATUS_TONES,
  type PlatformTenant,
  type SubscriptionStatus,
} from '../../api/platformApi';
import { formatDate } from '../../utils/dates';
import { BUSINESS_TYPE_LABELS } from '../../utils/labels';
import { formatMoney } from '../../utils/money';

type StatusFilter = '' | 'none' | `${SubscriptionStatus}`;

const PAGE_SIZE = 20;
/** The search is asked for when typing stops, not with each key. */
const SEARCH_DELAY_MS = 300;

function dueDate(tenant: PlatformTenant) {
  if (tenant.subscriptionStatus === null || tenant.subscriptionStatus === undefined) return '—';
  if (tenant.paidThrough) return `Pagado al ${formatDate(tenant.paidThrough)}`;
  return `Prueba al ${formatDate(tenant.trialEndsAt)}`;
}

/**
 * Platform console: how the businesses are doing with billing, and the actions on each one (plan, payments, trial,
 * suspension). Only the platform admin reaches it; the API checks it too.
 */
export default function PlatformConsole() {
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<StatusFilter>('');
  const [planId, setPlanId] = useState('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<PlatformTenant | null>(null);

  useEffect(() => {
    const typed = searchInput.trim();
    if (typed === search) return;
    const id = window.setTimeout(() => {
      setSearch(typed);
      setPage(1);
    }, SEARCH_DELAY_MS);
    return () => window.clearTimeout(id);
  }, [searchInput, search]);

  const overview = useQuery({ queryKey: ['platform', 'overview'], queryFn: platformApi.getOverview });
  const plans = useQuery({ queryKey: ['platform', 'plans'], queryFn: platformApi.getPlans });
  const tenants = useQuery({
    queryKey: ['platform', 'tenants', search, status, planId, page],
    queryFn: () => platformApi.getTenants({
      search: search || undefined,
      withoutPlan: status === 'none' || undefined,
      subscriptionStatus: status !== '' && status !== 'none' ? (Number(status) as SubscriptionStatus) : undefined,
      planId: planId || undefined,
      pageNumber: page,
      pageSize: PAGE_SIZE,
    }),
    placeholderData: keepPreviousData,
  });

  const o = overview.data;
  const cards = o ? [
    { label: 'Negocios', value: String(o.totalTenants), hint: `${o.activeTenants} con acceso` },
    { label: 'En prueba', value: String(o.inTrial), hint: `${o.trialsEndingIn7Days} terminan en 7 días` },
    { label: 'Al corriente', value: String(o.paying), hint: `${formatMoney(o.monthlyRecurringRevenue)} al mes` },
    { label: 'Pago vencido', value: String(o.pastDue), hint: 'En días de gracia', tone: o.pastDue > 0 ? 'warning' : undefined },
    { label: 'Sin acceso', value: String(o.tenantsWithoutAccess), hint: `${o.suspended} suspendidos · ${o.cancelled} cancelados`, tone: o.tenantsWithoutAccess > 0 ? 'danger' : undefined },
    { label: 'Cobrado (30 días)', value: formatMoney(o.paymentsLast30Days), hint: `${o.withoutPlan} negocios sin plan` },
  ] : [];

  const page$ = tenants.data;
  // A page that no longer exists (an action emptied the last one) is not «no businesses»: the last one is read instead.
  const pageGone = !!page$ && !tenants.isPlaceholderData && page$.items.length === 0 && page > 1 && page$.totalPages < page;
  if (pageGone) setPage(Math.max(1, page$.totalPages));
  const resetPage = () => setPage(1);
  const filtered = search !== '' || status !== '' || planId !== '';
  // The row as the list has it now: actions in the modal refresh the list.
  const selectedTenant = (selected && page$?.items.find((item) => item.tenantId === selected.tenantId)) ?? selected;

  return (
    <>
      <PageTitle title="Negocios" />

      {overview.isError && (
        <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
          <span className="me-auto">{getApiErrorMessage(overview.error, 'No se pudo cargar el resumen.')}</span>
          <button type="button" className="btn btn-sm btn-light" disabled={overview.isFetching} onClick={() => void overview.refetch()}>
            Reintentar
          </button>
        </div>
      )}
      <div className="row g-2 mb-3">
        {cards.map((card) => (
          <div key={card.label} className="col-6 col-lg-4 col-xxl-2">
            <div className={`card h-100 mb-0 ${card.tone ? `border-${card.tone}` : ''}`}>
              <div className="card-body py-2">
                <div className="text-muted small">{card.label}</div>
                <div className="h4 mb-0">{card.value}</div>
                <div className="text-muted small">{card.hint}</div>
              </div>
            </div>
          </div>
        ))}
      </div>

      <div className="card">
        <div className="card-body">
          <div className="row g-2 mb-3">
            <div className="col-md-5">
              <label className="form-label small mb-1" htmlFor="platform-search">Buscar</label>
              <input id="platform-search" type="search" className="form-control" placeholder="Nombre, enlace o correo" maxLength={100} value={searchInput}
                onChange={(event) => setSearchInput(event.target.value)} />
            </div>
            <div className="col-6 col-md-3">
              <label className="form-label small mb-1" htmlFor="platform-status">Estado</label>
              <select id="platform-status" className="form-select" value={status}
                onChange={(event) => { setStatus(event.target.value as StatusFilter); resetPage(); }}>
                <option value="">Todos</option>
                <option value="none">Sin plan</option>
                {Object.entries(SUBSCRIPTION_STATUS_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>{label}</option>
                ))}
              </select>
            </div>
            <div className="col-6 col-md-4">
              <label className="form-label small mb-1" htmlFor="platform-plan">Plan</label>
              <select id="platform-plan" className="form-select" value={planId}
                onChange={(event) => { setPlanId(event.target.value); resetPage(); }}>
                <option value="">Todos</option>
                {(plans.data ?? []).map((plan) => <option key={plan.id} value={plan.id}>{plan.name}</option>)}
              </select>
            </div>
          </div>

          {tenants.isError && (
            <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
              <span className="me-auto">{getApiErrorMessage(tenants.error, 'No se pudieron cargar los negocios.')}</span>
              <button type="button" className="btn btn-sm btn-light" disabled={tenants.isFetching} onClick={() => void tenants.refetch()}>
                Reintentar
              </button>
            </div>
          )}
          <div className="table-responsive">
            {/* While another filter is read, the rows of the one before stay, marked as busy. */}
            <table className="table align-middle mb-0" aria-busy={tenants.isPlaceholderData || undefined}>
              <thead>
                <tr>
                  <th>Negocio</th>
                  <th>Plan</th>
                  <th>Estado</th>
                  <th>Vence</th>
                  <th className="text-center">Sucursales</th>
                  <th className="text-center">Usuarios</th>
                  <th>Última venta</th>
                  <th className="text-end">Acciones</th>
                </tr>
              </thead>
              <tbody>
                {tenants.isLoading && (
                  <tr>
                    <td colSpan={8} className="text-center py-4">
                      <div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando los negocios…</span></div>
                    </td>
                  </tr>
                )}
                {/* Only for what was read for these filters, not while the rows of others stay. */}
                {page$ && !tenants.isPlaceholderData && page$.items.length === 0 && (
                  <tr>
                    <td colSpan={8} className="text-center text-muted py-4">
                      {filtered ? 'No hay negocios con esos filtros.' : 'Todavía no hay negocios.'}
                    </td>
                  </tr>
                )}
                {page$?.items.map((tenant) => (
                  <tr key={tenant.tenantId}>
                    <td>
                      <div className="fw-semibold">{tenant.name}</div>
                      <div className="small text-muted">
                        {BUSINESS_TYPE_LABELS[tenant.businessType] ?? 'Negocio'} · {tenant.slug}{tenant.contactEmail ? ` · ${tenant.contactEmail}` : ''}
                      </div>
                    </td>
                    <td>{tenant.planName ?? <span className="text-muted">Sin plan</span>}</td>
                    <td>
                      {tenant.subscriptionStatus !== null && tenant.subscriptionStatus !== undefined ? (
                        <span className={`badge text-bg-${SUBSCRIPTION_STATUS_TONES[tenant.subscriptionStatus]}`}>
                          {SUBSCRIPTION_STATUS_LABELS[tenant.subscriptionStatus]}
                        </span>
                      ) : tenant.tenantStatus !== 0 ? (
                        <span className="badge text-bg-danger">Suspendido</span>
                      ) : (
                        <span className="text-muted small">Sin límites</span>
                      )}
                    </td>
                    <td className="small">{dueDate(tenant)}</td>
                    <td className="text-center">{tenant.branchesCount}</td>
                    <td className="text-center">{tenant.usersCount}</td>
                    <td className="small">{formatDate(tenant.lastOrderAt)}</td>
                    <td className="text-end">
                      <button type="button" className="btn btn-sm btn-light" onClick={() => setSelected(tenant)}>
                        Cobranza
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {page$ && page$.totalPages > 1 && (
            <div className="d-flex justify-content-between align-items-center mt-3">
              <span className="small text-muted">Página {page$.pageNumber} de {page$.totalPages} · {page$.totalCount} negocios</span>
              <div className="btn-group">
                {/* From the page shown, and not while another is read: two clicks would go past the end. */}
                <button type="button" className="btn btn-sm btn-light" disabled={!page$.hasPreviousPage || tenants.isPlaceholderData}
                  onClick={() => setPage(page$.pageNumber - 1)}>Anterior</button>
                <button type="button" className="btn btn-sm btn-light" disabled={!page$.hasNextPage || tenants.isPlaceholderData}
                  onClick={() => setPage(page$.pageNumber + 1)}>Siguiente</button>
              </div>
            </div>
          )}
        </div>
      </div>

      <FakeRequestsWatch />

      {selectedTenant && <TenantBillingModal tenant={selectedTenant} plans={plans.data ?? []} onClose={() => setSelected(null)} />}
    </>
  );
}
