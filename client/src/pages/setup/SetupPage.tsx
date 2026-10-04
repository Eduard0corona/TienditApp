import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { tenantApi } from '../../api/tenantApi';
import { getApiErrorMessage } from '../../api/errors';
import SalesPolicyFields from '../../components/settings/SalesPolicyFields';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import type { StarterCatalogDto, TenantSalesPolicyDto, TenantSetupDto, TenantSetupStepKey } from '../../types';
import { TenantCapability } from '../../types';
import { BUSINESS_TYPE_LABELS } from '../../utils/labels';
import './SetupPage.css';

const STEPS = ['Tu negocio', 'Precios e IVA', 'Catálogo', '¡Listo!'] as const;

const MODULES = [
  { value: TenantCapability.Retail, label: 'Venta de productos', help: 'Vender artículos en mostrador.' },
  { value: TenantCapability.Food, label: 'Alimentos', help: 'Menú, mesas y pedidos de comida.' },
  { value: TenantCapability.Kitchen, label: 'Pantalla de cocina', help: 'Comandas en vivo para preparar.' },
  { value: TenantCapability.Service, label: 'Servicios', help: 'Recibir piezas, darles seguimiento y entregarlas.' },
  { value: TenantCapability.Appointments, label: 'Agenda', help: 'Citas con clientes.' },
  { value: TenantCapability.Inventory, label: 'Inventario', help: 'Insumos, existencias y compras.' },
];

const NEXT_STEPS: Array<{ key: TenantSetupStepKey; label: string; path: string; action: string }> = [
  { key: 'cash', label: 'Abre la caja con el efectivo inicial', path: '/cash', action: 'Abrir caja' },
  { key: 'firstSale', label: 'Haz tu primera venta', path: '/sell', action: 'Vender' },
  { key: 'team', label: 'Invita a tu equipo', path: '/employees', action: 'Invitar' },
  { key: 'logo', label: 'Sube el logotipo de tu negocio', path: '/settings/business', action: 'Subir logotipo' },
];

/**
 * Guided setup for a new business: modules, IVA policy, a sample catalog by line of business and the
 * first steps. Every step can be skipped; finishing (or skipping) it is remembered by the server.
 */
export default function SetupPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const { tenant, refreshTenant } = useTenant();
  const [step, setStep] = useState(0);
  const [setup, setSetup] = useState<TenantSetupDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [capabilities, setCapabilities] = useState(0);
  const [policy, setPolicy] = useState<TenantSalesPolicyDto>(() => ({
    pricesIncludeTax: tenant?.pricesIncludeTax ?? true,
    defaultTaxRate: tenant?.defaultTaxRate ?? 16,
    autoCloseCounterSales: tenant?.autoCloseCounterSales ?? true,
  }));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [installingId, setInstallingId] = useState('');

  const loadSetup = useCallback(async () => {
    try {
      const { data } = await tenantApi.getSetup();
      setSetup(data);
      setCapabilities(data.capabilities);
      setPolicy((current) => ({ ...current, pricesIncludeTax: data.pricesIncludeTax, defaultTaxRate: data.defaultTaxRate }));
      setLoadError('');
    } catch (err: unknown) {
      setLoadError(getApiErrorMessage(err, 'No se pudo cargar el arranque guiado.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadSetup();
  }, [loadSetup]);

  const goTo = (next: number) => {
    setError('');
    setStep(next);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const saveModules = async () => {
    if (!tenant) return;
    if ((capabilities & (TenantCapability.Retail | TenantCapability.Food | TenantCapability.Service)) === 0) {
      setError('Elige al menos venta de productos, alimentos o servicios.');
      return;
    }
    setSaving(true);
    setError('');
    try {
      if (capabilities !== setup?.capabilities) {
        await tenantApi.updateMyTenant({
          name: tenant.name,
          contactEmail: tenant.contactEmail,
          contactPhone: tenant.contactPhone,
          capabilities,
          rfc: tenant.rfc,
          legalName: tenant.legalName,
          industry: tenant.industry,
          taxRegime: tenant.taxRegime,
          street: tenant.street,
          exteriorNumber: tenant.exteriorNumber,
          interiorNumber: tenant.interiorNumber,
          neighborhood: tenant.neighborhood,
          zipCode: tenant.zipCode,
          city: tenant.city,
          state: tenant.state,
        });
        await Promise.all([refreshTenant(), loadSetup()]);
      }
      goTo(1);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron guardar los módulos.'));
    } finally {
      setSaving(false);
    }
  };

  const savePolicy = async () => {
    setSaving(true);
    setError('');
    try {
      await tenantApi.updateSalesPolicy(policy);
      await refreshTenant();
      goTo(2);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar la política de IVA.'));
    } finally {
      setSaving(false);
    }
  };

  const installCatalog = async (catalog: StarterCatalogDto) => {
    setInstallingId(catalog.id);
    setError('');
    try {
      const { data } = await tenantApi.installStarterCatalog(catalog.id);
      toast.success(`Listo: ${data.productsCreated} productos de ejemplo de ${data.templateName}. Edita los precios cuando quieras.`);
      await loadSetup();
      goTo(3);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo instalar la plantilla.'));
    } finally {
      setInstallingId('');
    }
  };

  const finish = async (path: string) => {
    setSaving(true);
    setError('');
    try {
      await tenantApi.completeSetup();
      await refreshTenant();
      navigate(path);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo terminar el arranque guiado.'));
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="text-center py-5">
        <div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando…</span></div>
      </div>
    );
  }

  if (loadError || !setup) {
    return (
      <div className="alert alert-danger mt-3 d-flex justify-content-between align-items-center gap-2">
        <span>{loadError || 'No se pudo cargar el arranque guiado.'}</span>
        <button type="button" className="btn btn-sm btn-light" onClick={() => void loadSetup()}>Reintentar</button>
      </div>
    );
  }

  const ownCatalogs = setup.starterCatalogs.filter((catalog) => catalog.businessType === setup.businessType);
  const otherCatalogs = setup.starterCatalogs.filter((catalog) => catalog.businessType !== setup.businessType);
  const stepDone = (key: TenantSetupStepKey) => setup.steps.find((item) => item.key === key)?.done ?? false;

  const renderCatalog = (catalog: StarterCatalogDto) => (
    <div className="col-md-6 col-xl-4" key={catalog.id}>
      <div className="card h-100 setup-catalog">
        <div className="card-body d-flex flex-column">
          <h3 className="h5 mb-1">{catalog.name}</h3>
          <p className="text-muted small mb-2">{catalog.description}</p>
          <p className="small mb-3">
            {catalog.productCount} productos en {catalog.categories.join(', ')}.
          </p>
          <button
            type="button"
            className="btn btn-outline-primary btn-touch mt-auto"
            onClick={() => void installCatalog(catalog)}
            disabled={!!installingId}
          >
            {installingId === catalog.id ? 'Instalando…' : 'Usar esta plantilla'}
          </button>
        </div>
      </div>
    </div>
  );

  return (
    <div className="setup-page">
      <div className="d-flex flex-wrap align-items-start justify-content-between gap-2 mt-3 mb-3">
        <div>
          <h1 className="h3 mb-1">Prepara {tenant?.name ?? 'tu negocio'}</h1>
          <p className="text-muted mb-0">Cuatro pasos para llegar a tu primera venta. Puedes cambiar todo después.</p>
        </div>
        <button type="button" className="btn btn-link" onClick={() => void finish('/')} disabled={saving}>
          Omitir por ahora
        </button>
      </div>

      <ol className="setup-stepper" aria-label="Pasos">
        {STEPS.map((label, index) => (
          <li key={label} className={index === step ? 'current' : index < step ? 'done' : ''} aria-current={index === step ? 'step' : undefined}>
            <span className="setup-step-number">{index < step ? <i className="ri-check-line" aria-hidden="true"></i> : index + 1}</span>
            <span className="setup-step-label">{label}</span>
          </li>
        ))}
      </ol>

      {error && <div className="alert alert-danger">{error}</div>}

      {step === 0 && (
        <div className="card">
          <div className="card-body">
            <h2 className="h5">¿Qué vas a usar?</h2>
            <p className="text-muted">
              Tu giro es <strong>{BUSINESS_TYPE_LABELS[setup.businessType] ?? 'Comercio'}</strong>. Activa solo lo que necesitas;
              el menú se ajusta a lo que elijas.
            </p>
            <div className="row g-2">
              {MODULES.map((module) => {
                const checked = (capabilities & module.value) === module.value;
                return (
                  <div className="col-md-6" key={module.value}>
                    <label className={`policy-option ${checked ? 'selected' : ''}`} htmlFor={`setup-module-${module.value}`}>
                      <input
                        id={`setup-module-${module.value}`}
                        type="checkbox"
                        className="form-check-input"
                        checked={checked}
                        onChange={(event) => setCapabilities((current) => (event.target.checked
                          ? current | module.value
                          : current & ~module.value))}
                        disabled={saving}
                      />
                      <span>
                        <strong>{module.label}</strong>
                        <span className="d-block small text-muted">{module.help}</span>
                      </span>
                    </label>
                  </div>
                );
              })}
            </div>
            <div className="d-flex justify-content-end mt-3">
              <button type="button" className="btn btn-primary btn-touch" onClick={() => void saveModules()} disabled={saving}>
                {saving ? 'Guardando…' : 'Continuar'}
              </button>
            </div>
          </div>
        </div>
      )}

      {step === 1 && (
        <div className="card">
          <div className="card-body">
            <h2 className="h5">Precios e IVA</h2>
            <p className="text-muted">El sistema calcula el IVA de cada venta con esta regla y lo muestra en el ticket.</p>
            <SalesPolicyFields value={policy} onChange={setPolicy} disabled={saving} idPrefix="setup-policy" />
            <div className="d-flex justify-content-between mt-3">
              <button type="button" className="btn btn-light btn-touch" onClick={() => goTo(0)} disabled={saving}>Atrás</button>
              <button type="button" className="btn btn-primary btn-touch" onClick={() => void savePolicy()} disabled={saving}>
                {saving ? 'Guardando…' : 'Continuar'}
              </button>
            </div>
          </div>
        </div>
      )}

      {step === 2 && (
        <div>
          {setup.hasProducts ? (
            <div className="card">
              <div className="card-body">
                <h2 className="h5">Tu catálogo ya tiene productos</h2>
                <p className="text-muted mb-3">Puedes revisarlos y ajustar precios o tasas de IVA desde Catálogo.</p>
                <Link to="/products" className="btn btn-light btn-touch me-2">Ver productos</Link>
                <button type="button" className="btn btn-primary btn-touch" onClick={() => goTo(3)}>Continuar</button>
              </div>
            </div>
          ) : (
            <>
              <h2 className="h5">Empieza con un catálogo de ejemplo</h2>
              <p className="text-muted">
                Crea categorías y productos con precios de ejemplo para vender hoy mismo. Después edita precios y nombres.
              </p>
              <div className="row g-3">{(ownCatalogs.length > 0 ? ownCatalogs : setup.starterCatalogs).map(renderCatalog)}</div>
              {ownCatalogs.length > 0 && otherCatalogs.length > 0 && (
                <details className="mt-3">
                  <summary className="fw-semibold">Plantillas de otros giros</summary>
                  <div className="row g-3 mt-1">{otherCatalogs.map(renderCatalog)}</div>
                </details>
              )}
              <div className="d-flex justify-content-between mt-3">
                <button type="button" className="btn btn-light btn-touch" onClick={() => goTo(1)}>Atrás</button>
                <button type="button" className="btn btn-link" onClick={() => goTo(3)}>Prefiero capturar mis productos</button>
              </div>
            </>
          )}
        </div>
      )}

      {step === 3 && (
        <div className="card">
          <div className="card-body">
            <h2 className="h5">¡Tu negocio está listo para vender!</h2>
            <p className="text-muted">Estos pasos completan tu configuración; también los verás en Inicio.</p>
            <ul className="list-unstyled setup-checklist">
              <li className={stepDone('catalog') ? 'done' : ''}>
                <i className={stepDone('catalog') ? 'ri-checkbox-circle-fill' : 'ri-checkbox-blank-circle-line'} aria-hidden="true"></i>
                <span className="flex-grow-1">Agrega tus productos</span>
                {!stepDone('catalog') && <Link to="/products/new" className="btn btn-sm btn-light">Agregar</Link>}
              </li>
              {NEXT_STEPS.map((item) => (
                <li key={item.key} className={stepDone(item.key) ? 'done' : ''}>
                  <i className={stepDone(item.key) ? 'ri-checkbox-circle-fill' : 'ri-checkbox-blank-circle-line'} aria-hidden="true"></i>
                  <span className="flex-grow-1">{item.label}</span>
                  {!stepDone(item.key) && (
                    <button type="button" className="btn btn-sm btn-light" onClick={() => void finish(item.path)} disabled={saving}>
                      {item.action}
                    </button>
                  )}
                </li>
              ))}
            </ul>
            <div className="d-flex flex-wrap justify-content-between gap-2 mt-3">
              <button type="button" className="btn btn-light btn-touch" onClick={() => goTo(2)} disabled={saving}>Atrás</button>
              <div className="d-flex gap-2">
                <button type="button" className="btn btn-light btn-touch" onClick={() => void finish('/dashboard')} disabled={saving}>
                  Ir a Inicio
                </button>
                <button type="button" className="btn btn-primary btn-touch" onClick={() => void finish('/sell')} disabled={saving}>
                  Hacer mi primera venta
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
