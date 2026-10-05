import { useState, useEffect, useRef } from 'react';
import { useForm } from 'react-hook-form';
import PageTitle from '../../components/common/PageTitle';
import UploadLoadingModal from '../../components/common/UploadLoadingModal';
import SalesPolicyCard from '../../components/settings/SalesPolicyCard';
import OnlineMenuCard from '../../components/settings/OnlineMenuCard';
import SubscriptionCard from '../../components/settings/SubscriptionCard';
import { useMySubscription } from '../../hooks/useMySubscription';
import { tenantApi } from '../../api/tenantApi';
import { TenantCapability } from '../../types';
import { useTenant } from '../../hooks/useTenant';
import type { UpdateTenantRequest, TenantProfileDto } from '../../types';
import { BUSINESS_TYPE_LABELS } from '../../utils/labels';
import { getApiErrorMessage } from '../../api/errors';


const INDUSTRY_OPTIONS = [
  'Alimentos y Bebidas',
  'Abarrotes',
  'Farmacia',
  'Ropa y Accesorios',
  'Electrónica',
  'Ferretería',
  'Papelería',
  'Estética y Spa',
  'Taller Mecánico',
  'Consultorio',
  'Servicios Profesionales',
  'Educación',
  'Gimnasio',
  'Lavandería',
  'Otro',
];

const TAX_REGIME_OPTIONS = [
  '601 - General de Ley Personas Morales',
  '603 - Personas Morales con Fines no Lucrativos',
  '605 - Sueldos y Salarios',
  '606 - Arrendamiento',
  '612 - Personas Físicas con Actividades Empresariales y Profesionales',
  '616 - Sin obligaciones fiscales',
  '621 - Incorporación Fiscal',
  '625 - Actividades Empresariales con Plataformas Tecnológicas',
  '626 - Régimen Simplificado de Confianza (RESICO)',
];

const MX_STATES = [
  'Aguascalientes', 'Baja California', 'Baja California Sur', 'Campeche', 'Chiapas',
  'Chihuahua', 'Ciudad de México', 'Coahuila', 'Colima', 'Durango', 'Estado de México',
  'Guanajuato', 'Guerrero', 'Hidalgo', 'Jalisco', 'Michoacán', 'Morelos', 'Nayarit',
  'Nuevo León', 'Oaxaca', 'Puebla', 'Querétaro', 'Quintana Roo', 'San Luis Potosí',
  'Sinaloa', 'Sonora', 'Tabasco', 'Tamaulipas', 'Tlaxcala', 'Veracruz', 'Yucatán', 'Zacatecas',
];

const CAPABILITY_OPTIONS = [
  { value: TenantCapability.Retail, label: 'Productos retail' },
  { value: TenantCapability.Service, label: 'Servicios' },
  { value: TenantCapability.Food, label: 'Alimentos / cocina' },
  { value: TenantCapability.Inventory, label: 'Inventario / insumos' },
  { value: TenantCapability.Appointments, label: 'Citas / agenda' },
  { value: TenantCapability.Kitchen, label: 'Pantalla de cocina' },
];

const CAPABILITY_PRESETS = [
  {
    id: 'laundry',
    label: 'Lavanderia',
    industry: 'Lavanderia',
    capabilities:
      TenantCapability.Service
      | TenantCapability.Retail
      | TenantCapability.Inventory
      | TenantCapability.Appointments,
  },
  {
    id: 'food',
    label: 'Alimentos',
    industry: 'Alimentos y Bebidas',
    capabilities:
      TenantCapability.Food
      | TenantCapability.Retail
      | TenantCapability.Inventory
      | TenantCapability.Kitchen,
  },
  {
    id: 'barber',
    label: 'Barberia',
    industry: 'Estetica y Spa',
    capabilities:
      TenantCapability.Service
      | TenantCapability.Retail
      | TenantCapability.Inventory
      | TenantCapability.Appointments,
  },
];

export default function BusinessSettings() {
  const { refreshTenant } = useTenant();
  const [capabilities, setCapabilities] = useState<number>(0);
  const subscription = useMySubscription();
  // Modules the plan lets the business turn on; null without plan (no limits).
  const planModules = subscription.data?.hasPlan && subscription.data.plan ? subscription.data.plan.modules : null;
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [loading, setLoading] = useState(true);
  const [editMode, setEditMode] = useState(false);
  const [msg, setMsg] = useState<{ type: string; text: string } | null>(null);
  const [logoMsg, setLogoMsg] = useState<{ type: string; text: string } | null>(null);
  const [logoLoading, setLogoLoading] = useState(false);
  const [logoUploading, setLogoUploading] = useState(false);
  const [tenant, setTenant] = useState<TenantProfileDto | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<UpdateTenantRequest>();

  const resetForm = (data: TenantProfileDto) => {
    reset({
      name: data.name,
      contactEmail: data.contactEmail,
      contactPhone: data.contactPhone || '',
      capabilities: data.capabilities,
      rfc: data.rfc || '',
      legalName: data.legalName || '',
      industry: data.industry || '',
      taxRegime: data.taxRegime || '',
      street: data.street || '',
      exteriorNumber: data.exteriorNumber || '',
      interiorNumber: data.interiorNumber || '',
      neighborhood: data.neighborhood || '',
      zipCode: data.zipCode || '',
      city: data.city || '',
      state: data.state || '',
    });
  };

  useEffect(() => {
    const loadTenant = async () => {
      try {
        const { data } = await tenantApi.getMyTenant();
        setTenant(data);
        setCapabilities(data.capabilities ?? 0);
        resetForm(data);
      } catch {
        setMsg({ type: 'danger', text: 'Error al cargar los datos de la empresa.' });
      } finally {
        setLoading(false);
      }
    };
    loadTenant();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onSubmit = async (data: UpdateTenantRequest) => {
    setMsg(null);
    try {
      const payload: UpdateTenantRequest = {
        ...data,
        capabilities,
      };

      const { data: updated } = await tenantApi.updateMyTenant(payload);
      setTenant(updated);
      setCapabilities(updated.capabilities ?? capabilities);
      await refreshTenant();
      setEditMode(false);
      setMsg({ type: 'success', text: 'Datos de la empresa actualizados correctamente.' });
    } catch (err: unknown) {
      const errMsg = getApiErrorMessage(err, 'Error al actualizar los datos de la empresa.');
      setMsg({ type: 'danger', text: errMsg });
    }
  };

  const onUploadLogo = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setLogoLoading(true);
    setLogoUploading(true);
    setLogoMsg(null);
    try {
      const { data: logos } = await tenantApi.uploadLogo(file);
      setTenant((prev) => prev ? { ...prev, logoUrl: logos.medium, logoImages: logos } : prev);
      await refreshTenant();
      setLogoMsg({ type: 'success', text: 'Logo actualizado.' });
    } catch (err: unknown) {
      const errMsg = getApiErrorMessage(err, 'Error al subir el logo.');
      setLogoMsg({ type: 'danger', text: errMsg });
    } finally {
      setLogoLoading(false);
      setLogoUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  };

  const onDeleteLogo = async () => {
    setLogoLoading(true);
    setLogoMsg(null);
    try {
      await tenantApi.deleteLogo();
      setTenant((prev) => prev ? { ...prev, logoUrl: undefined, logoImages: undefined } : prev);
      await refreshTenant();
      setLogoMsg({ type: 'success', text: 'Logo eliminado.' });
    } catch {
      setLogoMsg({ type: 'danger', text: 'Error al eliminar el logo.' });
    } finally {
      setLogoLoading(false);
    }
  };

  const logoSrc = tenant?.logoImages?.medium || tenant?.logoUrl || '/images/placeholders/store.svg';

  const formatAddress = () => {
    const parts = [
      tenant?.street,
      tenant?.exteriorNumber ? `#${tenant.exteriorNumber}` : null,
      tenant?.interiorNumber ? `Int. ${tenant.interiorNumber}` : null,
    ].filter(Boolean).join(' ');
    const parts2 = [
      tenant?.neighborhood,
      tenant?.zipCode ? `C.P. ${tenant.zipCode}` : null,
    ].filter(Boolean).join(', ');
    const parts3 = [tenant?.city, tenant?.state].filter(Boolean).join(', ');
    return [parts, parts2, parts3].filter(Boolean).join(', ');
  };

  if (loading) {
    return (
      <>
        <PageTitle title="Mi negocio" subtitle="Configuración" />
        <div className="text-center py-5">
          <div className="spinner-border text-primary" role="status">
            <span className="visually-hidden">Cargando...</span>
          </div>
        </div>
      </>
    );
  }

  return (
    <>
      <UploadLoadingModal
        show={logoLoading || logoUploading}
        title="Subiendo logo"
        description="Estamos actualizando el logo de tu empresa."
      />
      <PageTitle title="Mi negocio" subtitle="Configuración" />

      {/* Business Header */}
      <div className="row">
        <div className="col-sm-12">
          <div className="card bg-primary">
            <div className="card-body profile-user-box">
              <div className="row">
                <div className="col-sm-8">
                  <div className="row align-items-center">
                    <div className="col-auto">
                      <div className="avatar-lg">
                        <img
                          src={logoSrc}
                          alt="logo"
                          className="rounded-circle img-thumbnail"
                          style={{ objectFit: 'cover', width: 64, height: 64 }}
                        />
                      </div>
                    </div>
                    <div className="col">
                      <div>
                        <h4 className="mt-1 mb-1 text-white">{tenant?.name}</h4>
                        <p className="font-13 text-white-50">
                          {BUSINESS_TYPE_LABELS[tenant?.businessType ?? 0]} &middot; {tenant?.industry || 'Sin giro'}
                        </p>
                        <ul className="mb-0 list-inline text-light">
                          <li className="list-inline-item me-3">
                            <h5 className="mb-1 text-white">{tenant?.contactEmail}</h5>
                            <p className="mb-0 font-13 text-white-50">Email</p>
                          </li>
                          {tenant?.contactPhone && (
                            <li className="list-inline-item">
                              <h5 className="mb-1 text-white">{tenant.contactPhone}</h5>
                              <p className="mb-0 font-13 text-white-50">Teléfono</p>
                            </li>
                          )}
                        </ul>
                      </div>
                    </div>
                  </div>
                </div>
                <div className="col-sm-4">
                  <div className="text-center mt-sm-0 mt-3 text-sm-end">
                    <button
                      type="button"
                      className="btn btn-light"
                      onClick={() => {
                        if (editMode && tenant) {
                          resetForm(tenant);
                          setCapabilities(tenant.capabilities ?? 0);
                        }
                        setEditMode(!editMode);
                      }}
                    >
                      <i className="ri-store-2-line me-1" aria-hidden="true"></i>
                      {editMode ? 'Cancelar' : 'Editar Empresa'}
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      {msg && (
        <div className={`alert alert-${msg.type} alert-dismissible fade show`}>
          {msg.text}
          <button type="button" className="btn-close" onClick={() => setMsg(null)} />
        </div>
      )}

      <SubscriptionCard />
      <SalesPolicyCard />
      <OnlineMenuCard />

      <div className="row">
        {/* Left Column */}
        <div className="col-xl-4">
          {/* Business Info Card */}
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">Información de la Empresa</h4>

              {editMode ? (
                <form id="business-form" onSubmit={handleSubmit(onSubmit)}>
                  <div className="mb-3">
                    <label className="form-label">
                      Nombre Comercial <span className="text-danger">*</span>
                    </label>
                    <input
                      className={`form-control ${errors.name ? 'is-invalid' : ''}`}
                      type="text"
                      {...register('name', { required: 'El nombre comercial es requerido' })}
                    />
                    {errors.name && (
                      <div className="invalid-feedback">{errors.name.message}</div>
                    )}
                  </div>
                  <div className="mb-3">
                    <label className="form-label">
                      Email de Contacto <span className="text-danger">*</span>
                    </label>
                    <input
                      className={`form-control ${errors.contactEmail ? 'is-invalid' : ''}`}
                      type="email"
                      {...register('contactEmail', { required: 'El email es requerido' })}
                    />
                    {errors.contactEmail && (
                      <div className="invalid-feedback">{errors.contactEmail.message}</div>
                    )}
                  </div>
                  <div className="mb-3">
                    <label className="form-label">Teléfono de Contacto</label>
                    <input className="form-control" type="tel" {...register('contactPhone')} />
                  </div>
                  <div className="mb-3">
                    <label className="form-label">Giro del Negocio</label>
                    <select className="form-select" {...register('industry')}>
                      <option value="">Seleccionar...</option>
                      {INDUSTRY_OPTIONS.map((opt) => (
                        <option key={opt} value={opt}>{opt}</option>
                      ))}
                    </select>
                  </div>
                  <div className="mb-3">
                    <label className="form-label d-block">Modulos habilitados</label>
                    <div className="d-flex flex-wrap gap-2 mb-2">
                      {CAPABILITY_PRESETS.map((preset) => (
                        <button
                          key={preset.id}
                          type="button"
                          className="btn btn-soft-secondary btn-sm"
                          onClick={() => {
                            setCapabilities(planModules === null ? preset.capabilities : preset.capabilities & planModules);
                            setValue('industry', preset.industry);
                          }}
                        >
                          Preset: {preset.label}
                        </button>
                      ))}
                    </div>
                    <div className="border rounded p-2">
                      {CAPABILITY_OPTIONS.map((option) => {
                        const outsidePlan = planModules !== null && (planModules & option.value) !== option.value;
                        return (
                          <div key={option.value} className="form-check mb-1">
                            <input
                              className="form-check-input"
                              type="checkbox"
                              id={`capability-${option.value}`}
                              checked={(capabilities & option.value) === option.value}
                              disabled={outsidePlan}
                              onChange={(e) => {
                                setCapabilities((current) =>
                                  e.target.checked ? current | option.value : current & ~option.value,
                                );
                              }}
                            />
                            <label className="form-check-label" htmlFor={`capability-${option.value}`}>
                              {option.label}
                              {outsidePlan && <span className="text-muted small ms-1">(no incluido en tu plan)</span>}
                            </label>
                          </div>
                        );
                      })}
                    </div>
                  </div>
                  <button className="btn btn-primary" type="submit" disabled={isSubmitting}>
                    {isSubmitting ? 'Guardando...' : 'Guardar Cambios'}
                  </button>
                </form>
              ) : (
                <div className="text-start">
                  <p className="text-muted">
                    <strong>Nombre Comercial:</strong>{' '}
                    <span className="ms-2">{tenant?.name}</span>
                  </p>
                  <p className="text-muted">
                    <strong>Email:</strong>{' '}
                    <span className="ms-2">{tenant?.contactEmail}</span>
                  </p>
                  <p className="text-muted">
                    <strong>Teléfono:</strong>{' '}
                    <span className="ms-2">{tenant?.contactPhone || 'No registrado'}</span>
                  </p>
                  <p className="text-muted">
                    <strong>Tipo:</strong>{' '}
                    <span className="ms-2">{BUSINESS_TYPE_LABELS[tenant?.businessType ?? 0]}</span>
                  </p>
                  <p className="text-muted">
                    <strong>Giro:</strong>{' '}
                    <span className="ms-2">{tenant?.industry || 'No especificado'}</span>
                  </p>
                  <p className="text-muted">
                    <strong>Modulos:</strong>{' '}
                    <span className="ms-2">
                      {CAPABILITY_OPTIONS
                        .filter((option) => ((tenant?.capabilities ?? 0) & option.value) === option.value)
                        .map((option) => option.label)
                        .join(', ') || 'Sin modulos'}
                    </span>
                  </p>
                  <p className="text-muted mb-0">
                    <strong>Miembro desde:</strong>{' '}
                    <span className="ms-2">
                      {tenant?.createdAt
                        ? new Date(tenant.createdAt).toLocaleDateString('es-MX', {
                            year: 'numeric',
                            month: 'long',
                            day: 'numeric',
                          })
                        : ''}
                    </span>
                  </p>
                </div>
              )}
            </div>
          </div>

          {/* Logo Section */}
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">Logo</h4>

              {logoMsg && (
                <div className={`alert alert-${logoMsg.type}`}>{logoMsg.text}</div>
              )}

              <div className="text-center mb-3">
                <img
                  src={logoSrc}
                  alt="logo"
                  className="rounded img-thumbnail"
                  width="120"
                  height="120"
                  style={{ objectFit: 'cover' }}
                />
              </div>

              <div className="d-flex gap-2 justify-content-center">
                <input
                  type="file"
                  ref={fileInputRef}
                  className="d-none"
                  accept="image/*"
                  onChange={onUploadLogo}
                />
                <button
                  className="btn btn-primary btn-sm"
                  onClick={() => fileInputRef.current?.click()}
                  disabled={logoLoading}
                >
                  <i className="ri-upload-2-line me-1" aria-hidden="true"></i>
                  Subir Logo
                </button>
                {tenant?.logoUrl && (
                  <button
                    className="btn btn-danger btn-sm"
                    onClick={onDeleteLogo}
                    disabled={logoLoading}
                  >
                    <i className="ri-delete-bin-line me-1" aria-hidden="true"></i>
                    Eliminar
                  </button>
                )}
              </div>
            </div>
          </div>
        </div>

        {/* Right Column */}
        <div className="col-xl-8">
          {/* Fiscal Data */}
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">
                <i className="ri-file-list-3-line me-1"></i>
                Datos Fiscales
              </h4>

              {editMode ? (
                <>
                  <div className="row">
                    <div className="col-md-6">
                      <div className="mb-3">
                        <label className="form-label">RFC</label>
                        <input
                          className="form-control"
                          type="text"
                          placeholder="XAXX010101000"
                          maxLength={13}
                          style={{ textTransform: 'uppercase' }}
                          form="business-form"
                          {...register('rfc')}
                        />
                      </div>
                    </div>
                    <div className="col-md-6">
                      <div className="mb-3">
                        <label className="form-label">Razón Social</label>
                        <input
                          className="form-control"
                          type="text"
                          form="business-form"
                          {...register('legalName')}
                        />
                      </div>
                    </div>
                  </div>
                  <div className="mb-3">
                    <label className="form-label">Régimen Fiscal</label>
                    <select className="form-select" form="business-form" {...register('taxRegime')}>
                      <option value="">Seleccionar...</option>
                      {TAX_REGIME_OPTIONS.map((opt) => (
                        <option key={opt} value={opt}>{opt}</option>
                      ))}
                    </select>
                  </div>
                </>
              ) : (
                <div className="text-start">
                  <p className="text-muted">
                    <strong>RFC:</strong>{' '}
                    <span className="ms-2">{tenant?.rfc || 'No registrado'}</span>
                  </p>
                  <p className="text-muted">
                    <strong>Razón Social:</strong>{' '}
                    <span className="ms-2">{tenant?.legalName || 'No registrada'}</span>
                  </p>
                  <p className="text-muted mb-0">
                    <strong>Régimen Fiscal:</strong>{' '}
                    <span className="ms-2">{tenant?.taxRegime || 'No especificado'}</span>
                  </p>
                </div>
              )}
            </div>
          </div>

          {/* Address */}
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">
                <i className="ri-map-pin-line me-1"></i>
                Dirección Fiscal
              </h4>

              {editMode ? (
                <>
                  <div className="mb-3">
                    <label className="form-label">Calle</label>
                    <input
                      className="form-control"
                      type="text"
                      form="business-form"
                      {...register('street')}
                    />
                  </div>
                  <div className="row">
                    <div className="col-md-6">
                      <div className="mb-3">
                        <label className="form-label">Número Exterior</label>
                        <input
                          className="form-control"
                          type="text"
                          form="business-form"
                          {...register('exteriorNumber')}
                        />
                      </div>
                    </div>
                    <div className="col-md-6">
                      <div className="mb-3">
                        <label className="form-label">Número Interior</label>
                        <input
                          className="form-control"
                          type="text"
                          form="business-form"
                          {...register('interiorNumber')}
                        />
                      </div>
                    </div>
                  </div>
                  <div className="mb-3">
                    <label className="form-label">Colonia</label>
                    <input
                      className="form-control"
                      type="text"
                      form="business-form"
                      {...register('neighborhood')}
                    />
                  </div>
                  <div className="row">
                    <div className="col-md-4">
                      <div className="mb-3">
                        <label className="form-label">Código Postal</label>
                        <input
                          className="form-control"
                          type="text"
                          maxLength={5}
                          form="business-form"
                          {...register('zipCode')}
                        />
                      </div>
                    </div>
                    <div className="col-md-8">
                      <div className="mb-3">
                        <label className="form-label">Ciudad / Municipio</label>
                        <input
                          className="form-control"
                          type="text"
                          form="business-form"
                          {...register('city')}
                        />
                      </div>
                    </div>
                  </div>
                  <div className="mb-3">
                    <label className="form-label">Estado</label>
                    <select className="form-select" form="business-form" {...register('state')}>
                      <option value="">Seleccionar...</option>
                      {MX_STATES.map((s) => (
                        <option key={s} value={s}>{s}</option>
                      ))}
                    </select>
                  </div>
                </>
              ) : (
                <p className="text-muted mb-0">
                  {formatAddress() || 'No registrada'}
                </p>
              )}
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
