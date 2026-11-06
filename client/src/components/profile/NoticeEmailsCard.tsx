import { useId, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';

const REQUEST_EMAILS = '/api/v1/appointments/request-emails';
const QUOTE_REPLY_EMAILS = '/api/v1/quotes/reply-emails';
type NoticePath = typeof REQUEST_EMAILS | typeof QUOTE_REPLY_EMAILS;

interface NoticeEmails {
  enabled: boolean;
  email?: string | null;
  emailConfirmed: boolean;
}

interface NoticeEmailsCardProps {
  businessName?: string;
  /** Whoever answers the requests online (the agenda's permission): each request and each appointment a customer cancels. */
  agenda: boolean;
  /** Whoever works the quotes: each answer a customer gives from a quote's link (Fase 29O). */
  quotes: boolean;
}

/**
 * Fases 29N and 29O: whether this person gets an email with what the customers of the business they are in do online. Each
 * notice is their own choice, in that business only; until they make it, the owners get them. Whoever is not on its team
 * (the platform's admin) sees nothing.
 */
export default function NoticeEmailsCard({ businessName, agenda, quotes }: NoticeEmailsCardProps) {
  const inBusiness = businessName ? ` de ${businessName}` : '';
  const requests = useNoticeEmails(REQUEST_EMAILS, agenda);
  const replies = useNoticeEmails(QUOTE_REPLY_EMAILS, quotes);
  const rows = [agenda && !requests.isError, quotes && !replies.isError];
  if (!rows.some(Boolean)) return null;
  const loaded = [agenda ? requests.data : undefined, quotes ? replies.data : undefined].filter(
    (notice): notice is NoticeEmails => !!notice,
  );
  const email = loaded.find((notice) => notice.email)?.email;

  return (
    <div className="card">
      <div className="card-body">
        <h4 className="header-title mt-0 mb-3">Avisos por correo</h4>
        {loaded.length === 0 ? (
          <p className="text-muted small mb-0">Revisando…</p>
        ) : (
          <>
            <p className="text-muted small mb-2">
              Te llegan a <strong>{email || 'tu correo'}</strong>. Solo cambian para ti.
            </p>
            {rows[0] && (
              <NoticeEmailSwitch
                path={REQUEST_EMAILS}
                notice={requests.data}
                label="Citas en línea"
                description={`Cada solicitud de cita o de visita${inBusiness} y cada cita que un cliente cancela.`}
              />
            )}
            {rows[1] && (
              <NoticeEmailSwitch
                path={QUOTE_REPLY_EMAILS}
                notice={replies.data}
                label="Cotizaciones"
                description={`Cada vez que un cliente acepta o rechaza desde su enlace una cotización${inBusiness}.`}
              />
            )}
            {loaded.some((notice) => !notice.emailConfirmed) && (
              <div className="alert alert-warning small mt-2 mb-0">
                Tu correo todavía no está confirmado: no te llegan hasta que lo confirmes.
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

function noticeKey(path: NoticePath) {
  return ['notice-emails', path] as const;
}

function useNoticeEmails(path: NoticePath, enabled: boolean) {
  return useQuery({
    queryKey: noticeKey(path),
    queryFn: ({ signal }) => apiRequest('get', path, { signal }) as Promise<NoticeEmails>,
    enabled,
  });
}

function NoticeEmailSwitch({
  path,
  notice,
  label,
  description,
}: {
  path: NoticePath;
  notice: NoticeEmails | undefined;
  label: string;
  description: string;
}) {
  const switchId = useId();
  const descriptionId = useId();
  const queryClient = useQueryClient();
  const [error, setError] = useState('');
  const update = useMutation({
    mutationFn: (enabled: boolean) => apiRequest('put', path, { body: { enabled } }) as Promise<NoticeEmails>,
    onSuccess: (saved) => {
      setError('');
      queryClient.setQueryData(noticeKey(path), saved);
    },
    onError: (err: unknown) => setError(getApiErrorMessage(err, 'No se pudo guardar tu elección.')),
  });

  return (
    <div className="mb-2">
      <div className="form-check form-switch">
        <input
          className="form-check-input"
          type="checkbox"
          role="switch"
          id={switchId}
          aria-describedby={descriptionId}
          checked={notice?.enabled ?? false}
          disabled={!notice || update.isPending}
          onChange={(event) => update.mutate(event.target.checked)}
        />
        <label className="form-check-label" htmlFor={switchId}>
          {label}
        </label>
      </div>
      <p className="text-muted small mb-0 ms-4 ps-2" id={descriptionId}>
        {description}
      </p>
      {error && (
        <div className="alert alert-danger small mt-2 mb-0" role="alert">
          {error}
        </div>
      )}
    </div>
  );
}
