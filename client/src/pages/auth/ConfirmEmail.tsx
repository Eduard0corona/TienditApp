import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { authApi } from '../../api/authApi';
import { getApiErrorMessage } from '../../api/errors';
import BrandLogo from '../../components/common/BrandLogo';

type ConfirmationStatus = 'loading' | 'error';

const INCOMPLETE_LINK_MESSAGE = 'El enlace está incompleto. Ábrelo de nuevo desde tu correo o pide uno nuevo.';

/** What the login receives after the link activated the account (router state, see Login). */
export interface AccountActivatedState {
  accountActivated: true;
}

// Landing page of the link in the confirmation email: it confirms through the API, which activates the account,
// and sends the person to the login to enter their credentials. A link that fails explains why and offers a new one.
export default function ConfirmEmail() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const userId = searchParams.get('userId') ?? '';
  const token = searchParams.get('token') ?? '';
  const hasLink = Boolean(userId && token);
  const [status, setStatus] = useState<ConfirmationStatus>(hasLink ? 'loading' : 'error');
  const [message, setMessage] = useState(hasLink ? '' : INCOMPLETE_LINK_MESSAGE);
  const [email, setEmail] = useState('');
  const [resendMessage, setResendMessage] = useState('');
  const [resending, setResending] = useState(false);
  // StrictMode runs effects twice in development; one confirmation request is enough.
  const requestedRef = useRef(false);

  useEffect(() => {
    if (!hasLink || requestedRef.current) return;
    requestedRef.current = true;

    authApi
      .confirmEmail(userId, token)
      .then(() => {
        const state: AccountActivatedState = { accountActivated: true };
        // Replace: going back must not open the link again.
        navigate('/login', { replace: true, state });
      })
      .catch((err: unknown) => {
        setStatus('error');
        setMessage(getApiErrorMessage(err, 'No pudimos confirmar tu correo. Es posible que el enlace haya expirado.'));
      });
  }, [hasLink, navigate, token, userId]);

  const handleResend = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!email.trim() || resending) return;
    setResending(true);
    setResendMessage('');
    try {
      await authApi.resendConfirmationEmail(email.trim());
      setResendMessage('Si el correo está registrado, te enviamos un enlace nuevo.');
    } catch (err: unknown) {
      setResendMessage(getApiErrorMessage(err, 'No se pudo enviar el enlace. Intenta de nuevo en un momento.'));
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

                <div className="card-body p-4 text-center">
                  <h4 className="text-dark-50 pb-0 fw-bold">Confirmar correo</h4>

                  {status === 'loading' && (
                    <div className="py-3" role="status">
                      <div className="spinner-border text-primary mb-2" aria-hidden="true"></div>
                      <p className="text-muted mb-0">Confirmando tu correo…</p>
                    </div>
                  )}

                  {status === 'error' && (
                    <>
                      <div className="alert alert-danger mt-3 text-start" role="alert">
                        {message}
                      </div>
                      <form className="text-start" onSubmit={(event) => void handleResend(event)}>
                        <label htmlFor="resend-email" className="form-label">
                          Pide un enlace nuevo
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
                          <button className="btn btn-primary" type="submit" disabled={resending || !email.trim()}>
                            {resending ? 'Enviando…' : 'Enviar'}
                          </button>
                        </div>
                        {resendMessage && (
                          <div className="form-text" role="status">
                            {resendMessage}
                          </div>
                        )}
                      </form>
                    </>
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
