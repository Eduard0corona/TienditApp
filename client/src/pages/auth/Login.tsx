import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import type { LoginRequest } from '../../types';
import TwoFactorLoginStep from './TwoFactorLoginStep';
import type { AccountActivatedState } from './ConfirmEmail';
import BrandLogo from '../../components/common/BrandLogo';
import { getApiErrorMessage } from '../../api/errors';

export default function Login() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [error, setError] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  // Kept only in component memory: never in router state or browser storage.
  const [twoFactorChallengeToken, setTwoFactorChallengeToken] = useState<string | null>(null);

  // Without a page to return to, '/' picks the start: the guided setup for a new business, or the screen of each role.
  const from = (location.state as { from?: { pathname: string } })?.from?.pathname || '/';
  // Arriving from the link of the confirmation email, which just activated the account.
  const accountActivated = Boolean((location.state as Partial<AccountActivatedState> | null)?.accountActivated);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginRequest>();

  const onSubmit = async (data: LoginRequest) => {
    setError('');
    try {
      const result = await login(data);
      if (result.requiresTwoFactor) {
        setTwoFactorChallengeToken(result.twoFactorChallengeToken);
      } else {
        navigate(from, { replace: true });
      }
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al iniciar sesión');
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
            <div className="col-xxl-4 col-lg-5">
              <div className="card">
                {/* Logo */}
                <div className="card-header py-4 text-center bg-primary">
                  <Link to="/">
                    <BrandLogo tone="light" />
                  </Link>
                </div>

                <div className="card-body p-4">
                  {twoFactorChallengeToken ? (
                    <TwoFactorLoginStep
                      challengeToken={twoFactorChallengeToken}
                      onSuccess={() => navigate(from, { replace: true })}
                      onCancel={() => setTwoFactorChallengeToken(null)}
                    />
                  ) : (
                    <>
                      <div className="text-center w-75 m-auto">
                        <h4 className="text-dark-50 text-center pb-0 fw-bold">Iniciar sesión</h4>
                        <p className="text-muted mb-4">
                          Ingresa tu correo y tu contraseña para entrar.
                        </p>
                      </div>

                      {error ? (
                        <div className="alert alert-danger" role="alert">
                          {error}
                        </div>
                      ) : (
                        accountActivated && (
                          <div className="alert alert-success" role="status">
                            Tu cuenta quedó activada. Inicia sesión con tu correo y tu contraseña.
                          </div>
                        )
                      )}

                      <form onSubmit={handleSubmit(onSubmit)}>
                        <div className="mb-3">
                          <label htmlFor="emailaddress" className="form-label">
                            Correo
                          </label>
                          <input
                            className={`form-control ${errors.email ? 'is-invalid' : ''}`}
                            type="email"
                            id="emailaddress"
                            placeholder="Ingresa tu correo"
                            {...register('email', { required: 'El correo es requerido' })}
                          />
                          {errors.email && (
                            <div className="invalid-feedback">{errors.email.message}</div>
                          )}
                        </div>

                        <div className="mb-3">
                          <Link to="/forgot-password" className="text-muted float-end">
                            <small>¿Olvidaste tu contraseña?</small>
                          </Link>
                          <label htmlFor="password" className="form-label">
                            Contraseña
                          </label>
                          <div className="input-group input-group-merge">
                            <input
                              type={showPassword ? 'text' : 'password'}
                              id="password"
                              className={`form-control ${errors.password ? 'is-invalid' : ''}`}
                              placeholder="Ingresa tu contraseña"
                              {...register('password', { required: 'La contraseña es requerida' })}
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
                          {errors.password && (
                            <div className="text-danger mt-1" style={{ fontSize: '0.875em' }}>
                              {errors.password.message}
                            </div>
                          )}
                        </div>

                        <div className="mb-3 text-center">
                          <button
                            className="btn btn-primary"
                            type="submit"
                            disabled={isSubmitting}
                          >
                            {isSubmitting ? (
                              <>
                                <span className="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span>
                                Ingresando...
                              </>
                            ) : (
                              'Iniciar sesión'
                            )}
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
                    ¿No tienes una cuenta?{' '}
                    <Link to="/register" className="text-muted ms-1">
                      <b>Regístrate</b>
                    </Link>
                  </p>
                  <p className="text-muted">
                    <Link to="/check-email" className="text-muted">
                      <small>¿No te llegó el correo para activar tu cuenta?</small>
                    </Link>
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <footer className="ta-footer ta-footer-centered">
        {new Date().getFullYear()} &copy; TienditApp
      </footer>
    </div>
  );
}
