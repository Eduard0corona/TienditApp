import { useEffect, useRef, type ReactNode } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getApiErrorStatus } from '../../api/errors';
import { OnlineCheckoutStatus, OnlinePaymentRefundStatus } from '../../api/generated/enums';
import { publicMenuApi, type OnlineCheckoutStatus as CheckoutStatus } from '../../api/publicMenuApi';
import CopyTrackingLink from '../../components/tracking/CopyTrackingLink';
import { useDocumentTitle } from '../../hooks/useDocumentTitle';
import { formatMoney } from '../../utils/money';
import { onlineCheckoutReturnKey } from '../../utils/onlineCheckouts';
import { clearCart } from '../../utils/savedCart';
import { businessWhatsAppUrl } from '../../utils/whatsApp';
import './PublicMenuPage.css';

/** How often the page asks while Mercado Pago confirms the payment, the first two minutes. */
export const CONFIRM_REFRESH_MS = 4_000;
/** Then less often: the notification or the sweep may still place it. */
export const CONFIRM_SLOW_REFRESH_MS = 10_000;
const CONFIRM_FAST_FOR_MS = 2 * 60_000;
/**
 * A checkout waits for its payment 30 minutes and 10 more for one approved at the last moment: past that, measured from
 * when this page first asked (not with this device's clock against the server's), the page stops asking.
 */
export const CONFIRM_GIVE_UP_MS = 45 * 60_000;

/** What Mercado Pago's return says happened, before the server confirms it: approved, rejected or left without paying. */
function returnedAs(params: URLSearchParams): 'approved' | 'rejected' | 'none' {
  const status = params.get('collection_status') ?? params.get('status');
  if (status === 'approved') return 'approved';
  if (status === 'rejected') return 'rejected';
  return 'none';
}

function refundText(checkout: CheckoutStatus): string {
  switch (checkout.refundStatus) {
    case OnlinePaymentRefundStatus.Completed:
      return 'Ya te devolvimos tu pago completo. Según tu banco puede tardar unos días en verse.';
    case OnlinePaymentRefundStatus.Failed:
      return `${checkout.businessName} te devolverá tu pago. Si tienes dudas, escríbele.`;
    default:
      return 'Te devolvemos tu pago completo a tu tarjeta o cuenta de Mercado Pago.';
  }
}

/**
 * Where Mercado Pago sends the customer back (Fase 24I, ADR 0031): /m/{slug}/pago/{id}. It asks the server, which reads
 * the payment from Mercado Pago, until the order is placed, could not be placed (and its money goes back) or the time to
 * pay ended. The order's tracking link comes only with the return key this device kept when the payment started.
 */
export default function OnlinePaymentReturnPage() {
  const { slug = '', checkoutId = '' } = useParams<{ slug: string; checkoutId: string }>();
  const [searchParams] = useSearchParams();
  const paymentId = searchParams.get('payment_id') ?? searchParams.get('collection_id');
  const returned = returnedAs(searchParams);
  useDocumentTitle('Tu pago');

  // When the page first asked: how long it keeps asking is measured from here.
  const firstAskedAt = useRef<number | null>(null);
  const checkout = useQuery({
    queryKey: ['online-checkout', slug, checkoutId],
    queryFn: ({ signal }) => {
      firstAskedAt.current ??= Date.now();
      return publicMenuApi.confirmOnlineCheckout(slug, checkoutId, {
        paymentId: paymentId && /^\d{1,20}$/.test(paymentId) ? paymentId : null,
        returnKey: onlineCheckoutReturnKey(checkoutId),
      }, signal);
    },
    // Not found is final, and over the limit of asks waiting is what helps.
    retry: (failures, error) => ![404, 429].includes(getApiErrorStatus(error) ?? 0) && failures < 2,
    // While the checkout waits for its payment: the notification or the sweep may place it even after its time to pay.
    refetchInterval: (query) => {
      const data = query.state.data;
      if (!data || data.status !== OnlineCheckoutStatus.AwaitingPayment) return false;
      const asking = Date.now() - (firstAskedAt.current ?? Date.now());
      if (asking >= CONFIRM_GIVE_UP_MS) return false;
      return asking < CONFIRM_FAST_FOR_MS ? CONFIRM_REFRESH_MS : CONFIRM_SLOW_REFRESH_MS;
    },
  });
  const data = checkout.data;

  // Placed: the cart this device kept for the store was ordered.
  const placed = data?.status === OnlineCheckoutStatus.Placed;
  useEffect(() => {
    if (placed) clearCart(slug);
  }, [placed, slug]);

  const backToMenu = <Link className="btn btn-light btn-touch" to={`/m/${slug}`}>Volver al menú</Link>;
  // One live region for every state, so a screen reader announces how the payment ended when it changes.
  const frame = (content: ReactNode, busy = false) => (
    <div className="pm-page">
      <div className="pm-receipt" aria-live="polite" aria-busy={busy || undefined}>{content}</div>
    </div>
  );

  if (checkout.isPending) {
    return frame(
      <>
        <span className="spinner-border text-primary mb-3" role="status" aria-hidden="true"></span>
        <h1 className="h5">Confirmando tu pago…</h1>
      </>,
      true,
    );
  }

  if (!data) {
    const notFound = getApiErrorStatus(checkout.error) === 404;
    return frame(
      <>
        <h1 className="h5">{notFound ? 'No encontramos ese pago' : 'No pudimos revisar tu pago'}</h1>
        <p className="text-muted">
          {notFound
            ? 'Revisa el enlace o vuelve al menú.'
            : 'Revisa tu conexión e inténtalo de nuevo. Si ya pagaste, tu pedido se envía en cuanto se confirme.'}
        </p>
        <div className="pm-receipt-actions d-grid gap-2 mt-3">
          {!notFound && (
            <button type="button" className="btn btn-primary btn-touch" onClick={() => void checkout.refetch()}>Intentar de nuevo</button>
          )}
          {backToMenu}
        </div>
      </>,
    );
  }
  // A refresh that failed keeps what the page already knew.
  const refreshFailed = checkout.isError && (
    <p className="small text-muted mb-3" role="status">No pudimos actualizar cómo va tu pago; lo intentamos de nuevo en un momento.</p>
  );

  const contact = businessWhatsAppUrl(data.contactPhone, data.orderNumber
    ? `Hola, pagué en línea el pedido ${data.orderNumber}.`
    : `Hola ${data.businessName}, pagué en línea un pedido en su menú.`);

  if (data.status === OnlineCheckoutStatus.Placed) {
    return frame(
      <>
        <span className="pm-receipt-icon"><i className="ri-check-line" aria-hidden="true"></i></span>
        <h1 className="h4">¡Pago recibido!</h1>
        <p className="text-muted">
          {data.confirmed
            ? `${data.businessName} ya está preparando tu pedido.`
            : `Tu pedido ya está con ${data.businessName}; lo confirmará en unos minutos.`}
        </p>
        <dl className="pm-receipt-card">
          <div><dt>Folio</dt><dd><strong>{data.orderNumber}</strong></dd></div>
          <div><dt>Pagado en línea</dt><dd><strong className="ta-money">{formatMoney(data.amount)}</strong></dd></div>
        </dl>
        {data.linkedToAccount && <p className="small mb-0">Lo encuentras en tus pedidos.</p>}
        {data.trackingToken && !data.linkedToAccount && (
          <p className="small mb-0">Guarda el enlace de seguimiento para ver cómo va tu pedido.</p>
        )}
        <div className="pm-receipt-actions d-grid gap-2 mt-3">
          {data.trackingToken && (
            <Link className="btn btn-primary btn-touch" to={`/seguimiento#${data.trackingToken}`}>
              <i className="ri-route-line me-1" aria-hidden="true"></i>Seguir tu pedido
            </Link>
          )}
          {data.trackingToken && !data.linkedToAccount && <CopyTrackingLink token={data.trackingToken} />}
          {data.linkedToAccount && <Link className="btn btn-light btn-touch" to="/mis-pedidos">Ver mis pedidos</Link>}
          {backToMenu}
        </div>
        {!data.trackingToken && !data.linkedToAccount && (
          <p className="small text-muted mt-3 mb-0">Te avisamos por WhatsApp cómo va tu pedido.</p>
        )}
      </>,
    );
  }

  if (data.status === OnlineCheckoutStatus.NotPlaced) {
    return frame(
      <>
        <span className="pm-receipt-icon pm-receipt-icon-warning"><i className="ri-refund-2-line" aria-hidden="true"></i></span>
        <h1 className="h4">No pudimos enviar tu pedido</h1>
        {data.failureMessage && <p className="text-muted">{data.failureMessage}</p>}
        <p className="fw-semibold">{refundText(data)}</p>
        <div className="pm-receipt-actions d-grid gap-2 mt-3">
          {contact && (
            <a className="btn btn-soft-success btn-touch" href={contact} target="_blank" rel="noopener noreferrer">
              <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Escribir al negocio
            </a>
          )}
          {backToMenu}
        </div>
      </>,
    );
  }

  if (data.status === OnlineCheckoutStatus.Expired) {
    return frame(
      <>
        <h1 className="h4">Se acabó el tiempo para pagar</h1>
        <p className="text-muted">No recibimos tu pago y no se te cobró nada. Tu pedido sigue en el menú para que lo intentes otra vez.</p>
        <div className="pm-receipt-actions d-grid gap-2 mt-3">{backToMenu}</div>
      </>,
    );
  }

  // Still to pay: what Mercado Pago's return said, while the server confirms it. Its page comes only while the server
  // says there is time to pay it.
  return frame(
    <>
      {refreshFailed}
      {returned === 'approved' ? (
        <>
          <span className="spinner-border text-primary mb-3" role="status" aria-hidden="true"></span>
          <h1 className="h5">Confirmando tu pago…</h1>
          <p className="text-muted">Mercado Pago ya lo aprobó; en un momento enviamos tu pedido a {data.businessName}.</p>
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
        <div><dt>Total</dt><dd><strong className="ta-money">{formatMoney(data.amount)}</strong></dd></div>
      </dl>
      <div className="pm-receipt-actions d-grid gap-2 mt-3">
        {data.checkoutUrl && returned !== 'approved' && (
          <a className="btn btn-primary btn-touch" href={data.checkoutUrl}>
            {returned === 'rejected' ? 'Intentar de nuevo' : 'Pagar en Mercado Pago'}
          </a>
        )}
        {backToMenu}
      </div>
    </>,
  );
}
