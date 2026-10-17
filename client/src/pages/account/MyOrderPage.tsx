import { Link, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { accountApi } from '../../api/accountApi';
import { getApiErrorMessage } from '../../api/errors';
import PageTitle from '../../components/common/PageTitle';
import OrderItemsSummary from '../../components/tracking/OrderItemsSummary';
import OrderTimeline, { OrderStepChip } from '../../components/tracking/OrderTimeline';
import { useOrderAgain } from '../../hooks/useOrderAgain';
import { OrderTrackingStep } from '../../types';
import { formatDateTime } from '../../utils/dates';
import { TRACKING_REFRESH_MS } from '../../utils/orderTracking';
import { businessWhatsAppUrl } from '../../utils/whatsApp';

/**
 * One order of the customer account (Fase 24C): where it is, step by step with the time of each one, what it carries,
 * where it goes and «Pedir otra vez». It asks again every 20 seconds while the order is in progress.
 */
export default function MyOrderPage() {
  const { id = '' } = useParams();
  const orderAgain = useOrderAgain();
  const orderQuery = useQuery({
    queryKey: ['me', 'orders', 'detail', id],
    queryFn: ({ signal }) => accountApi.getOrder(id, signal),
    refetchInterval: (query) => (query.state.data?.inProgress ? TRACKING_REFRESH_MS : false),
  });
  const order = orderQuery.data;
  const contact = order && businessWhatsAppUrl(order.businessContactPhone, `Hola, les escribo por mi pedido ${order.orderNumber}.`);

  return (
    <div className="cl-page">
      <PageTitle title={order ? `Pedido ${order.orderNumber}` : 'Pedido'} parent={{ label: 'Mis pedidos', to: '/mis-pedidos' }} />

      {orderQuery.isLoading && (
        <div className="text-center py-5"><div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando</span></div></div>
      )}
      {orderQuery.error && !order && (
        <div className="alert alert-danger">
          {getApiErrorMessage(orderQuery.error, 'No se pudo cargar tu pedido.')} <Link to="/mis-pedidos">Ver mis pedidos</Link>
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
                    <small className="text-muted">{formatDateTime(order.createdAt)}</small>
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
                <h2 className="h6">Tu pedido</h2>
                <OrderItemsSummary
                  items={order.items}
                  deliveryFee={order.deliveryFee}
                  pricesIncludeTax={order.pricesIncludeTax}
                  tax={order.tax}
                  total={order.total}
                  paidAmount={order.paidAmount}
                  cancelled={order.step === OrderTrackingStep.Cancelled}
                />
                {order.delivery && (
                  <p className="small mt-3 mb-0">
                    <span className="text-muted d-block">Lo llevamos a</span>
                    {order.delivery.addressLine}, CP {order.delivery.postalCode}
                    {order.delivery.references && <span className="d-block text-muted">{order.delivery.references}</span>}
                  </p>
                )}
                {!order.delivery && order.tableNumber && (
                  <p className="small mt-3 mb-0"><span className="text-muted">Mesa:</span> {order.tableNumber}</p>
                )}
                <div className="d-flex flex-wrap gap-2 mt-3">
                  {order.canOrderAgain && (
                    <button
                      type="button"
                      className="btn btn-primary"
                      onClick={() => orderAgain(order)}
                      aria-label={`Pedir otra vez ${order.orderNumber} de ${order.businessName}`}
                    >
                      <i className="ri-restart-line me-1" aria-hidden="true"></i>Pedir otra vez
                    </button>
                  )}
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
