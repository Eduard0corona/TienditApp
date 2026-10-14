import { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { authApi } from '../../api/authApi';
import { getApiErrorMessage } from '../../api/errors';
import AuthCard from '../../components/auth/AuthCard';

const RESEND_COOLDOWN_SECONDS = 60;

/**
 * After signing up: the account is activated from the link sent by email and then signs in with its credentials.
 * The message names no address and is the same for every account; a new link can be asked for with the email.
 */
export default function CheckEmail() {
  const location = useLocation();
  // Router state only, never the URL: it prefills the form below and a reload forgets it.
  const routerState = location.state as { email?: string } | null;
  const [email, setEmail] = useState(routerState?.email ?? '');
  const [info, setInfo] = useState('');
  const [error, setError] = useState('');
  const [resending, setResending] = useState(false);
  // Coming straight from the sign-up the first email was just sent: wait before offering another one.
  const [cooldown, setCooldown] = useState(() => (routerState?.email ? RESEND_COOLDOWN_SECONDS : 0));

  useEffect(() => {
    if (cooldown <= 0) return;
    const timeoutId = window.setTimeout(() => setCooldown((seconds) => seconds - 1), 1000);
    return () => window.clearTimeout(timeoutId);
  }, [cooldown]);

  const handleResend = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!email.trim() || cooldown > 0 || resending) return;
    setResending(true);
    setInfo('');
    setError('');
    try {
      await authApi.resendConfirmationEmail(email.trim());
      setInfo('Si la cuenta existe y aún no está activa, te enviamos un enlace nuevo.');
      setCooldown(RESEND_COOLDOWN_SECONDS);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo enviar el enlace. Intenta de nuevo en un momento.'));
    } finally {
      setResending(false);
    }
  };

  return (
    <AuthCard
      title="Revisa tu correo"
      subtitle="Te enviamos un enlace para activar tu cuenta."
      footer={<Link to="/login" className="text-muted fw-bold">Ir a iniciar sesión</Link>}
    >
      <div className="text-center mb-3">
        <i className="ri-mail-send-line text-primary" style={{ fontSize: '3rem' }} aria-hidden="true"></i>
      </div>
      <p>Abre el correo y haz clic en el enlace. Después inicia sesión con tu correo y tu contraseña.</p>
      {/* The page is the same whether the email had an account or not; that email says how to enter. */}
      <p>¿Ya tenías una cuenta con ese correo? Te escribimos cómo entrar.</p>
      <p className="text-muted small">
        Si no lo ves en unos minutos, revisa la carpeta de spam o correo no deseado. El enlace vence en 24 horas.
      </p>

      {info && <div className="alert alert-info" role="status">{info}</div>}
      {error && <div className="alert alert-danger" role="alert">{error}</div>}

      <form onSubmit={(event) => void handleResend(event)}>
        <label htmlFor="resend-email" className="form-label">
          ¿No te llegó? Pide un enlace nuevo
        </label>
        <div className="input-group">
          <input
            id="resend-email"
            type="email"
            className="form-control"
            placeholder="tu@correo.com"
            autoComplete="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />
          <button className="btn btn-primary" type="submit" disabled={resending || cooldown > 0 || !email.trim()}>
            {resending ? 'Enviando…' : cooldown > 0 ? `Reenviar en ${cooldown} s` : 'Reenviar'}
          </button>
        </div>
      </form>
    </AuthCard>
  );
}
