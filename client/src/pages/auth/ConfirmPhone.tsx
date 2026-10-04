import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useLocation, useSearchParams, Link } from 'react-router-dom';
import { authApi } from '../../api/authApi';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import AuthCard from '../../components/auth/AuthCard';

const RESEND_COOLDOWN_SECONDS = 60;

interface ConfirmPhoneForm {
  code: string;
}

interface ChangePhoneForm {
  phoneNumber: string;
  password: string;
}

/**
 * The code sent at registration. A number typed wrong is fixed here, with the password of the account, before
 * the account can sign in.
 */
export default function ConfirmPhone() {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  // The account travels in the URL so a reload keeps the screen usable (router state is lost on reload).
  const routerState = location.state as { userId?: string } | null;
  const userId = searchParams.get('userId') || routerState?.userId || '';
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [info, setInfo] = useState('');
  const [resending, setResending] = useState(false);
  const [changingPhone, setChangingPhone] = useState(false);
  // Coming straight from registration the first code was just sent: wait before offering another one.
  const [cooldown, setCooldown] = useState(() => (routerState?.userId ? RESEND_COOLDOWN_SECONDS : 0));

  useEffect(() => {
    if (cooldown <= 0) return;
    const timeoutId = window.setTimeout(() => setCooldown((seconds) => seconds - 1), 1000);
    return () => window.clearTimeout(timeoutId);
  }, [cooldown]);

  const confirmForm = useForm<ConfirmPhoneForm>();
  const changeForm = useForm<ChangePhoneForm>();

  const onSubmit = async (data: ConfirmPhoneForm) => {
    setError('');
    setInfo('');
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
    setInfo('');
    try {
      await authApi.resendPhoneConfirmation(userId);
      setInfo('Te enviamos un código nuevo. Puede tardar un minuto en llegar.');
      setCooldown(RESEND_COOLDOWN_SECONDS);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo reenviar el código. Intenta de nuevo en un momento.'));
    } finally {
      setResending(false);
    }
  };

  const onChangePhone = async (data: ChangePhoneForm) => {
    setError('');
    setInfo('');
    try {
      await apiRequest('post', '/api/v1/auth/change-unconfirmed-phone', {
        body: { userId, password: data.password, phoneNumber: data.phoneNumber },
      });
      changeForm.reset();
      setChangingPhone(false);
      setCooldown(RESEND_COOLDOWN_SECONDS);
      setInfo('Te enviamos un código al número nuevo.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo cambiar el número.'));
    }
  };

  return (
    <AuthCard
      title="Confirmar teléfono"
      subtitle="Escribe el código de 5 o 6 dígitos que te enviamos por SMS o WhatsApp."
      footer={<Link to="/login" className="text-muted fw-bold">Volver a iniciar sesión</Link>}
    >
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {success && <div className="alert alert-success" role="status">{success}</div>}
      {info && <div className="alert alert-info" role="status">{info}</div>}

      {!userId ? (
        <div className="alert alert-warning mb-0" role="alert">
          No encontramos la cuenta por confirmar. Abre esta pantalla desde el registro o{' '}
          <Link to="/login">inicia sesión</Link>.
        </div>
      ) : changingPhone ? (
        // Its own key: React must not reuse the inputs of the other form (they are uncontrolled and keep their text).
        <form key="change-phone" onSubmit={changeForm.handleSubmit(onChangePhone)} noValidate>
          <div className="mb-3">
            <label htmlFor="new-phone" className="form-label">Número correcto</label>
            <input
              id="new-phone"
              type="tel"
              autoComplete="tel"
              className={`form-control ${changeForm.formState.errors.phoneNumber ? 'is-invalid' : ''}`}
              placeholder="10 dígitos"
              {...changeForm.register('phoneNumber', { required: 'Escribe tu número.' })}
            />
            {changeForm.formState.errors.phoneNumber && (
              <div className="invalid-feedback">{changeForm.formState.errors.phoneNumber.message}</div>
            )}
          </div>
          <div className="mb-3">
            <label htmlFor="change-phone-password" className="form-label">Tu contraseña</label>
            <input
              id="change-phone-password"
              type="password"
              autoComplete="current-password"
              className={`form-control ${changeForm.formState.errors.password ? 'is-invalid' : ''}`}
              {...changeForm.register('password', { required: 'Escribe la contraseña con la que te registraste.' })}
            />
            {changeForm.formState.errors.password && (
              <div className="invalid-feedback">{changeForm.formState.errors.password.message}</div>
            )}
            <div className="form-text">La que elegiste al registrarte: así sabemos que la cuenta es tuya.</div>
          </div>
          <div className="d-flex justify-content-end gap-2">
            <button type="button" className="btn btn-light" onClick={() => setChangingPhone(false)}>Cancelar</button>
            <button className="btn btn-primary" type="submit" disabled={changeForm.formState.isSubmitting || cooldown > 0}>
              {cooldown > 0 ? `Espera ${cooldown} s` : 'Enviar código'}
            </button>
          </div>
        </form>
      ) : (
        <form key="confirm" onSubmit={confirmForm.handleSubmit(onSubmit)}>
          <div className="mb-3">
            <label htmlFor="code" className="form-label">Código</label>
            <input
              className={`form-control text-center ${confirmForm.formState.errors.code ? 'is-invalid' : ''}`}
              type="text"
              id="code"
              maxLength={6}
              placeholder="000000"
              inputMode="numeric"
              pattern="[0-9]*"
              style={{ fontSize: '1.5rem', letterSpacing: '0.5rem' }}
              autoComplete="one-time-code"
              {...confirmForm.register('code', {
                required: 'Escribe el código.',
                minLength: { value: 5, message: 'El código tiene 5 o 6 dígitos.' },
                maxLength: { value: 6, message: 'El código tiene 5 o 6 dígitos.' },
                pattern: { value: /^\d{5,6}$/, message: 'Solo se permiten dígitos (5 o 6).' },
              })}
            />
            {confirmForm.formState.errors.code && <div className="invalid-feedback">{confirmForm.formState.errors.code.message}</div>}
          </div>

          <div className="mb-3 text-center">
            <button className="btn btn-primary" type="submit" disabled={confirmForm.formState.isSubmitting || Boolean(success)}>
              {confirmForm.formState.isSubmitting ? 'Verificando…' : 'Verificar'}
            </button>
          </div>

          <div className="d-flex flex-wrap justify-content-center gap-2">
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
            <button
              type="button"
              className="btn btn-link btn-sm"
              onClick={() => {
                setError('');
                setInfo('');
                setChangingPhone(true);
              }}
              disabled={Boolean(success)}
            >
              ¿Número equivocado? Cambiarlo
            </button>
          </div>
        </form>
      )}
    </AuthCard>
  );
}
