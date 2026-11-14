import { useRef, type ReactNode } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getApiErrorStatus } from '../../api/errors';
import { OnlineCheckoutStatus, OnlinePaymentRefundStatus } from '../../api/generated/enums';
import { publicMenuApi, type AppointmentDepositStatus } from '../../api/publicMenuApi';
import CopyTrackingLink from '../../components/tracking/CopyTrackingLink';
import { useDocumentTitle } from '../../hooks/useDocumentTitle';
import { formatMoney } from '../../utils/money';
import { onlineCheckoutReturnKey } from '../../utils/onlineCheckouts';
import { bookingPath, upperFirst } from '../../utils/onlineBooking';
import { businessWhatsAppUrl } from '../../utils/whatsApp';
import { CONFIRM_GIVE_UP_MS, CONFIRM_REFRESH_MS, CONFIRM_SLOW_REFRESH_MS } from './OnlinePaymentReturnPage';
import './PublicMenuPage.css';

/** What Mercado Pago's return says happened, before the server confirms it: approved, rejected or left without paying. */
function returnedAs(params: URLSearchParams): 'approved' | 'rejected' | 'none' {
  const status = params.get('collection_status') ?? params.get('status');
  if (status === 'approved') return 'approved';
  if (status === 'rejected') return 'rejected';
  return 'none';
}

function refundText(deposit: AppointmentDepositStatus): string {
  switch (deposit.refundStatus) {
    case OnlinePaymentRefundStatus.Completed:
      return 'Ya te devolvimos tu anticipo completo. Según tu banco puede tardar unos días en verse.';
    case OnlinePaymentRefundStatus.Failed:
      return `${deposit.businessName} te devolverá tu anticipo. Si tienes dudas, escríbele.`;
    default:
      return 'Te devolvemos tu anticipo completo a tu tarjeta o cuenta de Mercado Pago.';
  }
}

/**
 * Where Mercado Pago sends the customer back after paying the deposit to book (Fase 31O, ADR 0095): /m/{slug}/cita/pago/{id}.
 * It asks the server, which reads the payment from Mercado Pago, until the request is booked (with its link, only with the
 * return key this device kept when it went to pay), could not be (and its money goes back) or the time to pay ended.
 */
export default function AppointmentDepositReturnPage() {
  const { slug = '', checkoutId = '' } = useParams<{ slug: string; checkoutId: string }>();
  const [searchParams] = useSearchParams();
  const paymentId = searchParams.get('payment_id') ?? searchParams.get('collection_id');
  const returned = returnedAs(searchParams);
  useDocumentTitle('Tu anticipo');

  // When the page first asked: how long it keeps asking is measured from here.
  const firstAskedAt = useRef<number | null>(null);
  const deposit = useQuery({
    queryKey: ['appointment-deposit', slug, checkoutId],
    queryFn: ({ signal }) => {
      firstAskedAt.current ??= Date.now();
      return publicMenuApi.confirmAppointmentDeposit(slug, checkoutId, {
        paymentId: paymentId && /^\d{1,20}$/.test(paymentId) ? paymentId : null,
        returnKey: onlineCheckoutReturnKey(checkoutId),
      }, signal);
    },
    // Not found is final, and over the limit of asks waiting is what helps.
    retry: (failures, error) => ![404, 429].includes(getApiErrorStatus(error) ?? 0) && failures < 2,
    // While it waits for its payment: the notification or the sweep may book it even after its time to pay.
    refetchInterval: (query) => {
      const data = query.state.data;
      if (!data || data.status !== OnlineCheckoutStatus.AwaitingPayment) return false;
      const asking = Date.now() - (firstAskedAt.current ?? Date.now());
      if (asking >= CONFIRM_GIVE_UP_MS) return false;
      return asking < 2 * 60_000 ? CONFIRM_REFRESH_MS : CONFIRM_SLOW_REFRESH_MS;
    },
  });
  const data = deposit.data;

  const backToBooking = <Link className="btn btn-light btn-touch" to={bookingPath(slug)}>Volver a agendar</Link>;
  // One live region for every state, so a screen reader announces how the payment ended when it changes.
  const frame = (content: ReactNode, busy = false) => (
    <div className="pm-page">
      <div className="pm-receipt" aria-live="polite" aria-busy={busy || undefined}>{content}</div>
    </div>
  );

  if (deposit.isPending) {
    return frame(
      <>
        <span className="spinner-border text-primary mb-3" role="status" aria-hidden="true"></span>
        <h1 className="h5">Confirmando tu anticipo…</h1>
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
            ? 'Revisa el enlace o vuelve a la página para agendar.'
            : 'Revisa tu conexión e inténtalo de nuevo. Si ya pagaste, tu solicitud llega al negocio en cuanto se confirme.'}
        </p>
        <div className="pm-receipt-actions d-grid gap-2 mt-3">
          {!notFound && (
            <button type="button" className="btn btn-primary btn-touch" onClick={() => void deposit.refetch()}>Intentar de nuevo</button>
          )}
          {backToBooking}
        </div>
      </>,
    );
  }
  // A refresh that failed keeps what the page already knew.
  const refreshFailed = deposit.isError && (
    <p className="small text-muted mb-3" role="status">No pudimos actualizar cómo va tu pago; lo intentamos de nuevo en un momento.</p>
  );
  const contact = businessWhatsAppUrl(data.contactPhone, `Hola ${data.businessName}, pagué en línea el anticipo de una cita.`);

  if (data.status === OnlineCheckoutStatus.Placed) {
    const receipt = data.receipt;
    return frame(
      <>
        <span className="pm-receipt-icon"><i className="ri-check-line" aria-hidden="true"></i></span>
        <h1 className="h4">¡Anticipo pagado!</h1>
        <p className="text-muted">Tu solicitud ya está con {data.businessName}, que te confirma por WhatsApp.</p>
        <dl className="pm-receipt-card">
          {receipt && (
            <>
              <div><dt>Servicio</dt><dd><strong>{receipt.serviceName}</strong></dd></div>
              <div><dt>Cuándo</dt><dd>{upperFirst(receipt.dateLabel)} · {receipt.timeLabel}</dd></div>
              {receipt.staffName && <div><dt>Te atiende</dt><dd>{receipt.staffName}</dd></div>}
            </>
          )}
          <div><dt>Anticipo pagado</dt><dd><strong className="ta-money">{formatMoney(data.amount)}</strong></dd></div>
        </dl>
        <p className="small text-muted">Se descuenta al cobrarte la cita.</p>
        {receipt && !receipt.linkedToAccount && <p className="small mb-0">Guarda el enlace de tu cita para verla o cancelarla.</p>}
        {receipt?.linkedToAccount && <p className="small mb-0">La encuentras en tus citas.</p>}
        <div className="pm-receipt-actions d-grid gap-2 mt-3">
          {receipt && (
            <Link className="btn btn-primary btn-touch" to={`/cita#${receipt.trackingToken}`}>
              <i className="ri-calendar-event-line me-1" aria-hidden="true"></i>Ver tu cita
            </Link>
          )}
          {receipt && !receipt.linkedToAccount && <CopyTrackingLink token={receipt.trackingToken} path="/cita" />}
          {receipt?.linkedToAccount && <Link className="btn btn-light btn-touch" to="/mis-citas">Ver mis citas</Link>}
          <Link className="btn btn-light btn-touch" to={`/m/${slug}`}>Volver al menú</Link>
        </div>
        {!receipt && (
          <p className="small text-muted mt-3 mb-0">Te avisamos por WhatsApp; el enlace de tu cita llega en ese mensaje.</p>
        )}
      </>,
    );
  }

  if (data.status === OnlineCheckoutStatus.NotPlaced) {
    return frame(
      <>
        <span className="pm-receipt-icon pm-receipt-icon-warning"><i className="ri-refund-2-line" aria-hidden="true"></i></span>
        <h1 className="h4">No pudimos reservar tu cita</h1>
        {data.failureMessage && <p className="text-muted">{data.failureMessage}</p>}
        <p className="fw-semibold">{refundText(data)}</p>
        <div className="pm-receipt-actions d-grid gap-2 mt-3">
          <Link className="btn btn-primary btn-touch" to={bookingPath(slug)}>Elegir otra hora</Link>
          {contact && (
            <a className="btn btn-soft-success btn-touch" href={contact} target="_blank" rel="noopener noreferrer">
              <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Escribir al negocio
            </a>
          )}
        </div>
      </>,
    );
  }

  if (data.status === OnlineCheckoutStatus.Expired) {
    return frame(
      <>
        <h1 className="h4">Se acabó el tiempo para pagar</h1>
        <p className="text-muted">No recibimos tu anticipo y no se te cobró nada. Elige tu hora otra vez para reservarla.</p>
        <div className="pm-receipt-actions d-grid gap-2 mt-3">{backToBooking}</div>
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
          <h1 className="h5">Confirmando tu anticipo…</h1>
          <p className="text-muted">Mercado Pago ya lo aprobó; en un momento tu solicitud llega a {data.businessName}.</p>
        </>
      ) : (
        <>
          <h1 className="h5">{returned === 'rejected' ? 'Tu pago no se aprobó' : 'Aún no recibimos tu anticipo'}</h1>
          <p className="text-muted">
            {returned === 'rejected'
              ? 'No se te cobró nada. Puedes intentarlo con otra tarjeta o con tu cuenta de Mercado Pago.'
              : 'Si ya pagaste, en un momento se confirma aquí. Si no, puedes pagar ahora: tu hora no queda apartada mientras tanto.'}
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
        {backToBooking}
      </div>
    </>,
  );
}
