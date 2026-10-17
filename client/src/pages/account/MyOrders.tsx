import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import type { MyOrder } from '../../api/accountApi';
import PageTitle from '../../components/common/PageTitle';
import StatusChip from '../../components/common/StatusChip';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import type { OrderStatus } from '../../types';
import { formatDateTime } from '../../utils/dates';
import { formatMoney } from '../../utils/money';
import { linesOfOrder, saveCart } from '../../utils/savedCart';

const PAGE_SIZE = 10;

/**
 * A customer account's orders in every business, newest first, with «Pedir otra vez» where the menu takes orders: it
 * fills the store's cart with that order, and the store keeps what its menu still has (Fase 24B).
 */
export default function MyOrders() {
  const [page, setPage] = useState(1);
  const navigate = useNavigate();
  const orderAgain = (order: MyOrder) => {
    const { lines, skipped } = linesOfOrder(order.items);
    // An order all by weight has nothing to put back: the menu opens as it is, and a cart left there stays.
    if (lines.length > 0) {
      saveCart(order.businessSlug, { businessName: order.businessName, lines, reorderOf: order.items.length, skipped });
    }
    navigate(`/m/${order.businessSlug}`);
  };
  const ordersQuery = useQuery({
    queryKey: ['me', 'orders', page],
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/me/orders', { query: { pageNumber: page, pageSize: PAGE_SIZE }, signal }),
  });
  const orders = ordersQuery.data?.items ?? [];

  // Inside the customer's frame (CustomerLayout), which has no margins of its own.
  return (
    <div className="cl-page">
      <PageTitle title="Mis pedidos" />

      {ordersQuery.isLoading && (
        <div className="text-center py-5"><div className="spinner-border text-primary" role="status" /></div>
      )}
      {ordersQuery.error && (
        <div className="alert alert-danger">{getApiErrorMessage(ordersQuery.error, 'No se pudieron cargar tus pedidos.')}</div>
      )}
      {!ordersQuery.isLoading && !ordersQuery.error && orders.length === 0 && (
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

      <div className="row g-3">
        {orders.map((order) => (
          <div className="col-12 col-lg-6" key={order.id}>
            <div className="card h-100 mb-0">
              <div className="card-body">
                <div className="d-flex justify-content-between align-items-start gap-2 mb-2">
                  <div>
                    <h2 className="h6 mb-0">{order.businessName}</h2>
                    <small className="text-muted">{order.orderNumber} · {formatDateTime(order.createdAt)}</small>
                  </div>
                  <StatusChip status={order.status as OrderStatus} />
                </div>
                <ul className="list-unstyled small mb-2">
                  {order.items.slice(0, 4).map((item, index) => (
                    <li key={`${order.id}-${index}`}>
                      {item.quantity.toLocaleString('es-MX', { maximumFractionDigits: 3 })} × {item.productName}
                    </li>
                  ))}
                  {order.items.length > 4 && <li className="text-muted">y {order.items.length - 4} más</li>}
                </ul>
                <div className="d-flex justify-content-between align-items-center">
                  <span className="fw-semibold ta-money">
                    {formatMoney(order.total)}
                    {order.paidAmount < order.total && order.status !== 99 && (
                      <span className="d-block small text-danger fw-normal">Por pagar {formatMoney(order.total - order.paidAmount)}</span>
                    )}
                  </span>
                  {order.canOrderAgain && (
                    <button
                      type="button"
                      className="btn btn-sm btn-primary"
                      onClick={() => orderAgain(order)}
                      aria-label={`Pedir otra vez ${order.orderNumber} de ${order.businessName}`}
                    >
                      <i className="ri-restart-line me-1" aria-hidden="true"></i>Pedir otra vez
                    </button>
                  )}
                </div>
              </div>
            </div>
          </div>
        ))}
      </div>

      {(ordersQuery.data?.totalPages ?? 0) > 1 && (
        <div className="d-flex justify-content-center align-items-center gap-3 mt-3">
          <button type="button" className="btn btn-light btn-sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
            Anteriores
          </button>
          <span className="small text-muted">Página {page} de {ordersQuery.data?.totalPages}</span>
          <button
            type="button"
            className="btn btn-light btn-sm"
            disabled={!ordersQuery.data?.hasNextPage}
            onClick={() => setPage(page + 1)}
          >
            Siguientes
          </button>
        </div>
      )}
    </div>
  );
}
