import { useCallback, useEffect, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import { kitchenApi } from '../../api/kitchenApi';
import type { KitchenOrderDto } from '../../types';

const STATUS_LABELS: Record<number, string> = {
  0: 'Pendiente',
  1: 'Confirmada',
  2: 'En preparacion',
  3: 'Lista',
  4: 'Aceptada',
  5: 'En progreso',
  6: 'Entregada',
  7: 'Completada',
  99: 'Cancelada',
};

function toLocalDateTime(date: string) {
  return new Date(date).toLocaleString('es-MX', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export default function KitchenBoard() {
  const [orders, setOrders] = useState<KitchenOrderDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [processingOrderId, setProcessingOrderId] = useState('');

  const loadOrders = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const { data } = await kitchenApi.getOrders();
      setOrders(data);
    } catch {
      setError('No se pudieron cargar las ordenes de cocina.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadOrders();
  }, [loadOrders]);

  const handleStart = async (orderId: string) => {
    setProcessingOrderId(orderId);
    setError('');
    try {
      await kitchenApi.startPreparation(orderId);
      await loadOrders();
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string; detail?: string } } })?.response?.data?.error ||
        (err as { response?: { data?: { error?: string; detail?: string } } })?.response?.data?.detail ||
        'No se pudo iniciar la preparacion.';
      setError(msg);
    } finally {
      setProcessingOrderId('');
    }
  };

  const handleReady = async (orderId: string) => {
    setProcessingOrderId(orderId);
    setError('');
    try {
      await kitchenApi.markReady(orderId);
      await loadOrders();
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string; detail?: string } } })?.response?.data?.error ||
        (err as { response?: { data?: { error?: string; detail?: string } } })?.response?.data?.detail ||
        'No se pudo marcar la orden como lista.';
      setError(msg);
    } finally {
      setProcessingOrderId('');
    }
  };

  return (
    <>
      <PageTitle title="Cocina" subtitle="Operacion" />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError('')}></button>
        </div>
      )}

      {loading ? (
        <div className="text-center py-5">
          <div className="spinner-border text-primary" role="status"></div>
        </div>
      ) : orders.length === 0 ? (
        <div className="card">
          <div className="card-body text-center py-5 text-muted">
            No hay ordenes pendientes para cocina.
          </div>
        </div>
      ) : (
        <div className="row">
          {orders.map((order) => (
            <div className="col-xl-4 col-md-6" key={order.id}>
              <div className="card">
                <div className="card-body">
                  <div className="d-flex justify-content-between align-items-start mb-2">
                    <div>
                      <h5 className="mb-1">{order.orderNumber}</h5>
                      <div className="text-muted small">{toLocalDateTime(order.createdAt)}</div>
                    </div>
                    <span className={`badge ${order.status === 2 ? 'bg-primary' : 'bg-warning'}`}>
                      {STATUS_LABELS[order.status] || order.status}
                    </span>
                  </div>

                  <div className="mb-2">
                    <div><strong>Cliente:</strong> {order.customerName}</div>
                    <div><strong>Mesa:</strong> {order.tableNumber || '-'}</div>
                  </div>

                  <div className="border rounded p-2 mb-3">
                    <div className="fw-semibold mb-1">Items</div>
                    <ul className="mb-0 ps-3">
                      {order.items.map((item) => (
                        <li key={item.id}>
                          {item.quantity} x {item.productName}
                        </li>
                      ))}
                    </ul>
                  </div>

                  <div className="d-flex gap-2">
                    <button
                      type="button"
                      className="btn btn-sm btn-primary flex-fill"
                      onClick={() => handleStart(order.id)}
                      disabled={processingOrderId === order.id || order.status === 2}
                    >
                      Iniciar
                    </button>
                    <button
                      type="button"
                      className="btn btn-sm btn-success flex-fill"
                      onClick={() => handleReady(order.id)}
                      disabled={processingOrderId === order.id}
                    >
                      Marcar lista
                    </button>
                  </div>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}
    </>
  );
}
