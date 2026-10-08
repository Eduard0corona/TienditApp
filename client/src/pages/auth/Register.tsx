import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, Link } from 'react-router-dom';
import { authApi } from '../../api/authApi';
import type { RegisterBusinessRequest, RegisterCustomerRequest } from '../../types';
import { BusinessType } from '../../types';
import './register.css';
import BrandLogo from '../../components/common/BrandLogo';
import { getApiErrorMessage } from '../../api/errors';
import { asButton } from '../../utils/a11y';
import { newPasswordPattern, PASSWORD_MIN_LENGTH } from '../../utils/passwordRules';

type AccountType = 'business' | 'customer' | null;

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

export default function Register() {
  const navigate = useNavigate();
  const [accountType, setAccountType] = useState<AccountType>(null);
  const [error, setError] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [showOptionalData, setShowOptionalData] = useState(false);

  // Business form
  const businessForm = useForm<RegisterBusinessRequest>({
    defaultValues: { businessType: BusinessType.Food },
  });

  // Customer form
  const customerForm = useForm<RegisterCustomerRequest>();

  // Signing up opens no session: the account is activated from the emailed link and then signs in. The email
  // travels in router state (never the URL) only to prefill the form that asks for a new link.
  const goToCheckEmail = (email: string) => navigate('/check-email', { state: { email } });

  const onSubmitBusiness = async (data: RegisterBusinessRequest) => {
    setError('');
    try {
      await authApi.registerBusiness(data);
      goToCheckEmail(data.email);
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al registrar la empresa.');
      setError(msg);
    }
  };

  const onSubmitCustomer = async (data: RegisterCustomerRequest) => {
    setError('');
    try {
      await authApi.registerCustomer(data);
      goToCheckEmail(data.email);
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al registrar la cuenta.');
      setError(msg);
    }
  };

  return (
    <div className="authentication-bg position-relative" style={{ minHeight: '100vh' }}>
      <div className="position-absolute start-0 end-0 start-0 bottom-0 w-100 h-100">
        <svg xmlns="http://www.w3.org/2000/svg" width="100%" height="100%" viewBox="0 0 800 800">
          <g fillOpacity="0.22">
            <circle style={{ fill: 'rgba(var(--ct-primary-rgb), 0.1)' }} cx="400" cy="400" r="600" />
            <circle style={{ fill: 'rgba(var(--ct-primary-rgb), 0.2)' }} cx="400" cy="400" r="500" />
            <circle style={{ fill: 'rgba(var(--ct-primary-rgb), 0.3)' }} cx="400" cy="400" r="300" />
            <circle style={{ fill: 'rgba(var(--ct-primary-rgb), 0.4)' }} cx="400" cy="400" r="200" />
            <circle style={{ fill: 'rgba(var(--ct-primary-rgb), 0.5)' }} cx="400" cy="400" r="100" />
          </g>
        </svg>
      </div>

      <div className="account-pages pt-2 pt-sm-5 pb-4 pb-sm-5 position-relative">
        <div className="container">
          <div className="row justify-content-center">
            <div className={accountType === 'business' ? 'col-xxl-8 col-lg-9' : 'col-xxl-5 col-lg-6'}>
              <div className="card">
                {/* Logo */}
                <div className="card-header py-4 text-center bg-primary">
                  <Link to="/">
                    <BrandLogo tone="light" />
                  </Link>
                </div>

                <div className="card-body p-4">
                  {/* Account type selection */}
                  {!accountType && (
                    <>
                      <div className="text-center w-75 m-auto">
                        <h4 className="text-dark-50 text-center pb-0 fw-bold">Crear Cuenta</h4>
                        <p className="text-muted mb-4">
                          Selecciona el tipo de cuenta que deseas crear.
                        </p>
                      </div>

                      <div className="row g-3">
                        <div className="col-6">
                          <div
                            className="card border border-primary shadow-none mb-0 h-100 register-account-card"
                            {...asButton(() => setAccountType('business'))}
                          >
                            <div className="card-body text-center py-4">
                              <i className="ri-store-2-line text-primary" style={{ fontSize: '2.5rem' }}></i>
                              <h5 className="mt-2 mb-1">Soy Empresa</h5>
                              <p className="text-muted mb-0 font-13">
                                Registro para negocios que ofrecen productos o servicios.
                              </p>
                            </div>
                          </div>
                        </div>
                        <div className="col-6">
                          <div
                            className="card border border-primary shadow-none mb-0 h-100 register-account-card"
                            {...asButton(() => setAccountType('customer'))}
                          >
                            <div className="card-body text-center py-4">
                              <i className="ri-user-line text-primary" style={{ fontSize: '2.5rem' }}></i>
                              <h5 className="mt-2 mb-1">Soy Cliente</h5>
                              <p className="text-muted mb-0 font-13">
                                Registro para personas que desean consumir productos o servicios.
                              </p>
                            </div>
                          </div>
                        </div>
                      </div>
                    </>
                  )}

                  {/* Customer Registration Form */}
                  {accountType === 'customer' && (
                    <>
                      <div className="text-center m-auto">
                        <h4 className="text-dark-50 pb-0 fw-bold">Registro de Cliente</h4>
                        <p className="text-muted mb-4">Ingresa tus datos para crear tu cuenta.</p>
                      </div>

                      {error && <div className="alert alert-danger">{error}</div>}

                      <form onSubmit={customerForm.handleSubmit(onSubmitCustomer)}>
                        <div className="row">
                          <div className="col-md-6 mb-3">
                            <label htmlFor="register-nombre" className="form-label">Nombre <span className="text-danger">*</span></label>
                            <input id="register-nombre"
                              type="text"
                              className={`form-control ${customerForm.formState.errors.firstName ? 'is-invalid' : ''}`}
                              placeholder="Tu nombre"
                              {...customerForm.register('firstName', { required: 'El nombre es requerido', maxLength: 100 })}
                            />
                            {customerForm.formState.errors.firstName && (
                              <div className="invalid-feedback">{customerForm.formState.errors.firstName.message}</div>
                            )}
                          </div>
                          <div className="col-md-6 mb-3">
                            <label htmlFor="register-apellido" className="form-label">Apellido <span className="text-danger">*</span></label>
                            <input id="register-apellido"
                              type="text"
                              className={`form-control ${customerForm.formState.errors.lastName ? 'is-invalid' : ''}`}
                              placeholder="Tu apellido"
                              {...customerForm.register('lastName', { required: 'El apellido es requerido', maxLength: 100 })}
                            />
                            {customerForm.formState.errors.lastName && (
                              <div className="invalid-feedback">{customerForm.formState.errors.lastName.message}</div>
                            )}
                          </div>
                        </div>

                        <div className="mb-3">
                          <label htmlFor="register-email" className="form-label">Email <span className="text-danger">*</span></label>
                          <input id="register-email"
                            type="email"
                            className={`form-control ${customerForm.formState.errors.email ? 'is-invalid' : ''}`}
                            placeholder="tu@email.com"
                            {...customerForm.register('email', { required: 'El email es requerido' })}
                          />
                          {customerForm.formState.errors.email && (
                            <div className="invalid-feedback">{customerForm.formState.errors.email.message}</div>
                          )}
                        </div>

                        <div className="mb-3">
                          <label htmlFor="register-telefono" className="form-label">Teléfono <span className="text-danger">*</span></label>
                          <input id="register-telefono"
                            type="tel"
                            className={`form-control ${customerForm.formState.errors.phoneNumber ? 'is-invalid' : ''}`}
                            placeholder="+5215512345678"
                            {...customerForm.register('phoneNumber', {
                              required: 'El teléfono es requerido',
                              pattern: { value: /^\+[1-9]\d{6,14}$/, message: 'Formato E.164 (ej: +5215512345678)' },
                            })}
                          />
                          {customerForm.formState.errors.phoneNumber && (
                            <div className="invalid-feedback">{customerForm.formState.errors.phoneNumber.message}</div>
                          )}
                        </div>

                        <div className="mb-3">
                          <label htmlFor="register-contrasena" className="form-label">Contraseña <span className="text-danger">*</span></label>
                          <div className="input-group input-group-merge">
                            <input id="register-contrasena"
                              type={showPassword ? 'text' : 'password'}
                              className={`form-control ${customerForm.formState.errors.password ? 'is-invalid' : ''}`}
                              placeholder={`Mínimo ${PASSWORD_MIN_LENGTH} caracteres`}
                              {...customerForm.register('password', {
                                required: 'La contraseña es requerida',
                                pattern: newPasswordPattern,
                              })}
                            />
                            <button
                              type="button"
                              className={`input-group-text ${showPassword ? 'show-password' : ''}`}
                              onClick={() => setShowPassword(!showPassword)}
                              aria-label={showPassword ? 'Ocultar la contraseña' : 'Mostrar la contraseña'}
                              aria-pressed={showPassword}
                            >
                              <span className="password-eye" aria-hidden="true"></span>
                            </button>
                          </div>
                          {customerForm.formState.errors.password && (
                            <div className="text-danger mt-1" style={{ fontSize: '0.875em' }}>
                              {customerForm.formState.errors.password.message}
                            </div>
                          )}
                        </div>

                        <div className="mb-3 d-grid">
                          <button
                            type="submit"
                            className="btn btn-primary register-hover-btn"
                            disabled={customerForm.formState.isSubmitting}
                          >
                            {customerForm.formState.isSubmitting ? (
                              <>
                                <span className="spinner-border spinner-border-sm me-1" role="status"></span>
                                Registrando...
                              </>
                            ) : (
                              'Crear Cuenta'
                            )}
                          </button>
                        </div>

                        <div className="text-center">
                          <button
                            type="button"
                            className="btn btn-link text-muted p-0 register-hover-btn"
                            onClick={() => { setAccountType(null); setError(''); }}
                          >
                            <i className="ri-arrow-left-line me-1"></i>Cambiar tipo de cuenta
                          </button>
                        </div>
                      </form>
                    </>
                  )}

                  {/* Business Registration Form */}
                  {accountType === 'business' && (
                    <>
                      <div className="text-center m-auto">
                        <h4 className="text-dark-50 pb-0 fw-bold">Registro de Empresa</h4>
                        <p className="text-muted mb-4">Ingresa los datos de tu negocio para comenzar.</p>
                      </div>

                      {error && <div className="alert alert-danger">{error}</div>}

                      <form onSubmit={businessForm.handleSubmit(onSubmitBusiness)}>
                        {/* Personal Data Section */}
                        <h5 className="header-title mb-3">Datos Personales</h5>

                        <div className="row">
                          <div className="col-md-6 mb-3">
                            <label htmlFor="register-nombre-2" className="form-label">Nombre <span className="text-danger">*</span></label>
                            <input id="register-nombre-2"
                              type="text"
                              className={`form-control ${businessForm.formState.errors.firstName ? 'is-invalid' : ''}`}
                              placeholder="Tu nombre"
                              {...businessForm.register('firstName', { required: 'El nombre es requerido', maxLength: 100 })}
                            />
                            {businessForm.formState.errors.firstName && (
                              <div className="invalid-feedback">{businessForm.formState.errors.firstName.message}</div>
                            )}
                          </div>
                          <div className="col-md-6 mb-3">
                            <label htmlFor="register-apellido-2" className="form-label">Apellido <span className="text-danger">*</span></label>
                            <input id="register-apellido-2"
                              type="text"
                              className={`form-control ${businessForm.formState.errors.lastName ? 'is-invalid' : ''}`}
                              placeholder="Tu apellido"
                              {...businessForm.register('lastName', { required: 'El apellido es requerido', maxLength: 100 })}
                            />
                            {businessForm.formState.errors.lastName && (
                              <div className="invalid-feedback">{businessForm.formState.errors.lastName.message}</div>
                            )}
                          </div>
                        </div>

                        <div className="row">
                          <div className="col-md-4 mb-3">
                            <label htmlFor="register-email-2" className="form-label">Email <span className="text-danger">*</span></label>
                            <input id="register-email-2"
                              type="email"
                              className={`form-control ${businessForm.formState.errors.email ? 'is-invalid' : ''}`}
                              placeholder="tu@email.com"
                              {...businessForm.register('email', { required: 'El email es requerido' })}
                            />
                            {businessForm.formState.errors.email && (
                              <div className="invalid-feedback">{businessForm.formState.errors.email.message}</div>
                            )}
                          </div>
                          <div className="col-md-4 mb-3">
                            <label htmlFor="register-telefono-2" className="form-label">Teléfono <span className="text-danger">*</span></label>
                            <input id="register-telefono-2"
                              type="tel"
                              className={`form-control ${businessForm.formState.errors.phoneNumber ? 'is-invalid' : ''}`}
                              placeholder="+5215512345678"
                              {...businessForm.register('phoneNumber', {
                                required: 'El teléfono es requerido',
                                pattern: { value: /^\+[1-9]\d{6,14}$/, message: 'Formato E.164 (ej: +5215512345678)' },
                              })}
                            />
                            {businessForm.formState.errors.phoneNumber && (
                              <div className="invalid-feedback">{businessForm.formState.errors.phoneNumber.message}</div>
                            )}
                          </div>
                          <div className="col-md-4 mb-3">
                            <label htmlFor="register-contrasena-2" className="form-label">Contraseña <span className="text-danger">*</span></label>
                            <div className="input-group input-group-merge">
                              <input id="register-contrasena-2"
                                type={showPassword ? 'text' : 'password'}
                                className={`form-control ${businessForm.formState.errors.password ? 'is-invalid' : ''}`}
                                placeholder={`Mínimo ${PASSWORD_MIN_LENGTH} caracteres`}
                                {...businessForm.register('password', {
                                  required: 'La contraseña es requerida',
                                  pattern: newPasswordPattern,
                                })}
                              />
                              <button
                                type="button"
                                className={`input-group-text ${showPassword ? 'show-password' : ''}`}
                                onClick={() => setShowPassword(!showPassword)}
                                aria-label={showPassword ? 'Ocultar la contraseña' : 'Mostrar la contraseña'}
                                aria-pressed={showPassword}
                              >
                                <span className="password-eye" aria-hidden="true"></span>
                              </button>
                            </div>
                            {businessForm.formState.errors.password && (
                              <div className="text-danger mt-1" style={{ fontSize: '0.875em' }}>
                                {businessForm.formState.errors.password.message}
                              </div>
                            )}
                          </div>
                        </div>

                        <hr className="my-3" />

                        {/* Business Data Section */}
                        <h5 className="header-title mb-3">Datos del Negocio</h5>

                        <div className="row">
                          <div className="col-md-6 mb-3">
                            <label htmlFor="register-nombre-del-negocio" className="form-label">Nombre del negocio <span className="text-danger">*</span></label>
                            <input id="register-nombre-del-negocio"
                              type="text"
                              className={`form-control ${businessForm.formState.errors.businessName ? 'is-invalid' : ''}`}
                              placeholder="Nombre de tu negocio"
                              {...businessForm.register('businessName', { required: 'El nombre del negocio es requerido', maxLength: 200 })}
                            />
                            {businessForm.formState.errors.businessName && (
                              <div className="invalid-feedback">{businessForm.formState.errors.businessName.message}</div>
                            )}
                          </div>
                          <div className="col-md-6 mb-3">
                            <label htmlFor="register-tipo-de-negocio" className="form-label">Tipo de negocio <span className="text-danger">*</span></label>
                            <select id="register-tipo-de-negocio"
                              className={`form-select ${businessForm.formState.errors.businessType ? 'is-invalid' : ''}`}
                              {...businessForm.register('businessType', { required: 'Selecciona el tipo', valueAsNumber: true })}
                            >
                              <option value={BusinessType.Food}>Alimentos</option>
                              <option value={BusinessType.Retail}>Comercio</option>
                              <option value={BusinessType.Service}>Servicios</option>
                            </select>
                          </div>
                        </div>

                        {/* Only what is needed to start selling; the rest can wait for Mi negocio. */}
                        <button
                          type="button"
                          className="btn btn-link px-0"
                          onClick={() => setShowOptionalData((current) => !current)}
                          aria-expanded={showOptionalData}
                          aria-controls="register-optional-data"
                        >
                          <i className={`${showOptionalData ? 'ri-subtract-line' : 'ri-add-line'} me-1`} aria-hidden="true"></i>
                          {showOptionalData ? 'Ocultar datos fiscales y dirección' : 'Agregar datos fiscales y dirección (opcional)'}
                        </button>
                        <p className="text-muted small mb-3">Puedes llenarlos después en Mi negocio; solo hacen falta para facturar.</p>

                        {showOptionalData && (
                          <div id="register-optional-data">
                            <div className="mb-3">
                              <label htmlFor="register-giro" className="form-label">Giro</label>
                              <select id="register-giro" className="form-select" {...businessForm.register('industry')}>
                                <option value="">Seleccionar...</option>
                                {INDUSTRY_OPTIONS.map((opt) => (
                                  <option key={opt} value={opt}>{opt}</option>
                                ))}
                              </select>
                            </div>
                            <div className="row">
                              <div className="col-md-4 mb-3">
                                <label htmlFor="register-rfc" className="form-label">RFC</label>
                                <input id="register-rfc"
                                  type="text"
                                  className={`form-control ${businessForm.formState.errors.rfc ? 'is-invalid' : ''}`}
                                  placeholder="XAXX010101000"
                                  maxLength={13}
                                  {...businessForm.register('rfc', {
                                    pattern: { value: /^[A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3}$/i, message: 'Formato de RFC inválido' },
                                    setValueAs: (v: string) => v ? v.toUpperCase() : v,
                                  })}
                                />
                                {businessForm.formState.errors.rfc && (
                                  <div className="invalid-feedback">{businessForm.formState.errors.rfc.message}</div>
                                )}
                              </div>
                              <div className="col-md-4 mb-3">
                                <label htmlFor="register-razon-social" className="form-label">Razón Social</label>
                                <input id="register-razon-social"
                                  type="text"
                                  className="form-control"
                                  placeholder="Nombre legal de la empresa"
                                  {...businessForm.register('legalName', { maxLength: 300 })}
                                />
                              </div>
                              <div className="col-md-4 mb-3">
                                <label htmlFor="register-regimen-fiscal" className="form-label">Régimen Fiscal</label>
                                <select id="register-regimen-fiscal" className="form-select" {...businessForm.register('taxRegime')}>
                                  <option value="">Seleccionar...</option>
                                  {TAX_REGIME_OPTIONS.map((opt) => (
                                    <option key={opt} value={opt}>{opt}</option>
                                  ))}
                                </select>
                              </div>
                            </div>

                            <div className="mb-3">
                              <label htmlFor="register-telefono-del-negocio" className="form-label">Teléfono del Negocio</label>
                              <input id="register-telefono-del-negocio"
                                type="tel"
                                className="form-control"
                                placeholder="+5215512345678"
                                {...businessForm.register('contactPhone')}
                              />
                            </div>

                            <hr className="my-3" />

                            {/* Address Section */}
                            <h5 className="header-title mb-3">Dirección del Negocio</h5>

                            <div className="row">
                              <div className="col-md-6 mb-3">
                                <label htmlFor="register-calle" className="form-label">Calle</label>
                                <input id="register-calle"
                                  type="text"
                                  className="form-control"
                                  placeholder="Nombre de la calle"
                                  {...businessForm.register('street', { maxLength: 300 })}
                                />
                              </div>
                              <div className="col-md-3 mb-3">
                                <label htmlFor="register-num-exterior" className="form-label">Núm. Exterior</label>
                                <input id="register-num-exterior"
                                  type="text"
                                  className="form-control"
                                  placeholder="123"
                                  {...businessForm.register('exteriorNumber', { maxLength: 20 })}
                                />
                              </div>
                              <div className="col-md-3 mb-3">
                                <label htmlFor="register-num-interior" className="form-label">Núm. Interior</label>
                                <input id="register-num-interior"
                                  type="text"
                                  className="form-control"
                                  placeholder="A"
                                  {...businessForm.register('interiorNumber', { maxLength: 20 })}
                                />
                              </div>
                            </div>

                            <div className="row">
                              <div className="col-md-4 mb-3">
                                <label htmlFor="register-colonia" className="form-label">Colonia</label>
                                <input id="register-colonia"
                                  type="text"
                                  className="form-control"
                                  placeholder="Colonia"
                                  {...businessForm.register('neighborhood', { maxLength: 200 })}
                                />
                              </div>
                              <div className="col-md-2 mb-3">
                                <label htmlFor="register-c-p" className="form-label">C.P.</label>
                                <input id="register-c-p"
                                  type="text"
                                  className="form-control"
                                  placeholder="01000"
                                  maxLength={5}
                                  {...businessForm.register('zipCode', { maxLength: 10 })}
                                />
                              </div>
                              <div className="col-md-3 mb-3">
                                <label htmlFor="register-ciudad-municipio" className="form-label">Ciudad / Municipio</label>
                                <input id="register-ciudad-municipio"
                                  type="text"
                                  className="form-control"
                                  placeholder="Ciudad"
                                  {...businessForm.register('city', { maxLength: 200 })}
                                />
                              </div>
                              <div className="col-md-3 mb-3">
                                <label htmlFor="register-estado" className="form-label">Estado</label>
                                <select id="register-estado" className="form-select" {...businessForm.register('state')}>
                                  <option value="">Seleccionar...</option>
                                  {MX_STATES.map((s) => (
                                    <option key={s} value={s}>{s}</option>
                                  ))}
                                </select>
                              </div>
                            </div>
                          </div>
                        )}

                        <div className="mb-3 d-grid">
                          <button
                            type="submit"
                            className="btn btn-primary register-hover-btn"
                            disabled={businessForm.formState.isSubmitting}
                          >
                            {businessForm.formState.isSubmitting ? (
                              <>
                                <span className="spinner-border spinner-border-sm me-1" role="status"></span>
                                Registrando empresa...
                              </>
                            ) : (
                              'Registrar Empresa'
                            )}
                          </button>
                        </div>

                        <div className="text-center">
                          <button
                            type="button"
                            className="btn btn-link text-muted p-0 register-hover-btn"
                            onClick={() => { setAccountType(null); setError(''); }}
                          >
                            <i className="ri-arrow-left-line me-1"></i>Cambiar tipo de cuenta
                          </button>
                        </div>
                      </form>
                    </>
                  )}
                </div>
              </div>

              <div className="row mt-3">
                <div className="col-12 text-center">
                  <p className="text-muted">
                    ¿Ya tienes una cuenta?{' '}
                    <Link to="/login" className="text-muted ms-1">
                      <b>Iniciar Sesión</b>
                    </Link>
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <footer className="footer footer-alt">
        {new Date().getFullYear()} &copy; TienditApp
      </footer>
    </div>
  );
}
