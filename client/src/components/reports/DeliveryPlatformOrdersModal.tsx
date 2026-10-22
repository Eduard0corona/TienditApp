import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { reportsApi, type DeliveryPlatformOrder } from '../../api/reportsApi';
import DataTable, { type DataTableColumn } from '../common/DataTable';
import StatusChip from '../common/StatusChip';
import { OrderStatus } from '../../types';
import { formatShortDateTime } from '../../utils/dates';
import { getPlatformName, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { getCollectedByLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import { describePeriod } from './reportPeriods';

interface DeliveryPlatformOrdersModalProps {
  platform: DeliveryPlatform;
  /** The period of the report, as the server resolved it (yyyy-mm-dd). */
  from: string;
  to: string;
  /** The branch of the report, or '' for all. */
  branchId: string;
  onClose: () => void;
}

const COLUMNS: DataTableColumn[] = [
  { header: 'Folio de la app' },
  { header: 'Pedido' },
  { header: 'Fecha' },
  { header: 'Cobró' },
  { header: 'Venta', className: 'text-end' },
  { header: 'Comisión e IVA', className: 'text-end' },
  { header: 'Retenciones', className: 'text-end' },
  { header: 'Neto esperado', className: 'text-end' },
  { header: 'Devuelto', className: 'text-end' },
];

// The API takes up to 100 a page and answers 20 by default.
const PAGE_SIZES = [20, 50, 100];

/**
 * The orders of one delivery platform registered in the period of the report, the newest first, to compare folio by
 * folio with its weekly statement. Each one shows the amounts the server copied when it was registered; nothing here
 * imports or reconciles anything.
 */
export default function DeliveryPlatformOrdersModal({ platform, from, to, branchId, onClose }: DeliveryPlatformOrdersModalProps) {
  const titleId = useId();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(PAGE_SIZES[0]);
  // Searched on the server, over every page: the folio of the statement may be on any of them.
  const [search, setSearch] = useState('');
  const name = getPlatformName(platform);
  const orders = useQuery({
    queryKey: ['reports', 'delivery-platform-orders', platform, from, to, branchId, search, page, pageSize],
    queryFn: ({ signal }) => reportsApi.getDeliveryPlatformOrders(
      { channel: platform, from, to, branchId: branchId || undefined, search: search || undefined, pageNumber: page, pageSize },
      signal,
    ),
    // Another page keeps this one on screen until it arrives.
    placeholderData: keepPreviousData,
  });

  return (
    <Modal show onHide={onClose} size="xl" centered scrollable fullscreen="md-down" aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">Pedidos de {name} · {describePeriod(from, to)}</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        <p className="small text-muted">Cada pedido con la comisión que tenía {name} al registrarlo.</p>
        {orders.isError ? (
          <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2 mb-0" role="alert">
            <span className="me-auto">{getApiErrorMessage(orders.error, `No se pudieron cargar los pedidos de ${name}.`)}</span>
            <button type="button" className="btn btn-sm btn-light" onClick={() => void orders.refetch()}>Reintentar</button>
          </div>
        ) : (
          <DataTable<DeliveryPlatformOrder>
            data={orders.data?.items ?? []}
            columns={COLUMNS}
            keyExtractor={(order) => order.orderId}
            loading={orders.isPending}
            emptyMessage={`Sin pedidos de ${name} en el periodo.`}
            searchPlaceholder="Buscar folio o pedido"
            pageSizeOptions={PAGE_SIZES}
            serverSide={{
              totalCount: orders.data?.totalCount ?? 0,
              page,
              pageSize,
              onPageChange: setPage,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPage(1);
              },
              onSearchChange: (term) => {
                setSearch(term);
                setPage(1);
              },
            }}
            renderRow={(order) => (
              <>
                <td>
                  <span className="fw-semibold">{order.externalOrderNumber}</span>
                  {order.externalOrderId && <div className="small text-muted text-break">ID {order.externalOrderId}</div>}
                </td>
                <td>
                  {order.orderNumber}
                  {order.status === OrderStatus.Cancelled && <div><StatusChip status={order.status} /></div>}
                </td>
                <td>{formatShortDateTime(order.registeredAt)}</td>
                <td>{getCollectedByLabel(order.collectedBy, platform)}</td>
                <td className="text-end ta-money">{formatMoney(order.orderTotal)}</td>
                <td className="text-end ta-money">{formatMoney(order.commissionAndTax)}</td>
                <td className="text-end ta-money">{formatMoney(order.withheld)}</td>
                <td className="text-end ta-money">{formatMoney(order.expectedSettlement)}</td>
                <td className="text-end ta-money">{order.refundedAmount > 0 ? formatMoney(order.refundedAmount) : '—'}</td>
              </>
            )}
          />
        )}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose}>Cerrar</button>
      </Modal.Footer>
    </Modal>
  );
}
