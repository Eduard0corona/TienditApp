import { useEffect, useRef, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getApiErrorMessage, getApiErrorStatus } from '../../api/errors';
import { publicQuoteApi, type PublicQuote } from '../../api/quotesApi';
import PageTitle from '../../components/common/PageTitle';
import { QuoteStatus } from '../../types';
import { formatCalendarDate } from '../../utils/dates';
import { formatMoney } from '../../utils/money';
import { quoteFolio, quoteStatusChip, quoteStatusLabel } from '../../utils/quotes';
import { businessWhatsAppUrl } from '../../utils/whatsApp';
import QuoteReplyPanel from './QuoteReplyPanel';

/** What the customer reads about where the quote stands, beyond its chip. */
function standing(quote: PublicQuote): string | null {
  const { validUntil } = quote;
  // The business's day, as the validity («5 de octubre de 2026»): the customer's device may be in another time zone.
  const here = quote.repliedFromLink && quote.repliedOn ? formatCalendarDate(quote.repliedOn) : null;
  switch (quote.status) {
    case QuoteStatus.Sent:
      return quote.canReply
        ? `Vale hasta el ${formatCalendarDate(validUntil)}. Respóndela aquí o escríbele al negocio si tienes dudas.`
        : `Vale hasta el ${formatCalendarDate(validUntil)}. Para aceptarla o preguntar algo, responde al negocio.`;
    case QuoteStatus.Accepted:
      // Fase 29G: answered here, the business already knows and gets in touch.
      return here
        ? `La aceptaste aquí el ${here}. ${quote.businessName} ya lo sabe y se pondrá en contacto contigo para hacer el trabajo.`
        : 'Le dijiste al negocio que la aceptas.';
    case QuoteStatus.Rejected:
      return here ? `Le dijiste aquí que no la aceptas, el ${here}.` : 'Le dijiste al negocio que no la aceptas.';
    case QuoteStatus.Expired:
      return `Venció el ${formatCalendarDate(validUntil)}. Si aún te interesa, pídele al negocio una nueva.`;
    case QuoteStatus.Superseded:
      return 'El negocio la corrigió: esta versión ya no vale.';
    default:
      return null;
  }
}

/**
 * A quote opened with the link the business sent (/cotizacion#link, Fase 29C, ADR 0053), without an account: what it
 * includes, its taxes and total, until when it is valid, and a way to answer the business. The link lives in the fragment,
 * which the browser never sends to a server; the page sends it in the body of its request.
 */
export default function PublicQuotePage() {
  const location = useLocation();
  const token = location.hash.replace(/^#/, '');
  const quoteQuery = useQuery({
    queryKey: ['public', 'quote', token],
    queryFn: ({ signal }) => publicQuoteApi.view(token, signal),
    enabled: token.length > 0,
    retry: false,
  });
  // A link that stopped opening (404, refetched after an answer) shows only why, never the quote it no longer opens; any other
  // failed refresh keeps the quote on screen under its alert.
  const quote = getApiErrorStatus(quoteQuery.error) === 404 ? undefined : quoteQuery.data;
  const folio = quote ? quoteFolio(quote.number, quote.revision) : '';
  // Fase 29G: why the answer was not taken stays on the page of that link, also once the answer can no longer be given there.
  // Each refusal is its own (`attempt`): the same reason again is announced and takes the focus again.
  const [refusal, setRefusal] = useState<{ token: string; message: string; attempt: number } | null>(null);
  const refusalMessage = refusal?.token === token ? refusal.message : null;
  // After answering, or a refusal, the focus goes to what the page says now: the button that had it is gone.
  const focusNext = useRef<'status' | 'refusal' | null>(null);
  const statusRef = useRef<HTMLParagraphElement>(null);
  const refusalRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const target = focusNext.current === 'status' ? statusRef.current : focusNext.current === 'refusal' ? refusalRef.current : null;
    if (!target) return;
    focusNext.current = null;
    target.focus();
  }, [quote, refusal]);
  // To accept it or ask about it while it stands, or to ask for a new one once it expired.
  const contact = quote && (quote.status === QuoteStatus.Sent || quote.status === QuoteStatus.Expired)
    ? businessWhatsAppUrl(quote.businessContactPhone, `Hola, les escribo por la cotización ${folio}.`)
    : null;

  return (
    <div className="cl-page">
      <PageTitle title="Tu cotización" />

      {!token && (
        <div className="card">
          <div className="card-body text-center py-5">
            <i className="ri-file-paper-2-line fs-1 text-muted d-block mb-2" aria-hidden="true"></i>
            <h2 className="h5">Abre el enlace de tu mensaje</h2>
            <p className="text-muted mb-0">Cuando un negocio te manda una cotización, el mensaje trae un enlace para verla.</p>
          </div>
        </div>
      )}
      {quoteQuery.isLoading && (
        <div className="text-center py-5"><div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando</span></div></div>
      )}
      {quoteQuery.error && (
        <div className="alert alert-warning" role="alert">
          {getApiErrorMessage(quoteQuery.error, 'No se pudo cargar tu cotización.')}
        </div>
      )}

      {quote && (
        <div className="card mb-0">
          <div className="card-body">
            <div className="d-flex flex-wrap justify-content-between align-items-start gap-2 mb-3">
              <div>
                <h2 className="h5 mb-0">Cotización {folio}</h2>
                <small className="text-muted">{quote.businessName} · para {quote.customerName}</small>
              </div>
              <span className={quoteStatusChip(quote.status)}>{quoteStatusLabel(quote.status)}</span>
            </div>

            {quote.status === QuoteStatus.Superseded && quote.newerToken ? (
              <div className="alert alert-info small" role="status">
                El negocio la corrigió: esta versión ya no vale.{' '}
                <Link to={{ pathname: '/cotizacion', hash: quote.newerToken }}>Ver la versión nueva</Link>
              </div>
            ) : (
              standing(quote) && <p ref={statusRef} tabIndex={-1} className="small" role="status">{standing(quote)}</p>
            )}
            {refusalMessage && (
              <div key={refusal?.attempt} ref={refusalRef} tabIndex={-1} className="alert alert-warning small" role="alert">
                <strong>Tu respuesta no se envió.</strong> {refusalMessage}
              </div>
            )}
            {quote.response && <p className="small text-muted">Tu mensaje: «{quote.response}»</p>}

            <div className="table-responsive">
              <table className="table table-sm mb-2">
                <thead>
                  <tr><th>Qué incluye</th><th className="text-end">Cantidad</th><th className="text-end">Importe</th></tr>
                </thead>
                <tbody>
                  {quote.lines.map((line, index) => (
                    <tr key={index}>
                      <td>{line.description}<span className="d-block text-muted small">{formatMoney(line.unitPrice)} c/u</span></td>
                      <td className="text-end">{line.quantity}</td>
                      <td className="text-end">{formatMoney(line.total)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <dl className="row small mb-2">
              <dt className="col-7 col-sm-9 text-muted fw-normal text-sm-end">Subtotal</dt>
              <dd className="col-5 col-sm-3 text-end">{formatMoney(quote.subTotal)}</dd>
              <dt className="col-7 col-sm-9 text-muted fw-normal text-sm-end">IVA</dt>
              <dd className="col-5 col-sm-3 text-end">{formatMoney(quote.tax)}</dd>
              <dt className="col-7 col-sm-9 text-sm-end">Total</dt>
              <dd className="col-5 col-sm-3 text-end fw-semibold">{formatMoney(quote.total)}</dd>
              {quote.visitCredit?.applies && (
                <>
                  <dt className="col-7 col-sm-9 text-muted fw-normal text-sm-end">Visita (se descuenta)</dt>
                  <dd className="col-5 col-sm-3 text-end">−{formatMoney(quote.visitCredit.amount)}</dd>
                  <dt className="col-7 col-sm-9 text-sm-end">Por pagar</dt>
                  <dd className="col-5 col-sm-3 text-end fw-semibold">{formatMoney(quote.toPay)}</dd>
                </>
              )}
            </dl>
            <p className="text-muted small mb-0">{quote.pricesIncludeTax ? 'Los precios incluyen IVA.' : 'El IVA se suma a los precios.'}</p>
            {quote.note && <p className="small mt-2 mb-0">{quote.note}</p>}

            {quote.canReply && (
              <QuoteReplyPanel
                key={token}
                quote={quote}
                token={token}
                onAnswered={() => {
                  focusNext.current = 'status';
                  setRefusal(null);
                }}
                onRefused={(message) => {
                  focusNext.current = 'refusal';
                  setRefusal((last) => ({ token, message, attempt: (last?.attempt ?? 0) + 1 }));
                }}
              />
            )}

            {contact && (
              // While the customer can answer here, the business's WhatsApp is for questions: one primary action per screen.
              <a className={quote.canReply ? 'btn btn-link px-0 mt-2' : 'btn btn-primary mt-3'} href={contact} target="_blank" rel="noreferrer">
                <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>{quote.canReply ? 'Preguntar algo al negocio' : 'Responder al negocio'}
              </a>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
