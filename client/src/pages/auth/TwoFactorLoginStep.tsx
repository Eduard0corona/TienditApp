import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useAuth } from '../../hooks/useAuth';
import { getApiErrorMessage } from '../../api/errors';

interface TwoFactorLoginStepProps {
  challengeToken: string;
  onSuccess: () => void;
  onCancel: () => void;
}

interface TwoFactorForm {
  code: string;
}

export default function TwoFactorLoginStep({ challengeToken, onSuccess, onCancel }: TwoFactorLoginStepProps) {
  const { completeTwoFactorLogin } = useAuth();
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);
  const [error, setError] = useState('');

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<TwoFactorForm>();

  const toggleCodeType = () => {
    setUseRecoveryCode((current) => !current);
    setError('');
    reset({ code: '' });
  };

  const onSubmit = async (data: TwoFactorForm) => {
    setError('');
    try {
      await completeTwoFactorLogin({
        twoFactorChallengeToken: challengeToken,
        code: data.code,
        useRecoveryCode,
      });
      onSuccess();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'No se pudo verificar el código');
      setError(msg);
    }
  };

  return (
    <>
      <div className="text-center w-75 m-auto">
        <h4 className="text-dark-50 text-center pb-0 fw-bold">Verificación en dos pasos</h4>
        <p className="text-muted mb-4">
          {useRecoveryCode
            ? 'Ingresa uno de tus códigos de recuperación. Cada código solo puede usarse una vez.'
            : 'Ingresa el código de 6 dígitos de tu app de autenticación.'}
        </p>
      </div>

      {error && (
        <div className="alert alert-danger" role="alert">
          {error}
        </div>
      )}

      <form onSubmit={handleSubmit(onSubmit)}>
        <div className="mb-3">
          <label htmlFor="twoFactorCode" className="form-label">
            {useRecoveryCode ? 'Código de recuperación' : 'Código de verificación'}
          </label>
          <input
            key={useRecoveryCode ? 'recovery' : 'authenticator'}
            id="twoFactorCode"
            type="text"
            className={`form-control text-center ${errors.code ? 'is-invalid' : ''}`}
            autoComplete={useRecoveryCode ? 'off' : 'one-time-code'}
            inputMode={useRecoveryCode ? 'text' : 'numeric'}
            maxLength={useRecoveryCode ? 32 : 7}
            placeholder={useRecoveryCode ? 'XXXXX-XXXXX' : '000000'}
            autoFocus
            aria-invalid={errors.code ? 'true' : 'false'}
            aria-describedby={errors.code ? 'twoFactorCodeError' : undefined}
            style={{ fontSize: '1.25rem', letterSpacing: '0.25rem' }}
            {...register('code', {
              required: useRecoveryCode
                ? 'El código de recuperación es requerido'
                : 'El código es requerido',
              validate: (value) =>
                useRecoveryCode ||
                /^\d{6}$/.test(value.replace(/[\s-]/g, '')) ||
                'El código debe tener 6 dígitos',
            })}
          />
          {errors.code && (
            <div id="twoFactorCodeError" className="invalid-feedback">
              {errors.code.message}
            </div>
          )}
        </div>

        <div className="mb-3 text-center">
          <button className="btn btn-primary" type="submit" disabled={isSubmitting}>
            {isSubmitting ? (
              <>
                <span className="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span>
                Verificando...
              </>
            ) : (
              'Verificar'
            )}
          </button>
        </div>
      </form>

      <div className="d-flex flex-column align-items-center">
        <button
          type="button"
          className="btn btn-link btn-sm text-muted"
          onClick={toggleCodeType}
          disabled={isSubmitting}
        >
          {useRecoveryCode
            ? 'Usar el código de la app de autenticación'
            : 'Usar un código de recuperación'}
        </button>
        <button
          type="button"
          className="btn btn-link btn-sm text-muted"
          onClick={onCancel}
          disabled={isSubmitting}
        >
          Volver al inicio de sesión
        </button>
      </div>
    </>
  );
}
