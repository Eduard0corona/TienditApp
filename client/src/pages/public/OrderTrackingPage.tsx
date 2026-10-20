import { Link, useLocation } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { publicMenuApi } from '../../api/publicMenuApi';
import PageTitle from '../../components/common/PageTitle';
import OrderItemsSummary from '../../components/tracking/OrderItemsSummary';
import OrderTimeline, { OrderStepChip } from '../../components/tracking/OrderTimeline';
import { OrderTrackingStep } from '../../types';
import { formatDateTime } from '../../utils/dates';
import { trackingRefresh } from '../../utils/orderTracking';
import { businessWhatsAppUrl } from '../../utils/whatsApp';

/**
 * An online order followed with the link of its receipt (/seguimiento#link, Fase 24C), without an account. The link lives
 * in the fragment, which the browser never sends to a server; the page sends it in the body of its request. It asks again
 * every 20 seconds while the order is in progress.
 */
export default function OrderTrackingPage() {
  const token = useLocation().hash.replace(/^#/, '');
  const trackingQuery = useQuery({
    queryKey: ['public', 'tracking', token],
    queryFn: ({ signal }) => publicMenuApi.trackOrder(token, signal),
    enabled: token.length > 0,
    retry: false,
    refetchInterval: (query) => trackingRefresh(!!query.state.data?.inProgress, query.state.error),
  });
  const order = trackingQuery.data;
  const contact = order && businessWhatsAppUrl(order.businessContactPhone, `Hola, les escribo por mi pedido ${order.orderNumber}.`);

  return (
    <div className="cl-page">
      <PageTitle title="Seguimiento de tu pedido" />

      {!token && (
        <div className="card">
          <div className="card-body text-center py-5">
            <i className="ri-route-line fs-1 text-muted d-block mb-2" aria-hidden="true"></i>
            <h2 className="h5">Abre el enlace de tu recibo</h2>
            <p className="text-muted mb-0">Al hacer un pedido en línea recibes un enlace para ver cómo va. <Link to="/explorar">Explorar negocios</Link></p>
          </div>
        </div>
      )}
      {trackingQuery.isLoading && (
        <div className="text-center py-5"><div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando</span></div></div>
      )}
      {trackingQuery.error && (
        <div className="alert alert-warning">
          {getApiErrorMessage(trackingQuery.error, 'No se pudo cargar tu pedido.')}
          {/* A later request failed: what shows is what was read before. */}
          {order && ' Lo de abajo es lo último que vimos.'}
        </div>
      )}

      {order && (
        <div className="row g-3">
          <div className="col-12 col-lg-5">
            <div className="card mb-0">
              <div className="card-body">
                <div className="d-flex justify-content-between align-items-start gap-2 mb-3">
                  <div>
                    <h2 className="h5 mb-0">{order.businessName}</h2>
                    <small className="text-muted">Folio {order.orderNumber} · {formatDateTime(order.createdAt)}</small>
                  </div>
                  <OrderStepChip step={order.step} label={order.stepLabel} />
                </div>
                <OrderTimeline steps={order.steps} />
                {order.step === OrderTrackingStep.Cancelled && order.cancellationReason && (
                  <p className="small mt-3 mb-0"><span className="text-muted">Motivo:</span> {order.cancellationReason}</p>
                )}
                {order.courierTrackingUrl && (
                  <a className="btn btn-light btn-sm mt-3" href={order.courierTrackingUrl} target="_blank" rel="noopener noreferrer">
                    <i className="ri-truck-line me-1" aria-hidden="true"></i>Seguir el envío con la paquetería
                  </a>
                )}
              </div>
            </div>
          </div>
          <div className="col-12 col-lg-7">
            <div className="card mb-0">
              <div className="card-body">
                <h2 className="h6">Tu pedido{order.toAddress ? ' a domicilio' : ''}</h2>
                <OrderItemsSummary
                  items={order.items}
                  deliveryFee={order.deliveryFee}
                  visitFee={order.visitFee}
                  pricesIncludeTax={order.pricesIncludeTax}
                  tax={order.tax}
                  total={order.total}
                  paidAmount={order.paidAmount}
                  writtenOffAmount={order.writtenOffAmount}
                  cancelled={order.step === OrderTrackingStep.Cancelled}
                />
                <div className="d-flex flex-wrap gap-2 mt-3">
                  <Link className="btn btn-primary" to={`/m/${order.businessSlug}`}>
                    <i className="ri-store-2-line me-1" aria-hidden="true"></i>Ver el menú
                  </Link>
                  {contact && (
                    <a className="btn btn-light" href={contact} target="_blank" rel="noopener noreferrer">
                      <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Escribir al negocio
                    </a>
                  )}
                </div>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
