import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import AuthCard from '../../components/auth/AuthCard';
import { useAuth } from '../../hooks/useAuth';
import { newPasswordPattern } from '../../utils/passwordRules';

interface AcceptInvitationForm {
  newPassword: string;
  confirmPassword: string;
}

// Mirrors the Identity policy configured in the API (6+ characters, upper, lower and digit).

/** The link of an invitation to a team: the person sees who invited them, chooses a password and enters. */
export default function AcceptInvitation() {
  const [searchParams] = useSearchParams();
  const userId = searchParams.get('userId') ?? '';
  const token = searchParams.get('token') ?? '';
  const navigate = useNavigate();
  const { acceptInvitation } = useAuth();
  const [error, setError] = useState('');
  const linkIsComplete = userId.length > 0 && token.length > 0;

  const invitation = useQuery({
    queryKey: ['invitation', userId, token],
    queryFn: () => apiRequest('get', '/api/v1/auth/invitation', { query: { userId, token } }),
    enabled: linkIsComplete,
    retry: false,
    staleTime: Infinity,
  });

  const {
    register,
    handleSubmit,
    getValues,
    formState: { errors, isSubmitting },
  } = useForm<AcceptInvitationForm>();

  const onSubmit = async (data: AcceptInvitationForm) => {
    setError('');
    try {
      await acceptInvitation({ userId, token, newPassword: data.newPassword });
      navigate('/', { replace: true });
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo aceptar la invitación. Pide que te la reenvíen.'));
    }
  };

  const footer = (
    <Link to="/login" className="text-muted fw-bold">¿Ya tienes contraseña? Inicia sesión</Link>
  );

  if (!linkIsComplete || invitation.isError) {
    return (
      <AuthCard title="Invitación" footer={footer}>
        <div className="alert alert-warning mb-0" role="alert">
          {linkIsComplete
            ? getApiErrorMessage(invitation.error, 'La invitación no es válida o ya expiró: pide que te la reenvíen.')
            : 'El enlace está incompleto. Ábrelo tal como te llegó, o pide que te lo reenvíen.'}
        </div>
      </AuthCard>
    );
  }

  if (!invitation.data) {
    return (
      <AuthCard title="Invitación" footer={footer}>
        <p className="text-center text-muted mb-0" role="status">Revisando tu invitación…</p>
      </AuthCard>
    );
  }

  const { firstName, email, businesses } = invitation.data;

  return (
    <AuthCard
      title={`Hola, ${firstName}`}
      subtitle={<>Te invitaron a <strong>{businesses.join(', ')}</strong>. Elige tu contraseña para entrar.</>}
      footer={footer}
    >
      {error && <div className="alert alert-danger" role="alert">{error}</div>}

      <form onSubmit={handleSubmit(onSubmit)} noValidate>
        <div className="mb-3">
          <label htmlFor="invitation-email" className="form-label">Entrarás con este correo</label>
          <input id="invitation-email" className="form-control" value={email} readOnly />
          <div className="form-text">Si no es tuyo, avísale a quien te invitó antes de seguir.</div>
        </div>
        <div className="mb-3">
          <label htmlFor="invitation-password" className="form-label">Contraseña</label>
          <input
            id="invitation-password"
            type="password"
            autoComplete="new-password"
            className={`form-control ${errors.newPassword ? 'is-invalid' : ''}`}
            {...register('newPassword', {
              required: 'Escribe una contraseña.',
              pattern: newPasswordPattern,
            })}
          />
          {errors.newPassword && <div className="invalid-feedback">{errors.newPassword.message}</div>}
        </div>
        <div className="mb-3">
          <label htmlFor="invitation-password-confirm" className="form-label">Repite la contraseña</label>
          <input
            id="invitation-password-confirm"
            type="password"
            autoComplete="new-password"
            className={`form-control ${errors.confirmPassword ? 'is-invalid' : ''}`}
            {...register('confirmPassword', {
              required: 'Repite la contraseña.',
              validate: (value) => value === getValues('newPassword') || 'Las contraseñas no coinciden.',
            })}
          />
          {errors.confirmPassword && <div className="invalid-feedback">{errors.confirmPassword.message}</div>}
        </div>
        <div className="d-grid">
          <button className="btn btn-primary" type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Entrando…' : 'Entrar al negocio'}
          </button>
        </div>
      </form>
    </AuthCard>
  );
}
