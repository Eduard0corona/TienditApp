import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useLocation, useSearchParams, Link } from 'react-router-dom';
import { authApi } from '../../api/authApi';
import { getApiErrorMessage } from '../../api/errors';
import BrandLogo from '../../components/common/BrandLogo';

const RESEND_COOLDOWN_SECONDS = 60;

interface ConfirmPhoneForm {
  code: string;
}

export default function ConfirmPhone() {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  // The account travels in the URL so a reload keeps the screen usable (router state is lost on reload).
  const routerState = location.state as { userId?: string } | null;
  const userId = searchParams.get('userId') || routerState?.userId || '';
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [resendMessage, setResendMessage] = useState('');
  const [resending, setResending] = useState(false);
  // Coming straight from registration the first code was just sent: wait before offering another one.
  const [cooldown, setCooldown] = useState(() => (routerState?.userId ? RESEND_COOLDOWN_SECONDS : 0));

  useEffect(() => {
    if (cooldown <= 0) return;
    const timeoutId = window.setTimeout(() => setCooldown((seconds) => seconds - 1), 1000);
    return () => window.clearTimeout(timeoutId);
  }, [cooldown]);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ConfirmPhoneForm>();

  const onSubmit = async (data: ConfirmPhoneForm) => {
    setError('');
    setResendMessage('');
    try {
      await authApi.confirmPhone(userId, data.code);
      setSuccess('Número confirmado. Ahora puedes iniciar sesión.');
      setTimeout(() => navigate('/login'), 2000);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'El código no es válido o ya expiró. Pide uno nuevo.'));
    }
  };

  const handleResend = async () => {
    if (!userId || cooldown > 0 || resending) return;
    setResending(true);
    setError('');
    setResendMessage('');
    try {
      await authApi.resendPhoneConfirmation(userId);
      setResendMessage('Te enviamos un código nuevo. Puede tardar un minuto en llegar.');
      setCooldown(RESEND_COOLDOWN_SECONDS);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo reenviar el código. Intenta de nuevo en un momento.'));
    } finally {
      setResending(false);
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
                <div className="card-header py-4 text-center bg-primary">
                  <Link to="/">
                    <BrandLogo tone="light" />
                  </Link>
                </div>

                <div className="card-body p-4">
                  <div className="text-center w-75 m-auto">
                    <h4 className="text-dark-50 text-center pb-0 fw-bold">Confirmar teléfono</h4>
                    <p className="text-muted mb-4">
                      Escribe el código de 5 o 6 dígitos que te enviamos por SMS o WhatsApp.
                    </p>
                  </div>

                  {error && <div className="alert alert-danger" role="alert">{error}</div>}
                  {success && <div className="alert alert-success" role="status">{success}</div>}
                  {resendMessage && <div className="alert alert-info" role="status">{resendMessage}</div>}

                  {!userId ? (
                    <div className="alert alert-warning mb-0" role="alert">
                      No encontramos la cuenta por confirmar. Abre esta pantalla desde el registro o{' '}
                      <Link to="/login">inicia sesión</Link>.
                    </div>
                  ) : (
                  <form onSubmit={handleSubmit(onSubmit)}>
                    <div className="mb-3">
                      <label htmlFor="code" className="form-label">Código</label>
                      <input
                        className={`form-control text-center ${errors.code ? 'is-invalid' : ''}`}
                        type="text"
                        id="code"
                        maxLength={6}
                        placeholder="000000"
                        inputMode="numeric"
                        pattern="[0-9]*"
                        style={{ fontSize: '1.5rem', letterSpacing: '0.5rem' }}
                        autoComplete="one-time-code"
                        {...register('code', {
                          required: 'Escribe el código.',
                          minLength: { value: 5, message: 'El código tiene 5 o 6 dígitos.' },
                          maxLength: { value: 6, message: 'El código tiene 5 o 6 dígitos.' },
                          pattern: { value: /^\d{5,6}$/, message: 'Solo se permiten dígitos (5 o 6).' },
                        })}
                      />
                      {errors.code && <div className="invalid-feedback">{errors.code.message}</div>}
                    </div>

                    <div className="mb-3 text-center">
                      <button className="btn btn-primary" type="submit" disabled={isSubmitting || Boolean(success)}>
                        {isSubmitting ? 'Verificando…' : 'Verificar'}
                      </button>
                    </div>

                    <div className="text-center">
                      <button
                        type="button"
                        className="btn btn-link btn-sm"
                        onClick={() => void handleResend()}
                        disabled={cooldown > 0 || resending || Boolean(success)}
                      >
                        {resending
                          ? 'Enviando…'
                          : cooldown > 0
                            ? `Reenviar código en ${cooldown} s`
                            : '¿No te llegó? Reenviar código'}
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
                      <b>Volver a iniciar sesión</b>
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
