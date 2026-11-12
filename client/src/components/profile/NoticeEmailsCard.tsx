import { useId, useState } from 'react';
import { useMutation, useQuery, useQueryClient, type UseQueryResult } from '@tanstack/react-query';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage, getApiErrorStatus } from '../../api/errors';

const REQUEST_EMAILS = '/api/v1/appointments/request-emails';
const QUOTE_REPLY_EMAILS = '/api/v1/quotes/reply-emails';
const ORDER_CANCELLATION_EMAILS = '/api/v1/orders/cancellation-emails';
const REVIEW_EMAILS = '/api/v1/reviews/emails';
type NoticePath = typeof REQUEST_EMAILS | typeof QUOTE_REPLY_EMAILS | typeof ORDER_CANCELLATION_EMAILS | typeof REVIEW_EMAILS;

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
  /**
   * Whoever confirms the orders: each online order its customer cancels before it was accepted (Fase 30B) and, since the Fase
   * 31K, the one still waiting to be accepted 15 minutes after it arrived.
   */
  orders?: boolean;
  /** Whoever reads and answers the reviews (owners and admins): each new review (Fase 31K). */
  reviews?: boolean;
  /** The owner's are theirs alone; anyone else's, whoever runs the team may also choose (Fase 29Q). */
  ownsBusiness?: boolean;
}

/**
 * Fases 29N and 29O: whether this person gets an email with what the customers of the business they are in do online. Each
 * notice is their choice, in that business only; until someone makes it, the owners get them. Since the Fase 29Q whoever
 * runs the team may also choose them for anyone but the owner, and the last choice holds; since the Fase 30B, also the
 * online orders their customers cancel (and since the Fase 31K those still waiting), and since the Fase 31K the new reviews.
 * Whoever is not on its team (the platform's admin) sees nothing.
 */
export default function NoticeEmailsCard({
  businessName,
  agenda,
  quotes,
  orders = false,
  reviews = false,
  ownsBusiness = false,
}: NoticeEmailsCardProps) {
  const inBusiness = businessName ? ` de ${businessName}` : '';
  const requests = useNoticeEmails(REQUEST_EMAILS, agenda);
  const replies = useNoticeEmails(QUOTE_REPLY_EMAILS, quotes);
  const cancellations = useNoticeEmails(ORDER_CANCELLATION_EMAILS, orders);
  const newReviews = useNoticeEmails(REVIEW_EMAILS, reviews);
  // A notice the server says is not theirs to choose (403, or 404 off the team) is not shown; any other failure is.
  const rows = [
    agenda && !refused(requests),
    quotes && !refused(replies),
    orders && !refused(cancellations),
    reviews && !refused(newReviews),
  ];
  if (!rows.some(Boolean)) return null;
  const shown = [requests, replies, cancellations, newReviews].filter((_, index) => rows[index]);
  const loaded = shown.map((query) => query.data).filter((notice): notice is NoticeEmails => !!notice);
  const email = loaded.find((notice) => notice.email)?.email;

  return (
    <div className="card">
      <div className="card-body">
        <h4 className="header-title mt-0 mb-3">Avisos por correo</h4>
        {shown.every((query) => query.isPending && !query.isError) ? (
          <p className="text-muted small mb-0" role="status">
            Revisando…
          </p>
        ) : (
          <>
            <p className="text-muted small mb-2">
              Te llegan a <strong>{email || 'tu correo'}</strong>. Solo cambian para ti
              {ownsBusiness ? '.' : '; quien administra el equipo también los puede elegir por ti.'}
            </p>
            {rows[0] && (
              <NoticeEmailSwitch
                path={REQUEST_EMAILS}
                query={requests}
                label="Citas en línea"
                description={`Cada solicitud de cita o de visita${inBusiness}, su recordatorio si sigue sin respuesta y cada cita que un cliente cancela.`}
              />
            )}
            {rows[1] && (
              <NoticeEmailSwitch
                path={QUOTE_REPLY_EMAILS}
                query={replies}
                label="Cotizaciones"
                description={`Cada vez que un cliente acepta o rechaza desde su enlace una cotización${inBusiness}.`}
              />
            )}
            {rows[2] && (
              <NoticeEmailSwitch
                path={ORDER_CANCELLATION_EMAILS}
                query={cancellations}
                label="Pedidos en línea"
                description={`Cada pedido en línea${inBusiness} que lleva 15 minutos sin que nadie lo acepte o lo rechace, que su cliente cancela antes de que lo aceptes, o que alguien más del equipo cancela porque el cliente lo pidió.`}
              />
            )}
            {rows[3] && (
              <NoticeEmailSwitch
                path={REVIEW_EMAILS}
                query={newReviews}
                label="Reseñas"
                description={`Cada reseña nueva que un cliente escribe${inBusiness}, con sus estrellas y su comentario.`}
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

type NoticeQuery = UseQueryResult<NoticeEmails>;

function refused(query: NoticeQuery): boolean {
  const status = query.isError ? getApiErrorStatus(query.error) : undefined;
  return status === 403 || status === 404;
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
  query,
  label,
  description,
}: {
  path: NoticePath;
  query: NoticeQuery;
  label: string;
  description: string;
}) {
  const switchId = useId();
  const descriptionId = useId();
  const queryClient = useQueryClient();
  const [error, setError] = useState('');
  const notice = query.data;
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
          aria-busy={!notice && query.isPending}
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
      {!notice && query.isError && (
        <div className="alert alert-danger small mt-2 mb-0 d-flex align-items-center gap-2" role="alert">
          <span>No se pudo leer esta elección.</span>
          <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => void query.refetch()}>
            Reintentar
          </button>
        </div>
      )}
      {error && (
        <div className="alert alert-danger small mt-2 mb-0" role="alert">
          {error}
        </div>
      )}
    </div>
  );
}
