import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import PageTitle from '../../components/common/PageTitle';
import StatusChip from '../../components/common/StatusChip';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import type { OrderStatus } from '../../types';
import { formatDateTime } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

const PAGE_SIZE = 10;

/** A customer account's orders in every business, newest first, with "Pedir otra vez" where the menu takes orders. */
export default function MyOrders() {
  const [page, setPage] = useState(1);
  const ordersQuery = useQuery({
    queryKey: ['me', 'orders', page],
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/me/orders', { query: { pageNumber: page, pageSize: PAGE_SIZE }, signal }),
  });
  const orders = ordersQuery.data?.items ?? [];

  return (
    <>
      <PageTitle title="Mis pedidos" subtitle="Mi cuenta" />

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
                    <Link className="btn btn-sm btn-primary" to={`/m/${order.businessSlug}`}>
                      <i className="ri-restart-line me-1" aria-hidden="true"></i>Pedir otra vez
                    </Link>
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
    </>
  );
}
