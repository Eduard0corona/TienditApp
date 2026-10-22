import { useId, useMemo, useState } from 'react';
import type { PlatformSettlementOrder } from '../../api/platformSettlements';
import { OrderStatus } from '../../types';
import { formatShortDateTime } from '../../utils/dates';
import type { DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { getCollectedByLabel } from '../../utils/labels';
import { formatMoney, toCents } from '../../utils/money';
import { placementOf } from './settlementEdits';
import DataTable, { DataTableFilter, type DataTableColumn } from '../common/DataTable';
import StatusChip from '../common/StatusChip';

type Shown = 'all' | 'picked' | 'unpicked';

interface SettlementOrdersProps {
  platform: DeliveryPlatform;
  orders: PlatformSettlementOrder[];
  /** The period being written (yyyy-mm-dd), to say which orders fall before or after it. */
  from: string;
  to: string;
  /** The orders saved in the settlement, to say when what one brings changed since the save. */
  saved: ReadonlySet<string>;
  /** Without it the list only shows: a closed or voided settlement, or someone who only reads the reports. */
  picking?: {
    isPicked: (order: PlatformSettlementOrder) => boolean;
    onPick: (orderIds: string[], picked: boolean) => void;
    disabled: boolean;
  };
  loading?: boolean;
}

// A statement brings dozens of orders a week: a page holds a week of most businesses.
const PAGE_SIZES = [50, 100, 500];

// On a phone the box, the folio (with its date) and what it brings, the three that are compared with the statement; who
// charged and the sale from a tablet up.
const FROM_TABLET = 'd-none d-sm-table-cell';

/**
 * The orders of a settlement, folio by folio, to compare with the platform's statement: with boxes while it is open, the
 * ones of the period proposed and the pending ones of before unchecked; what each one brings is the server's.
 */
export default function SettlementOrders({ platform, orders, from, to, saved, picking, loading = false }: SettlementOrdersProps) {
  const id = useId();
  const [shown, setShown] = useState<Shown>('all');
  const rows = useMemo(() => {
    if (!picking || shown === 'all') return orders;
    return orders.filter((order) => picking.isPicked(order) === (shown === 'picked'));
  }, [orders, picking, shown]);
  const inPeriod = useMemo(
    () => orders.filter((order) => placementOf(order.registeredOn, from, to) === 'in').map((order) => order.orderId),
    [orders, from, to],
  );

  const columns: DataTableColumn[] = [
    ...(picking ? [{ header: 'Incluir', width: 72 }] : []),
    { header: 'Folio de la app' },
    { header: 'Cobró', className: FROM_TABLET },
    { header: 'Venta', className: `text-end ${FROM_TABLET}` },
    { header: 'Neto esperado', className: 'text-end' },
  ];
  const emptyMessage = !picking
    ? 'Esta liquidación no lleva pedidos.'
    : shown !== 'all' ? 'Ningún pedido aquí.' : 'Sin pedidos pendientes hasta el final del periodo.';

  return (
    <DataTable<PlatformSettlementOrder>
      data={rows}
      columns={columns}
      keyExtractor={(order) => order.orderId}
      loading={loading}
      emptyMessage={emptyMessage}
      searchPlaceholder="Buscar folio o pedido"
      searchFilter={(order, search) =>
        order.externalOrderNumber.toLowerCase().includes(search)
        || (order.externalOrderId ?? '').toLowerCase().includes(search)
        || order.orderNumber.toLowerCase().includes(search)}
      pageSizeOptions={PAGE_SIZES}
      filters={picking && (
        <DataTableFilter label="Mostrar" htmlFor={`${id}-shown`}>
          <select id={`${id}-shown`} className="form-select form-select-sm" value={shown} onChange={(event) => setShown(event.target.value as Shown)}>
            <option value="all">Todos</option>
            <option value="picked">Incluidos</option>
            <option value="unpicked">Sin incluir</option>
          </select>
        </DataTableFilter>
      )}
      toolbarEnd={picking && (
        <>
          <button
            type="button"
            className="btn btn-light btn-sm"
            onClick={() => picking.onPick(inPeriod, true)}
            disabled={picking.disabled || inPeriod.length === 0}
          >
            Incluir los del periodo
          </button>
          <button
            type="button"
            className="btn btn-light btn-sm"
            onClick={() => picking.onPick(orders.map((order) => order.orderId), false)}
            disabled={picking.disabled || orders.length === 0}
          >
            Quitar todos
          </button>
        </>
      )}
      renderRow={(order) => {
        const placement = placementOf(order.registeredOn, from, to);
        const changed = saved.has(order.orderId) && toCents(order.currentExpectedNet) !== toCents(order.expectedNet);
        return (
          <>
            {picking && (
              <td>
                <input
                  type="checkbox"
                  className="form-check-input"
                  aria-label={`Incluir el pedido ${order.externalOrderNumber} (${order.orderNumber})`}
                  checked={picking.isPicked(order)}
                  disabled={picking.disabled || placement === 'after'}
                  onChange={(event) => picking.onPick([order.orderId], event.target.checked)}
                />
              </td>
            )}
            <td>
              <span className="fw-semibold">{order.externalOrderNumber}</span>
              {order.externalOrderId && <div className="small text-muted text-break">ID {order.externalOrderId}</div>}
              <div className="small text-muted">{order.orderNumber} · {formatShortDateTime(order.registeredAt)}</div>
              {placement !== 'in' && (
                <div className="small text-muted">{placement === 'before' ? 'De antes del periodo' : 'Después del periodo: no entra'}</div>
              )}
              {order.status === OrderStatus.Cancelled && <div><StatusChip status={order.status} /></div>}
            </td>
            <td className={FROM_TABLET}>{getCollectedByLabel(order.collectedBy, platform)}</td>
            <td className={`text-end ${FROM_TABLET}`}>
              <span className="ta-money">{formatMoney(order.orderTotal)}</span>
              {order.refundedAmount > 0 && <div className="small text-muted">Devuelto <span className="ta-money">{formatMoney(order.refundedAmount)}</span></div>}
            </td>
            <td className="text-end">
              <span className="ta-money">{formatMoney(picking ? order.currentExpectedNet : order.expectedNet)}</span>
              {changed && (
                <div className="small text-muted">
                  {picking ? 'Guardado: ' : 'Ahora: '}
                  <span className="ta-money">{formatMoney(picking ? order.expectedNet : order.currentExpectedNet)}</span>
                </div>
              )}
            </td>
          </>
        );
      }}
    />
  );
}
