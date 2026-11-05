import { useId, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';

const REQUEST_EMAILS = '/api/v1/appointments/request-emails';
const requestEmailsKey = ['appointments', 'request-emails'] as const;

/**
 * Fase 29N: whether this person gets an email with each request online of the business they are in. Their own choice, in
 * that business only; until they make it, the owners get them. Whoever is not on its team (the platform's admin) sees nothing.
 */
export default function RequestEmailsCard({ businessName }: { businessName?: string }) {
  const switchId = useId();
  const queryClient = useQueryClient();
  const [error, setError] = useState('');
  const settings = useQuery({
    queryKey: requestEmailsKey,
    queryFn: ({ signal }) => apiRequest('get', REQUEST_EMAILS, { signal }),
  });
  const update = useMutation({
    mutationFn: (enabled: boolean) => apiRequest('put', REQUEST_EMAILS, { body: { enabled } }),
    onSuccess: (saved) => {
      setError('');
      queryClient.setQueryData(requestEmailsKey, saved);
    },
    onError: (err: unknown) => setError(getApiErrorMessage(err, 'No se pudo guardar tu elección.')),
  });

  if (settings.isError) return null;
  const current = settings.data;

  return (
    <div className="card">
      <div className="card-body">
        <h4 className="header-title mt-0 mb-3">Avisos de solicitudes</h4>
        {!current ? (
          <p className="text-muted small mb-0">Revisando…</p>
        ) : (
          <>
            <div className="form-check form-switch">
              <input
                className="form-check-input"
                type="checkbox"
                role="switch"
                id={switchId}
                checked={current.enabled}
                disabled={update.isPending}
                onChange={(event) => update.mutate(event.target.checked)}
              />
              <label className="form-check-label" htmlFor={switchId}>
                Recibir un correo con cada solicitud de cita o de visita
              </label>
            </div>
            <p className="text-muted small mt-2 mb-0">
              Te llega a <strong>{current.email || 'tu correo'}</strong> en cuanto un cliente pide una cita o una visita en
              línea{businessName ? ` en ${businessName}` : ''}. Solo cambia para ti.
            </p>
            {!current.emailConfirmed && (
              <div className="alert alert-warning small mt-2 mb-0">
                Tu correo todavía no está confirmado: no te llegan hasta que lo confirmes.
              </div>
            )}
            {error && (
              <div className="alert alert-danger small mt-2 mb-0" role="alert">
                {error}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
