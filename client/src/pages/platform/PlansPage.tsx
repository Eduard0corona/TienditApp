import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import PageTitle from '../../components/common/PageTitle';
import { getApiErrorMessage } from '../../api/errors';
import { platformApi, type Plan } from '../../api/platformApi';
import { useToast } from '../../hooks/useToast';
import { formatMoney } from '../../utils/money';
import { MODULE_OPTIONS, SALE_MODULES, moduleLabels } from '../../utils/modules';

interface PlanForm {
  id?: string;
  code: string;
  name: string;
  description: string;
  monthlyPrice: string;
  modules: number;
  maxBranches: string;
  maxUsers: string;
  trialDays: string;
  sortOrder: string;
  isDefaultForNewBusinesses: boolean;
  isActive: boolean;
}

const EMPTY: PlanForm = {
  code: '',
  name: '',
  description: '',
  monthlyPrice: '',
  modules: SALE_MODULES,
  maxBranches: '1',
  maxUsers: '3',
  trialDays: '14',
  sortOrder: '0',
  isDefaultForNewBusinesses: false,
  isActive: true,
};

function toForm(plan: Plan): PlanForm {
  return {
    id: plan.id,
    code: plan.code,
    name: plan.name,
    description: plan.description ?? '',
    monthlyPrice: String(plan.monthlyPrice),
    modules: plan.modules,
    maxBranches: String(plan.maxBranches),
    maxUsers: String(plan.maxUsers),
    trialDays: String(plan.trialDays),
    sortOrder: String(plan.sortOrder),
    isDefaultForNewBusinesses: plan.isDefaultForNewBusinesses,
    isActive: plan.isActive,
  };
}

/**
 * Plans of the platform: price, modules a business on them may turn on, limits and free trial. Businesses
 * without a plan are not limited; the plan marked for new businesses gives the trial of those that sign up.
 */
export default function PlansPage() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const plans = useQuery({ queryKey: ['platform', 'plans'], queryFn: platformApi.getPlans });
  const [form, setForm] = useState<PlanForm | null>(null);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!form) return;
    const price = Number(form.monthlyPrice);
    if (!form.code.trim() || !form.name.trim() || !Number.isFinite(price) || price < 0) {
      setError('Escribe la clave, el nombre y un precio mensual de 0 o más.');
      return;
    }
    if ((form.modules & SALE_MODULES) === 0) {
      setError('Incluye al menos un tipo de venta: productos, servicios o alimentos.');
      return;
    }
    const body = {
      code: form.code.trim(),
      name: form.name.trim(),
      description: form.description.trim() || null,
      monthlyPrice: Math.round(price * 100) / 100,
      modules: form.modules,
      maxBranches: Number(form.maxBranches) || 1,
      maxUsers: Number(form.maxUsers) || 1,
      trialDays: Number(form.trialDays) || 0,
      sortOrder: Number(form.sortOrder) || 0,
      isDefaultForNewBusinesses: form.isDefaultForNewBusinesses,
    };
    setSaving(true);
    setError('');
    try {
      if (form.id) await platformApi.updatePlan(form.id, { ...body, id: form.id, isActive: form.isActive });
      else await platformApi.createPlan(body);
      toast.success(form.id ? 'Plan actualizado.' : 'Plan creado.');
      setForm(null);
      await queryClient.invalidateQueries({ queryKey: ['platform'] });
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar el plan.'));
    } finally {
      setSaving(false);
    }
  };

  const list = plans.data ?? [];

  return (
    <>
      <PageTitle title="Planes" />

      <div className="card">
        <div className="card-body">
          <div className="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3">
            <p className="text-muted small mb-0" style={{ maxWidth: '44rem' }}>
              Los negocios sin plan no tienen límites. Al quitar un módulo de un plan, los negocios en él lo apagan; el plan para
              negocios nuevos da la prueba gratis a los que se registran.
            </p>
            <button type="button" className="btn btn-primary btn-sm" onClick={() => { setForm(EMPTY); setError(''); }}>
              <i className="ri-add-line me-1" aria-hidden="true"></i>Nuevo plan
            </button>
          </div>

          {plans.isLoading && <div className="text-center py-4"><div className="spinner-border text-primary" role="status"></div></div>}
          {plans.isError && <div className="alert alert-danger">{getApiErrorMessage(plans.error, 'No se pudieron cargar los planes.')}</div>}
          {plans.isSuccess && list.length === 0 && (
            <div className="text-center text-muted py-4">Aún no hay planes.</div>
          )}

          {list.length > 0 && (
            <div className="table-responsive">
              <table className="table align-middle mb-0">
                <thead>
                  <tr>
                    <th>Plan</th>
                    <th className="text-end">Precio mensual</th>
                    <th>Módulos</th>
                    <th className="text-center">Sucursales</th>
                    <th className="text-center">Usuarios</th>
                    <th className="text-center">Prueba</th>
                    <th className="text-center">Negocios</th>
                    <th className="text-end">Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {list.map((plan) => (
                    <tr key={plan.id} className={plan.isActive ? '' : 'text-muted'}>
                      <td>
                        <div className="fw-semibold">{plan.name}</div>
                        <div className="small text-muted">
                          {plan.code}
                          {plan.isDefaultForNewBusinesses && <span className="badge text-bg-info ms-2">Negocios nuevos</span>}
                          {!plan.isActive && <span className="badge text-bg-secondary ms-2">Inactivo</span>}
                        </div>
                      </td>
                      <td className="text-end ta-money">{formatMoney(plan.monthlyPrice)}</td>
                      <td>
                        <div className="d-flex flex-wrap gap-1">
                          {moduleLabels(plan.modules).map((label) => (
                            <span key={label} className="badge text-bg-light border">{label}</span>
                          ))}
                        </div>
                      </td>
                      <td className="text-center">{plan.maxBranches}</td>
                      <td className="text-center">{plan.maxUsers}</td>
                      <td className="text-center">{plan.trialDays > 0 ? `${plan.trialDays} días` : 'Sin prueba'}</td>
                      <td className="text-center">{plan.tenantsCount}</td>
                      <td className="text-end">
                        <button type="button" className="btn btn-sm btn-light" onClick={() => { setForm(toForm(plan)); setError(''); }}
                          aria-label={`Editar ${plan.name}`}>
                          Editar
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

      <Modal show={form !== null} onHide={() => setForm(null)} size="lg" centered>
        {form && (
          <form onSubmit={(event) => void save(event)}>
            <Modal.Header closeButton>
              <Modal.Title>{form.id ? `Editar ${form.name}` : 'Nuevo plan'}</Modal.Title>
            </Modal.Header>
            <Modal.Body>
              <div className="row g-2">
                <div className="col-md-3">
                  <label className="form-label" htmlFor="plan-code">Clave</label>
                  <input id="plan-code" className="form-control text-uppercase" maxLength={30} required value={form.code}
                    onChange={(event) => setForm({ ...form, code: event.target.value })} />
                </div>
                <div className="col-md-5">
                  <label className="form-label" htmlFor="plan-name">Nombre</label>
                  <input id="plan-name" className="form-control" maxLength={80} required value={form.name}
                    onChange={(event) => setForm({ ...form, name: event.target.value })} />
                </div>
                <div className="col-md-4">
                  <label className="form-label" htmlFor="plan-price">Precio mensual (MXN)</label>
                  <div className="input-group">
                    <span className="input-group-text">$</span>
                    <input id="plan-price" type="number" min={0} step="0.01" className="form-control ta-money" required value={form.monthlyPrice}
                      onChange={(event) => setForm({ ...form, monthlyPrice: event.target.value })} />
                  </div>
                </div>
                <div className="col-12">
                  <label className="form-label" htmlFor="plan-description">Descripción (opcional)</label>
                  <input id="plan-description" className="form-control" maxLength={500} value={form.description}
                    onChange={(event) => setForm({ ...form, description: event.target.value })} />
                </div>
                <div className="col-12">
                  <span className="form-label d-block">Módulos que el negocio puede encender</span>
                  <div className="d-flex flex-wrap gap-3 border rounded p-2">
                    {MODULE_OPTIONS.map((option) => (
                      <div key={option.value} className="form-check mb-0">
                        <input id={`plan-module-${option.value}`} type="checkbox" className="form-check-input"
                          checked={(form.modules & option.value) === option.value}
                          onChange={(event) => setForm({
                            ...form,
                            modules: event.target.checked ? form.modules | option.value : form.modules & ~option.value,
                          })} />
                        <label className="form-check-label" htmlFor={`plan-module-${option.value}`}>{option.label}</label>
                      </div>
                    ))}
                  </div>
                </div>
                <div className="col-6 col-md-3">
                  <label className="form-label" htmlFor="plan-branches">Sucursales</label>
                  <input id="plan-branches" type="number" min={1} max={50} className="form-control" value={form.maxBranches}
                    onChange={(event) => setForm({ ...form, maxBranches: event.target.value })} />
                </div>
                <div className="col-6 col-md-3">
                  <label className="form-label" htmlFor="plan-users">Usuarios</label>
                  <input id="plan-users" type="number" min={1} max={500} className="form-control" value={form.maxUsers}
                    onChange={(event) => setForm({ ...form, maxUsers: event.target.value })} />
                </div>
                <div className="col-6 col-md-3">
                  <label className="form-label" htmlFor="plan-trial">Prueba gratis (días)</label>
                  <input id="plan-trial" type="number" min={0} max={90} className="form-control" value={form.trialDays}
                    onChange={(event) => setForm({ ...form, trialDays: event.target.value })} />
                </div>
                <div className="col-6 col-md-3">
                  <label className="form-label" htmlFor="plan-order">Orden</label>
                  <input id="plan-order" type="number" className="form-control" value={form.sortOrder}
                    onChange={(event) => setForm({ ...form, sortOrder: event.target.value })} />
                </div>
                <div className="col-md-6">
                  <div className="form-check form-switch">
                    <input id="plan-default" type="checkbox" role="switch" className="form-check-input" checked={form.isDefaultForNewBusinesses}
                      onChange={(event) => setForm({ ...form, isDefaultForNewBusinesses: event.target.checked })} />
                    <label className="form-check-label" htmlFor="plan-default">Plan para negocios nuevos (con su prueba gratis)</label>
                  </div>
                </div>
                {form.id && (
                  <div className="col-md-6">
                    <div className="form-check form-switch">
                      <input id="plan-active" type="checkbox" role="switch" className="form-check-input" checked={form.isActive}
                        onChange={(event) => setForm({ ...form, isActive: event.target.checked })} />
                      <label className="form-check-label" htmlFor="plan-active">Activo (se ofrece a los negocios)</label>
                    </div>
                  </div>
                )}
              </div>
              {error && <div className="alert alert-danger mt-3 mb-0">{error}</div>}
            </Modal.Body>
            <Modal.Footer>
              <button type="button" className="btn btn-light" onClick={() => setForm(null)}>Cancelar</button>
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? 'Guardando…' : 'Guardar plan'}</button>
            </Modal.Footer>
          </form>
        )}
      </Modal>
    </>
  );
}
