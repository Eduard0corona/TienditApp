import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useSearchParams } from 'react-router-dom';
import { authApi } from '../../api/authApi';
import BrandLogo from '../../components/common/BrandLogo';

interface ResetPasswordForm {
  newPassword: string;
  confirmPassword: string;
}

const getErrorMessage = (err: unknown, fallback: string) =>
  (err as { response?: { data?: { error?: string } } })?.response?.data?.error || fallback;

// Mirrors the Identity policy configured in the API (6+ characters, upper, lower and digit).
const PASSWORD_RULES = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{6,}$/;

export default function ResetPassword() {
  const [searchParams] = useSearchParams();
  const userId = searchParams.get('userId') ?? '';
  const token = searchParams.get('token') ?? '';
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');

  const {
    register,
    handleSubmit,
    getValues,
    formState: { errors, isSubmitting },
  } = useForm<ResetPasswordForm>();

  const onSubmit = async (data: ResetPasswordForm) => {
    setError('');
    try {
      await authApi.resetPassword(userId, token, data.newPassword);
      setSuccess(true);
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'No se pudo restablecer la contraseña. El enlace pudo haber expirado.'));
    }
  };

  const linkIsValid = userId.length > 0 && token.length > 0;

  return (
    <div className="authentication-bg position-relative" style={{ minHeight: '100vh' }}>
      <div className="account-pages pt-2 pt-sm-5 pb-4 pb-sm-5 position-relative">
        <div className="container">
          <div className="row justify-content-center">
            <div className="col-xxl-4 col-lg-5">
              <div className="card">
                <div className="card-header py-4 text-center bg-primary">
                  <Link to="/">
                    <BrandLogo tone="light" />
                  </Link>
                </div>

                <div className="card-body p-4">
                  <div className="text-center w-75 m-auto">
                    <h4 className="text-dark-50 text-center pb-0 fw-bold">Nueva contraseña</h4>
                    <p className="text-muted mb-4">Escribe la nueva contraseña de tu cuenta.</p>
                  </div>

                  {error && <div className="alert alert-danger" role="alert">{error}</div>}

                  {!linkIsValid ? (
                    <div className="alert alert-warning text-center" role="alert">
                      El enlace no es válido. Solicita uno nuevo desde{' '}
                      <Link to="/forgot-password">Recuperar contraseña</Link>.
                    </div>
                  ) : success ? (
                    <div className="alert alert-success text-center" role="status">
                      <i className="ri-check-line me-1"></i>
                      Tu contraseña se actualizó. Ya puedes <Link to="/login">iniciar sesión</Link>.
                    </div>
                  ) : (
                    <form onSubmit={handleSubmit(onSubmit)} noValidate>
                      <div className="mb-3">
                        <label htmlFor="newPassword" className="form-label">Nueva contraseña</label>
                        <input
                          id="newPassword"
                          type="password"
                          autoComplete="new-password"
                          className={`form-control ${errors.newPassword ? 'is-invalid' : ''}`}
                          {...register('newPassword', {
                            required: 'La contraseña es requerida',
                            pattern: {
                              value: PASSWORD_RULES,
                              message: 'Usa al menos 6 caracteres con mayúscula, minúscula y número.',
                            },
                          })}
                        />
                        {errors.newPassword && <div className="invalid-feedback">{errors.newPassword.message}</div>}
                      </div>

                      <div className="mb-3">
                        <label htmlFor="confirmPassword" className="form-label">Confirmar contraseña</label>
                        <input
                          id="confirmPassword"
                          type="password"
                          autoComplete="new-password"
                          className={`form-control ${errors.confirmPassword ? 'is-invalid' : ''}`}
                          {...register('confirmPassword', {
                            required: 'Confirma la contraseña',
                            validate: (value) => value === getValues('newPassword') || 'Las contraseñas no coinciden',
                          })}
                        />
                        {errors.confirmPassword && <div className="invalid-feedback">{errors.confirmPassword.message}</div>}
                      </div>

                      <div className="mb-0 text-center">
                        <button className="btn btn-primary" type="submit" disabled={isSubmitting}>
                          {isSubmitting ? 'Guardando...' : 'Guardar contraseña'}
                        </button>
                      </div>
                    </form>
                  )}
                </div>
              </div>

              <div className="row mt-3">
                <div className="col-12 text-center">
                  <p className="text-muted">
                    <Link to="/login" className="text-muted ms-1">
                      <b>Volver al login</b>
                    </Link>
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
