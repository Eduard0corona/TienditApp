import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi, type MyOrder } from '../../api/accountApi';
import { getApiErrorMessage } from '../../api/errors';
import PageTitle from '../../components/common/PageTitle';
import ReviewButton from '../../components/reviews/ReviewButton';
import ReviewDialog, { type ReviewedBusiness } from '../../components/reviews/ReviewDialog';
import { OrderStepChip } from '../../components/tracking/OrderTimeline';
import { useMyReviews } from '../../hooks/useMyReviews';
import { useOrderAgain } from '../../hooks/useOrderAgain';
import { MyOrdersScope, OrderStatus, OrderTrackingStep } from '../../types';
import { formatDateTime } from '../../utils/dates';
import { formatMoney, toCents } from '../../utils/money';
import { trackingRefresh } from '../../utils/orderTracking';

const PAGE_SIZE = 10;
/** The most the API gives in one page: orders in progress are few, and all of them show. */
const IN_PROGRESS_PAGE_SIZE = 50;

/** What is left to pay, in cents: what was paid and what the business forgave (Fase 26B) do not always add up in floats. */
const toPayCents = (order: MyOrder) => toCents(order.total) - toCents(order.paidAmount) - toCents(order.writtenOffAmount);

interface OrderCardProps {
  order: MyOrder;
  onOrderAgain: (order: MyOrder) => void;
  /** Whether the account already rated its business, and how to open the dialog (Fase 24H). */
  reviewed: boolean;
  onReview: (business: ReviewedBusiness) => void;
}

function OrderCard({ order, onOrderAgain, reviewed, onReview }: OrderCardProps) {
  const cancelled = order.step === OrderTrackingStep.Cancelled;
  // An order received is what lets the account rate its business, as the API decides.
  const received = order.status === OrderStatus.Delivered || order.status === OrderStatus.Completed;
  return (
    <div className="card h-100 mb-0">
      <div className="card-body">
        <div className="d-flex justify-content-between align-items-start gap-2 mb-2">
          <div>
            <h3 className="h6 mb-0">{order.businessName}</h3>
            <small className="text-muted">{order.orderNumber} · {formatDateTime(order.createdAt)}</small>
          </div>
          <OrderStepChip step={order.step} label={order.stepLabel} />
        </div>
        <ul className="list-unstyled small mb-2">
          {order.items.slice(0, 4).map((item, index) => (
            <li key={`${order.id}-${index}`}>
              {item.quantity.toLocaleString('es-MX', { maximumFractionDigits: 3 })} × {item.productName}
            </li>
          ))}
          {order.items.length > 4 && <li className="text-muted">y {order.items.length - 4} más</li>}
        </ul>
        <div className="d-flex justify-content-between align-items-center gap-2 flex-wrap">
          <span className="fw-semibold ta-money">
            {formatMoney(order.total)}
            {toPayCents(order) > 0 && !cancelled && (
              <span className="d-block small text-danger fw-normal">Por pagar {formatMoney(toPayCents(order) / 100)}</span>
            )}
          </span>
          <span className="d-flex flex-wrap gap-2">
            {received && (
              <ReviewButton
                businessName={order.businessName}
                reviewed={reviewed}
                onClick={() => onReview({ slug: order.businessSlug, name: order.businessName })}
              />
            )}
            <Link
              className="btn btn-sm btn-light"
              to={`/mis-pedidos/${order.id}`}
              aria-label={`Ver pedido ${order.orderNumber} de ${order.businessName}`}
            >
              Ver pedido
            </Link>
            {order.canOrderAgain && (
              <button
                type="button"
                className="btn btn-sm btn-primary"
                onClick={() => onOrderAgain(order)}
                aria-label={`Pedir otra vez ${order.orderNumber} de ${order.businessName}`}
              >
                <i className="ri-restart-line me-1" aria-hidden="true"></i>Pedir otra vez
              </button>
            )}
          </span>
        </div>
      </div>
    </div>
  );
}

/**
 * A customer account's orders in every business (Fase 24C): the ones in progress first, asked again every 20 seconds while
 * there are any, and the ones before, newest first and by pages. «Pedir otra vez» fills the store's cart where the menu
 * takes orders (Fase 24B), and an order received rates its business (Fase 24H).
 */
export default function MyOrders() {
  const [page, setPage] = useState(1);
  const [reviewing, setReviewing] = useState<ReviewedBusiness | null>(null);
  const orderAgain = useOrderAgain();
  const myReviews = useMyReviews();
  const queryClient = useQueryClient();
  const inProgressQuery = useQuery({
    queryKey: ['me', 'orders', 'in-progress'],
    queryFn: ({ signal }) => accountApi.getOrders(MyOrdersScope.InProgress, 1, IN_PROGRESS_PAGE_SIZE, signal),
    refetchInterval: (query) => trackingRefresh((query.state.data?.items.length ?? 0) > 0, query.state.error),
  });
  const pastQuery = useQuery({
    queryKey: ['me', 'orders', 'past', page],
    queryFn: ({ signal }) => accountApi.getOrders(MyOrdersScope.Past, page, PAGE_SIZE, signal),
  });
  const inProgress = inProgressQuery.data?.items ?? [];
  const past = pastQuery.data?.items ?? [];

  // An order that leaves «En curso» (delivered or cancelled) goes to «Anteriores»: those are asked again.
  const followed = inProgress.map((order) => order.id).join(',');
  const lastFollowed = useRef<string | null>(null);
  useEffect(() => {
    if (!inProgressQuery.data) return;
    if (lastFollowed.current !== null && lastFollowed.current !== followed) {
      void queryClient.invalidateQueries({ queryKey: ['me', 'orders', 'past'] });
    }
    lastFollowed.current = followed;
  }, [followed, inProgressQuery.data, queryClient]);

  const loading = inProgressQuery.isLoading || pastQuery.isLoading;
  const error = inProgressQuery.error ?? pastQuery.error;

  // Inside the customer's frame (CustomerLayout), which has no margins of its own.
  return (
    <div className="cl-page">
      <PageTitle title="Mis pedidos" />

      {loading && (
        <div className="text-center py-5"><div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando</span></div></div>
      )}
      {error && (
        <div className="alert alert-danger">{getApiErrorMessage(error, 'No se pudieron cargar tus pedidos.')}</div>
      )}
      {!loading && !error && inProgress.length === 0 && past.length === 0 && (
        <div className="card">
          <div className="card-body text-center py-5">
            <i className="ri-shopping-bag-3-line fs-1 text-muted d-block mb-2" aria-hidden="true"></i>
            <h2 className="h5">Todavía no tienes pedidos</h2>
            <p className="text-muted mb-0">
              Aquí aparecen los pedidos que hagas con tu cuenta o con el teléfono que confirmaste.
            </p>
          </div>
        </div>
      )}

      {inProgress.length > 0 && (
        <section aria-labelledby="mis-pedidos-en-curso" className="mb-4">
          <h2 id="mis-pedidos-en-curso" className="h5 mb-3">En curso</h2>
          <div className="row g-3">
            {inProgress.map((order) => (
              <div className="col-12 col-lg-6" key={order.id}>
                <OrderCard order={order} onOrderAgain={orderAgain} reviewed={myReviews.has(order.businessSlug)} onReview={setReviewing} />
              </div>
            ))}
          </div>
        </section>
      )}

      {past.length > 0 && (
        <section aria-labelledby="mis-pedidos-anteriores">
          <h2 id="mis-pedidos-anteriores" className="h5 mb-3">Anteriores</h2>
          <div className="row g-3">
            {past.map((order) => (
              <div className="col-12 col-lg-6" key={order.id}>
                <OrderCard order={order} onOrderAgain={orderAgain} reviewed={myReviews.has(order.businessSlug)} onReview={setReviewing} />
              </div>
            ))}
          </div>

          {(pastQuery.data?.totalPages ?? 0) > 1 && (
            <div className="d-flex justify-content-center align-items-center gap-3 mt-3">
              <button type="button" className="btn btn-light btn-sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                Más recientes
              </button>
              <span className="small text-muted">Página {page} de {pastQuery.data?.totalPages}</span>
              <button
                type="button"
                className="btn btn-light btn-sm"
                disabled={!pastQuery.data?.hasNextPage}
                onClick={() => setPage(page + 1)}
              >
                Más antiguos
              </button>
            </div>
          )}
        </section>
      )}

      <ReviewDialog business={reviewing} onClose={() => setReviewing(null)} />
    </div>
  );
}
