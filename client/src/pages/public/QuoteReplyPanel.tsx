import { useEffect, useId, useRef, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage, getApiErrorStatus } from '../../api/errors';
import { publicQuoteApi, type PublicQuote } from '../../api/quotesApi';
import { formatMoney } from '../../utils/money';
import { goToExternalPage } from '../../utils/externalNavigation';
import { rememberQuoteDeposit } from '../../utils/quoteDeposits';
import { quoteFolio } from '../../utils/quotes';

export const MAX_REPLY_LENGTH = 500;

type Choice = 'accept' | 'reject';

interface QuoteReplyPanelProps {
  quote: PublicQuote;
  token: string;
  /** The answer was taken: the quote on screen is the one that came back. */
  onAnswered: () => void;
  /** It was refused with this reason; the page keeps it after this panel goes away, and shows the quote as it stands now. */
  onRefused: (message: string) => void;
}

/**
 * The customer accepts or rejects the quote from its link (Fase 29G, ADR 0061): they pick, read what it means and may tell
 * the business something before sending it. The quote comes back as it stands and replaces the one on screen; a refusal
 * (the business changed it meanwhile, or turned the answers off) brings the page up to date and the page says why. A link
 * that stopped opening only refetches: the page says that one. A version that asks a deposit (Fase 31N, ADR 0094) is accepted
 * by paying it: the customer goes to Mercado Pago, and this device keeps the link to come back to it.
 */
export default function QuoteReplyPanel({ quote, token, onAnswered, onRefused }: QuoteReplyPanelProps) {
  const queryClient = useQueryClient();
  const headingId = useId();
  const messageId = useId();
  const counterId = useId();
  const [choice, setChoice] = useState<Choice | null>(null);
  const [message, setMessage] = useState('');
  const folio = quoteFolio(quote.number, quote.revision);
  // The focus follows what replaces the button that had it: the message (or «Ir a pagar») when the form opens, the button picked
  // on «Cancelar».
  const messageRef = useRef<HTMLTextAreaElement>(null);
  const payRef = useRef<HTMLButtonElement>(null);
  const acceptRef = useRef<HTMLButtonElement>(null);
  const rejectRef = useRef<HTMLButtonElement>(null);
  const returnTo = useRef<Choice | null>(null);
  useEffect(() => {
    if (choice) {
      // The message, or «Ir a pagar» when accepting is paying the deposit (no message goes with it).
      (messageRef.current ?? payRef.current)?.focus();
      return;
    }
    if (returnTo.current) (returnTo.current === 'accept' ? acceptRef : rejectRef).current?.focus();
    returnTo.current = null;
  }, [choice]);

  const deposit = quote.depositAmount ?? null;
  // The deposit can stop being payable while its form is open (the business's account stopped collecting, and refusing to
  // start it read the quote again): then only the rejection stays, and the refusal keeps the focus.
  const shown = choice === 'accept' && deposit !== null && !quote.canPayDeposit ? null : choice;

  const payDeposit = useMutation({
    mutationFn: () => publicQuoteApi.startDeposit(token),
    onSuccess: (started) => {
      rememberQuoteDeposit(started.checkoutId, token);
      goToExternalPage(started.checkoutUrl);
    },
    onError: (err) => {
      const status = getApiErrorStatus(err);
      if (status !== 404) onRefused(getApiErrorMessage(err, 'Revisa tu conexión e inténtalo otra vez.'));
      if (status !== undefined && status < 500)
        void queryClient.invalidateQueries({ queryKey: ['public', 'quote', token] });
    },
  });

  const reply = useMutation({
    mutationFn: (accepted: boolean) => publicQuoteApi.reply(token, accepted, message.trim() || null),
    onSuccess: (answered) => {
      onAnswered();
      queryClient.setQueryData(['public', 'quote', token], answered);
    },
    onError: (err) => {
      const status = getApiErrorStatus(err);
      // A link that stopped opening: the page says so once it reads it again.
      if (status !== 404) onRefused(getApiErrorMessage(err, 'Revisa tu conexión e inténtalo otra vez.'));
      // The quote changed meanwhile (an answer, a correction, the answers turned off): the page shows how it stands now.
      if (status !== undefined && status < 500)
        void queryClient.invalidateQueries({ queryKey: ['public', 'quote', token] });
    },
  });

  const cancel = () => {
    returnTo.current = choice;
    setChoice(null);
  };

  return (
    <section className="border-top pt-3 mt-3" aria-labelledby={headingId}>
      <h3 id={headingId} className="h6">¿Qué le respondes a {quote.businessName}?</h3>
      {deposit !== null && !quote.canPayDeposit && (
        <p className="small">
          Para aceptarla, {quote.businessName} pide un anticipo de {formatMoney(deposit)} que por ahora no se puede pagar en
          línea: escríbele para aceptarla.
        </p>
      )}
      {shown === null ? (
        <div className="d-flex flex-wrap gap-2">
          {(deposit === null || quote.canPayDeposit) && (
            <button ref={acceptRef} type="button" className="btn btn-primary" onClick={() => setChoice('accept')}>
              <i className="ri-check-line me-1" aria-hidden="true"></i>
              {deposit === null ? 'Aceptar la cotización' : `Aceptar y pagar el anticipo de ${formatMoney(deposit)}`}
            </button>
          )}
          <button ref={rejectRef} type="button" className="btn btn-outline-secondary" onClick={() => setChoice('reject')}>
            No la acepto
          </button>
        </div>
      ) : shown === 'accept' && deposit !== null ? (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            payDeposit.mutate();
          }}
        >
          <p className="small mb-2">
            Para aceptar la cotización {folio} pagas en línea su anticipo de {formatMoney(deposit)} con Mercado Pago, a la cuenta
            de {quote.businessName}; lo demás se lo pagas como acuerden. Cuando el pago se aprueba, la cotización queda aceptada.
          </p>
          <div className="d-flex flex-wrap gap-2">
            <button ref={payRef} type="submit" className="btn btn-primary" disabled={payDeposit.isPending}>
              {payDeposit.isPending && <span className="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>}
              Ir a pagar {formatMoney(deposit)}
            </button>
            <button type="button" className="btn btn-light" onClick={cancel} disabled={payDeposit.isPending}>
              Cancelar
            </button>
          </div>
        </form>
      ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            reply.mutate(choice === 'accept');
          }}
        >
          <p className="small mb-2">
            {choice === 'accept'
              ? `Aceptas la cotización ${folio} por ${formatMoney(quote.toPay)}${quote.visitCredit?.applies ? ', ya con la visita descontada' : ''}. ${quote.businessName} recibe tu respuesta y se pondrá en contacto contigo para hacer el trabajo.`
              : `${quote.businessName} sabrá que no la aceptas. Si quieres, dile por qué: puede mandarte otra versión.`}
          </p>
          <label className="form-label small mb-1" htmlFor={messageId}>Mensaje para {quote.businessName} (opcional)</label>
          <textarea
            ref={messageRef}
            id={messageId}
            aria-describedby={counterId}
            className="form-control form-control-sm"
            rows={3}
            maxLength={MAX_REPLY_LENGTH}
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            placeholder={choice === 'accept' ? 'Por ejemplo, qué días te acomodan' : 'Por ejemplo, qué te gustaría cambiar'}
          />
          <div id={counterId} className="form-text text-end">{message.length} de {MAX_REPLY_LENGTH} caracteres</div>
          <div className="d-flex flex-wrap gap-2 mt-2">
            <button type="submit" className="btn btn-primary" disabled={reply.isPending}>
              {reply.isPending && <span className="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>}
              {choice === 'accept' ? 'Sí, la acepto' : 'Enviar mi respuesta'}
            </button>
            <button type="button" className="btn btn-light" onClick={cancel} disabled={reply.isPending}>
              Cancelar
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
