import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link } from 'react-router-dom';
import { authApi } from '../../api/authApi';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import AuthCard from '../../components/auth/AuthCard';
import { newPasswordPattern } from '../../utils/passwordRules';

type Channel = 'email' | 'sms';

interface RequestForm {
  email: string;
}

interface SmsResetForm {
  code: string;
  newPassword: string;
}

const RESEND_COOLDOWN_SECONDS = 60;
// Mirrors the Identity policy configured in the API (6+ characters, upper, lower and digit).

/**
 * Recover the password: a link by email, or a code by SMS to the phone the account confirmed (for whoever does not
 * check their email). Both answer the same whether the account exists or not.
 */
export default function ForgotPassword() {
  const [channel, setChannel] = useState<Channel>('email');
  const [emailSent, setEmailSent] = useState(false);
  const [smsEmail, setSmsEmail] = useState('');
  const [done, setDone] = useState(false);
  const [error, setError] = useState('');
  const [info, setInfo] = useState('');
  const [cooldown, setCooldown] = useState(0);

  useEffect(() => {
    if (cooldown <= 0) return;
    const timeoutId = window.setTimeout(() => setCooldown((seconds) => seconds - 1), 1000);
    return () => window.clearTimeout(timeoutId);
  }, [cooldown]);

  const requestForm = useForm<RequestForm>();
  const resetForm = useForm<SmsResetForm>();

  const chooseChannel = (next: Channel) => {
    setChannel(next);
    setError('');
    setInfo('');
    setEmailSent(false);
    setSmsEmail('');
  };

  const requestCode = async (email: string) => {
    await apiRequest('post', '/api/v1/auth/forgot-password/sms', { body: { email } });
    setSmsEmail(email);
    setCooldown(RESEND_COOLDOWN_SECONDS);
    setInfo('Si tu cuenta tiene un teléfono confirmado, te enviamos un código por SMS. Puede tardar un minuto.');
  };

  const onRequest = async ({ email }: RequestForm) => {
    setError('');
    setInfo('');
    try {
      if (channel === 'email') {
        await authApi.forgotPassword(email.trim());
        setEmailSent(true);
      } else {
        await requestCode(email.trim());
      }
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo enviar. Intenta de nuevo en un momento.'));
    }
  };

  const onReset = async ({ code, newPassword }: SmsResetForm) => {
    setError('');
    try {
      await apiRequest('post', '/api/v1/auth/reset-password/sms', { body: { email: smsEmail, code, newPassword } });
      setDone(true);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'El código no es válido o ya expiró. Pide uno nuevo.'));
    }
  };

  const resend = async () => {
    if (cooldown > 0) return;
    setError('');
    try {
      await requestCode(smsEmail);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo reenviar el código.'));
    }
  };

  return (
    <AuthCard
      title="Recuperar contraseña"
      subtitle={channel === 'email'
        ? 'Escribe tu correo y te mandamos un enlace para elegir una contraseña nueva.'
        : 'Escribe tu correo y te mandamos un código por SMS al teléfono que confirmaste.'}
      footer={<Link to="/login" className="text-muted fw-bold">Volver a iniciar sesión</Link>}
    >
      {done ? (
        <div className="alert alert-success text-center mb-0" role="status">
          Tu contraseña se actualizó. Ya puedes <Link to="/login">iniciar sesión</Link>.
        </div>
      ) : (
        <>
          <div className="btn-group w-100 mb-3" role="group" aria-label="Cómo recuperarla">
            <button
              type="button"
              className={`btn btn-sm ${channel === 'email' ? 'btn-primary' : 'btn-light'}`}
              aria-pressed={channel === 'email'}
              onClick={() => chooseChannel('email')}
            >
              <i className="ri-mail-line me-1" aria-hidden="true"></i>Por correo
            </button>
            <button
              type="button"
              className={`btn btn-sm ${channel === 'sms' ? 'btn-primary' : 'btn-light'}`}
              aria-pressed={channel === 'sms'}
              onClick={() => chooseChannel('sms')}
            >
              <i className="ri-message-2-line me-1" aria-hidden="true"></i>Por SMS
            </button>
          </div>

          {error && <div className="alert alert-danger" role="alert">{error}</div>}
          {info && <div className="alert alert-info" role="status">{info}</div>}

          {emailSent ? (
            <div className="alert alert-success text-center mb-0" role="status">
              Si el correo está registrado, te llegará un enlace para elegir tu contraseña nueva.
            </div>
          ) : channel === 'sms' && smsEmail ? (
            // Its own key: React must not reuse the inputs of the other form (they are uncontrolled and keep their text).
            <form key="sms-reset" onSubmit={resetForm.handleSubmit(onReset)} noValidate>
              <div className="mb-3">
                <label htmlFor="sms-code" className="form-label">Código</label>
                <input
                  id="sms-code"
                  className={`form-control text-center ${resetForm.formState.errors.code ? 'is-invalid' : ''}`}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={6}
                  placeholder="000000"
                  style={{ fontSize: '1.5rem', letterSpacing: '0.5rem' }}
                  {...resetForm.register('code', {
                    required: 'Escribe el código.',
                    pattern: { value: /^\d{5,6}$/, message: 'El código tiene 5 o 6 dígitos.' },
                  })}
                />
                {resetForm.formState.errors.code && <div className="invalid-feedback">{resetForm.formState.errors.code.message}</div>}
              </div>
              <div className="mb-3">
                <label htmlFor="sms-new-password" className="form-label">Contraseña nueva</label>
                <input
                  id="sms-new-password"
                  type="password"
                  autoComplete="new-password"
                  className={`form-control ${resetForm.formState.errors.newPassword ? 'is-invalid' : ''}`}
                  {...resetForm.register('newPassword', {
                    required: 'Escribe la contraseña nueva.',
                    pattern: newPasswordPattern,
                  })}
                />
                {resetForm.formState.errors.newPassword && (
                  <div className="invalid-feedback">{resetForm.formState.errors.newPassword.message}</div>
                )}
              </div>
              <div className="d-grid gap-2">
                <button className="btn btn-primary" type="submit" disabled={resetForm.formState.isSubmitting}>
                  {resetForm.formState.isSubmitting ? 'Guardando…' : 'Guardar contraseña'}
                </button>
                <button type="button" className="btn btn-link btn-sm" onClick={() => void resend()} disabled={cooldown > 0}>
                  {cooldown > 0 ? `Pedir otro código en ${cooldown} s` : '¿No te llegó? Pedir otro código'}
                </button>
              </div>
            </form>
          ) : (
            <form key="request" onSubmit={requestForm.handleSubmit(onRequest)} noValidate>
              <div className="mb-3">
                <label htmlFor="recover-email" className="form-label">Correo</label>
                <input
                  id="recover-email"
                  type="email"
                  autoComplete="email"
                  className={`form-control ${requestForm.formState.errors.email ? 'is-invalid' : ''}`}
                  placeholder="tu@correo.com"
                  {...requestForm.register('email', { required: 'Escribe tu correo.' })}
                />
                {requestForm.formState.errors.email && (
                  <div className="invalid-feedback">{requestForm.formState.errors.email.message}</div>
                )}
              </div>
              <div className="d-grid">
                <button className="btn btn-primary" type="submit" disabled={requestForm.formState.isSubmitting}>
                  {requestForm.formState.isSubmitting
                    ? 'Enviando…'
                    : channel === 'email' ? 'Enviar enlace' : 'Enviar código'}
                </button>
              </div>
            </form>
          )}
        </>
      )}
    </AuthCard>
  );
}
