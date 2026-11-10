import { useEffect, useRef, useState } from 'react';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import { useAuth } from '../../hooks/useAuth';

type Step = 'idle' | 'request' | 'confirm';

/**
 * The phone of the account recovers the password by SMS, so it changes (or gets confirmed) with the password and a
 * code sent to the new number.
 */
export default function PhoneChangeCard() {
  const { user, refreshProfile } = useAuth();
  const [step, setStep] = useState<Step>('idle');
  const [phone, setPhone] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  // The number the code went to: what is typed in the field after sending it does not change where it is confirmed.
  const [sentTo, setSentTo] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'danger' | 'info'; text: string } | null>(null);
  // Where the focus goes once a step changes: its first field, or back to the button that opened it. Not on the page's load.
  const moved = useRef(false);
  const startButton = useRef<HTMLButtonElement>(null);
  const phoneInput = useRef<HTMLInputElement>(null);
  const codeInput = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (!moved.current) return;
    (step === 'request' ? phoneInput : step === 'confirm' ? codeInput : startButton).current?.focus();
  }, [step]);

  const goTo = (next: Step) => {
    moved.current = true;
    setStep(next);
  };

  const start = () => {
    setPhone(user?.phoneNumberConfirmed ? '' : user?.phoneNumber ?? '');
    setPassword('');
    setCode('');
    setMessage(null);
    goTo('request');
  };

  /** Back to ask for another code: what the previous one said goes away (the code typed, once another is sent). */
  const back = () => {
    setMessage(null);
    goTo('request');
  };

  const cancel = () => {
    setMessage(null);
    goTo('idle');
  };

  const requestCode = async () => {
    const number = phone;
    setBusy(true);
    setMessage(null);
    try {
      await apiRequest('post', '/api/v1/auth/profile/phone', { body: { phoneNumber: number, currentPassword: password } });
      setSentTo(number);
      setPassword('');
      setCode('');
      goTo('confirm');
      setMessage({ type: 'info', text: 'Te enviamos un código al número nuevo.' });
    } catch (err: unknown) {
      setMessage({ type: 'danger', text: getApiErrorMessage(err, 'No se pudo enviar el código.') });
    } finally {
      setBusy(false);
    }
  };

  const confirm = async () => {
    setBusy(true);
    setMessage(null);
    try {
      await apiRequest('post', '/api/v1/auth/profile/phone/confirm', { body: { phoneNumber: sentTo, code } });
      await refreshProfile();
      goTo('idle');
      setMessage({ type: 'success', text: 'Número confirmado. Con él puedes recuperar tu contraseña por SMS.' });
    } catch (err: unknown) {
      setMessage({ type: 'danger', text: getApiErrorMessage(err, 'El código no es válido o ya expiró.') });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="card">
      <div className="card-body">
        <h4 className="header-title mt-0 mb-3">Teléfono</h4>
        {/* Always on the page, so what appears in it is announced; a failure is announced as an alert. */}
        <div role="status">
          {message && message.type !== 'danger' && <div className={`alert alert-${message.type} py-2`}>{message.text}</div>}
        </div>
        {message?.type === 'danger' && <div className="alert alert-danger py-2" role="alert">{message.text}</div>}

        {step === 'idle' && (
          <>
            <p className="mb-2">
              {user?.phoneNumber || 'Sin teléfono'}
              {user?.phoneNumber && (
                <span className={`badge ms-2 ${user.phoneNumberConfirmed ? 'bg-success' : 'bg-warning text-dark'}`}>
                  {user.phoneNumberConfirmed ? 'Confirmado' : 'Sin confirmar'}
                </span>
              )}
            </p>
            <p className="small text-muted">Con un teléfono confirmado recuperas tu contraseña por SMS.</p>
            <button ref={startButton} type="button" className="btn btn-light btn-sm" onClick={start}>
              {user?.phoneNumber && !user.phoneNumberConfirmed ? 'Confirmar o cambiar número' : 'Cambiar número'}
            </button>
          </>
        )}

        {step === 'request' && (
          <form onSubmit={(event) => { event.preventDefault(); void requestCode(); }}>
            <div className="mb-2">
              <label className="form-label" htmlFor="profile-new-phone">Número</label>
              <input id="profile-new-phone" ref={phoneInput} type="tel" autoComplete="tel" className="form-control" placeholder="10 dígitos" value={phone}
                onChange={(event) => setPhone(event.target.value)} required />
            </div>
            <div className="mb-3">
              <label className="form-label" htmlFor="profile-phone-password">Tu contraseña</label>
              <input id="profile-phone-password" type="password" autoComplete="current-password" className="form-control" value={password}
                onChange={(event) => setPassword(event.target.value)} required />
            </div>
            <div className="d-flex gap-2">
              <button type="button" className="btn btn-light btn-sm" onClick={cancel} disabled={busy} aria-label="Cancelar el cambio de número">
                Cancelar
              </button>
              <button type="submit" className="btn btn-primary btn-sm" disabled={busy || !phone.trim() || !password}>Enviar código</button>
            </div>
          </form>
        )}

        {step === 'confirm' && (
          <form onSubmit={(event) => { event.preventDefault(); void confirm(); }}>
            <div className="mb-3">
              <label className="form-label" htmlFor="profile-phone-code">Código que llegó a {sentTo}</label>
              <input
                id="profile-phone-code"
                ref={codeInput}
                className="form-control text-center"
                inputMode="numeric"
                autoComplete="one-time-code"
                maxLength={6}
                placeholder="000000"
                value={code}
                onChange={(event) => setCode(event.target.value)}
                required
              />
            </div>
            <div className="d-flex gap-2">
              <button type="button" className="btn btn-light btn-sm" onClick={back} disabled={busy}>Atrás</button>
              <button type="submit" className="btn btn-primary btn-sm" disabled={busy || !/^\d{5,6}$/.test(code)}>Confirmar</button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
