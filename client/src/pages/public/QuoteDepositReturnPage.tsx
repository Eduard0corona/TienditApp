import { useRef, type ReactNode } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getApiErrorStatus } from '../../api/errors';
import { OnlineCheckoutStatus, OnlinePaymentRefundStatus } from '../../api/generated/enums';
import { publicQuoteApi, type QuoteDepositStatus } from '../../api/quotesApi';
import { useDocumentTitle } from '../../hooks/useDocumentTitle';
import { formatMoney } from '../../utils/money';
import { quoteDepositToken } from '../../utils/quoteDeposits';
import { CONFIRM_GIVE_UP_MS, CONFIRM_REFRESH_MS, CONFIRM_SLOW_REFRESH_MS } from './OnlinePaymentReturnPage';
import './PublicMenuPage.css';

const CONFIRM_FAST_FOR_MS = 2 * 60_000;

/** What Mercado Pago's return says happened, before the server confirms it: approved, rejected or left without paying. */
function returnedAs(params: URLSearchParams): 'approved' | 'rejected' | 'none' {
  const status = params.get('collection_status') ?? params.get('status');
  if (status === 'approved') return 'approved';
  if (status === 'rejected') return 'rejected';
  return 'none';
}

function refundText(deposit: QuoteDepositStatus): string {
  switch (deposit.refundStatus) {
    case OnlinePaymentRefundStatus.Completed:
      return 'Ya te devolvimos tu pago completo. Según tu banco puede tardar unos días en verse.';
    case OnlinePaymentRefundStatus.Failed:
      return 'El negocio te devolverá tu pago. Si tienes dudas, escríbele.';
    default:
      return 'Te devolvemos tu pago completo a tu tarjeta o cuenta de Mercado Pago.';
  }
}

/**
 * Where Mercado Pago sends the customer back after paying the deposit of a quote (Fase 31N, ADR 0094):
 * /cotizacion/pago/{id}. With the link this device kept when it went to pay, it asks the server, which reads the payment
 * from Mercado Pago, until the deposit accepted the quote, could not (and its money goes back) or the time to pay ended.
 * Without that link (another device, storage blocked) it cannot ask: the payment is applied all the same, and the quote's
 * link says how it went.
 */
export default function QuoteDepositReturnPage() {
  const { checkoutId = '' } = useParams<{ checkoutId: string }>();
  const [searchParams] = useSearchParams();
  const paymentId = searchParams.get('payment_id') ?? searchParams.get('collection_id');
  const returned = returnedAs(searchParams);
  const token = quoteDepositToken(checkoutId);
  useDocumentTitle('Tu anticipo');

  // When the page first asked: how long it keeps asking is measured from here.
  const firstAskedAt = useRef<number | null>(null);
  const deposit = useQuery({
    queryKey: ['quote-deposit', checkoutId, token],
    queryFn: ({ signal }) => {
      firstAskedAt.current ??= Date.now();
      return publicQuoteApi.confirmDeposit(token!, checkoutId, paymentId && /^\d{1,20}$/.test(paymentId) ? paymentId : null, signal);
    },
    enabled: token !== null,
    // Not found is final, and over the limit of asks waiting is what helps.
    retry: (failures, error) => ![404, 429].includes(getApiErrorStatus(error) ?? 0) && failures < 2,
    // While it waits for its payment: the notification or the sweep may apply it even after its time to pay.
    refetchInterval: (query) => {
      const data = query.state.data;
      if (!data || data.status !== OnlineCheckoutStatus.AwaitingPayment) return false;
      const asking = Date.now() - (firstAskedAt.current ?? Date.now());
      if (asking >= CONFIRM_GIVE_UP_MS) return false;
      return asking < CONFIRM_FAST_FOR_MS ? CONFIRM_REFRESH_MS : CONFIRM_SLOW_REFRESH_MS;
    },
  });
  const data = deposit.data;

  const backToQuote = token
    ? <Link className="btn btn-light btn-touch" to={{ pathname: '/cotizacion', hash: token }}>Ver tu cotización</Link>
    : null;
  // One live region for every state, so a screen reader announces how the payment ended when it changes.
  const frame = (content: ReactNode, busy = false) => (
    <div className="pm-page">
      <div className="pm-receipt" aria-live="polite" aria-busy={busy || undefined}>{content}</div>
    </div>
  );

  if (!token) {
    return frame(
      <>
        <h1 className="h5">Abre otra vez el enlace de tu cotización</h1>
        <p className="text-muted mb-0">
          Si pagaste el anticipo, en unos minutos tu cotización aparece aceptada en el enlace que te mandó el negocio. Si el
          pago no se aprueba, no se te cobra nada.
        </p>
      </>,
    );
  }

  if (deposit.isPending) {
    return frame(
      <>
        <span className="spinner-border text-primary mb-3" role="status" aria-hidden="true"></span>
        <h1 className="h5">Confirmando tu pago…</h1>
      </>,
      true,
    );
  }

  if (!data) {
    const notFound = getApiErrorStatus(deposit.error) === 404;
    return frame(
      <>
        <h1 className="h5">{notFound ? 'No encontramos ese pago' : 'No pudimos revisar tu pago'}</h1>
        <p className="text-muted">
          {notFound
            ? 'Abre otra vez el enlace de tu cotización para ver cómo va.'
            : 'Revisa tu conexión e inténtalo de nuevo. Si ya pagaste, tu cotización queda aceptada en cuanto se confirme.'}
        </p>
        <div className="pm-receipt-actions d-grid gap-2 mt-3">
          {!notFound && (
            <button type="button" className="btn btn-primary btn-touch" onClick={() => void deposit.refetch()}>Intentar de nuevo</button>
          )}
          {backToQuote}
        </div>
      </>,
    );
  }
  // A refresh that failed keeps what the page already knew.
  const refreshFailed = deposit.isError && (
    <p className="small text-muted mb-3" role="status">No pudimos actualizar cómo va tu pago; lo intentamos de nuevo en un momento.</p>
  );

  if (data.status === OnlineCheckoutStatus.Placed) {
    return frame(
      <>
        <span className="pm-receipt-icon"><i className="ri-check-line" aria-hidden="true"></i></span>
        <h1 className="h4">¡Anticipo pagado!</h1>
        <p className="text-muted">Tu cotización quedó aceptada. El negocio ya lo sabe y se pondrá en contacto contigo para hacer el trabajo.</p>
        <dl className="pm-receipt-card">
          {data.orderNumber && <div><dt>Orden</dt><dd><strong>{data.orderNumber}</strong></dd></div>}
          <div><dt>Pagado en línea</dt><dd><strong className="ta-money">{formatMoney(data.amount)}</strong></dd></div>
        </dl>
        <div className="pm-receipt-actions d-grid gap-2 mt-3">{backToQuote}</div>
      </>,
    );
  }

  if (data.status === OnlineCheckoutStatus.NotPlaced) {
    return frame(
      <>
        <span className="pm-receipt-icon pm-receipt-icon-warning"><i className="ri-refund-2-line" aria-hidden="true"></i></span>
        <h1 className="h4">Tu pago no aceptó la cotización</h1>
        {data.failureMessage && <p className="text-muted">{data.failureMessage}</p>}
        <p className="fw-semibold">{refundText(data)}</p>
        <div className="pm-receipt-actions d-grid gap-2 mt-3">{backToQuote}</div>
      </>,
    );
  }

  if (data.status === OnlineCheckoutStatus.Expired) {
    return frame(
      <>
        <h1 className="h4">Se acabó el tiempo para pagar</h1>
        <p className="text-muted">No recibimos tu pago y no se te cobró nada. Puedes intentarlo otra vez desde tu cotización mientras siga vigente.</p>
        <div className="pm-receipt-actions d-grid gap-2 mt-3">{backToQuote}</div>
      </>,
    );
  }

  // Still to pay: what Mercado Pago's return said, while the server confirms it. Its page comes only while the server says
  // there is time to pay it.
  return frame(
    <>
      {refreshFailed}
      {returned === 'approved' ? (
        <>
          <span className="spinner-border text-primary mb-3" role="status" aria-hidden="true"></span>
          <h1 className="h5">Confirmando tu pago…</h1>
          <p className="text-muted">Mercado Pago ya lo aprobó; en un momento tu cotización queda aceptada.</p>
        </>
      ) : (
        <>
          <h1 className="h5">{returned === 'rejected' ? 'Tu pago no se aprobó' : 'Aún no recibimos tu pago'}</h1>
          <p className="text-muted">
            {returned === 'rejected'
              ? 'No se te cobró nada. Puedes intentarlo con otra tarjeta o con tu cuenta de Mercado Pago.'
              : 'Si ya pagaste, en un momento se confirma aquí. Si no, puedes pagar ahora.'}
          </p>
        </>
      )}
      <dl className="pm-receipt-card">
        <div><dt>Anticipo</dt><dd><strong className="ta-money">{formatMoney(data.amount)}</strong></dd></div>
      </dl>
      <div className="pm-receipt-actions d-grid gap-2 mt-3">
        {data.checkoutUrl && returned !== 'approved' && (
          <a className="btn btn-primary btn-touch" href={data.checkoutUrl}>
            {returned === 'rejected' ? 'Intentar de nuevo' : 'Pagar en Mercado Pago'}
          </a>
        )}
        {backToQuote}
      </div>
    </>,
  );
}
