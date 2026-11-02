import { useId, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage, getApiErrorStatus } from '../../api/errors';
import { publicQuoteApi, type PublicQuote } from '../../api/quotesApi';
import { formatMoney } from '../../utils/money';
import { quoteFolio } from '../../utils/quotes';

export const MAX_REPLY_LENGTH = 500;

type Choice = 'accept' | 'reject';

interface QuoteReplyPanelProps {
  quote: PublicQuote;
  token: string;
}

/**
 * The customer accepts or rejects the quote from its link (Fase 29G, ADR 0061): they pick, read what it means and may tell
 * the business something before sending it. The quote comes back as it stands and replaces the one on screen; a refusal
 * (the business changed it meanwhile, or turned the answers off) brings the page up to date and says why.
 */
export default function QuoteReplyPanel({ quote, token }: QuoteReplyPanelProps) {
  const queryClient = useQueryClient();
  const headingId = useId();
  const messageId = useId();
  const [choice, setChoice] = useState<Choice | null>(null);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const folio = quoteFolio(quote.number, quote.revision);

  const reply = useMutation({
    mutationFn: (accepted: boolean) => publicQuoteApi.reply(token, accepted, message.trim() || null),
    onSuccess: (answered) => {
      queryClient.setQueryData(['public', 'quote', token], answered);
    },
    onError: (err) => {
      setError(getApiErrorMessage(err, 'No se pudo enviar tu respuesta. Inténtalo otra vez.'));
      // The quote changed meanwhile (an answer, a correction, the answers turned off): the page shows how it stands now.
      const status = getApiErrorStatus(err);
      if (status !== undefined && status < 500)
        void queryClient.invalidateQueries({ queryKey: ['public', 'quote', token] });
    },
  });

  const pick = (next: Choice | null) => {
    setChoice(next);
    setError('');
  };

  return (
    <section className="border-top pt-3 mt-3" aria-labelledby={headingId}>
      <h3 id={headingId} className="h6">¿Qué le respondes a {quote.businessName}?</h3>
      {choice === null ? (
        <div className="d-flex flex-wrap gap-2">
          <button type="button" className="btn btn-primary" onClick={() => pick('accept')}>
            <i className="ri-check-line me-1" aria-hidden="true"></i>Aceptar la cotización
          </button>
          <button type="button" className="btn btn-outline-secondary" onClick={() => pick('reject')}>
            No la acepto
          </button>
        </div>
      ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            setError('');
            reply.mutate(choice === 'accept');
          }}
        >
          <p className="small mb-2">
            {choice === 'accept'
              ? `Aceptas la cotización ${folio} por ${formatMoney(quote.toPay)}${quote.visitCredit?.applies ? ', ya con la visita descontada' : ''}. ${quote.businessName} recibe tu respuesta y se pone en contacto contigo para hacer el trabajo.`
              : `${quote.businessName} recibe que no la aceptas. Si quieres, dile por qué: puede mandarte otra versión.`}
          </p>
          <label className="form-label small mb-1" htmlFor={messageId}>Mensaje para {quote.businessName} (opcional)</label>
          <textarea
            id={messageId}
            className="form-control form-control-sm"
            rows={3}
            maxLength={MAX_REPLY_LENGTH}
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            placeholder={choice === 'accept' ? 'Por ejemplo, qué días te acomodan' : 'Por ejemplo, qué te gustaría cambiar'}
          />
          {error && <div className="alert alert-danger small mt-2 mb-0" role="alert">{error}</div>}
          <div className="d-flex flex-wrap gap-2 mt-2">
            <button type="submit" className="btn btn-primary" disabled={reply.isPending}>
              {reply.isPending && <span className="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>}
              {choice === 'accept' ? 'Sí, la acepto' : 'Enviar mi respuesta'}
            </button>
            <button type="button" className="btn btn-light" onClick={() => pick(null)} disabled={reply.isPending}>
              Cancelar
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
