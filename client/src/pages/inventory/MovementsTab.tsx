import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import type { InventoryMovementDto } from '../../types';
import { formatMoney } from '../../utils/money';
import {
  MOVEMENT_REASON_OPTIONS,
  MOVEMENT_TYPE_OPTIONS,
  formatDateTime,
  getMovementReasonLabel,
  getMovementTypeLabel,
} from './inventoryLabels';
import type { MovementFilters } from './inventoryLists';
import type { ServerList } from './useServerList';

const COLUMNS = [
  { header: 'Folio' },
  { header: 'Tipo', className: 'text-center' },
  { header: 'Motivo', className: 'text-center' },
  { header: 'Fecha', className: 'text-center' },
  { header: 'Referencia' },
  { header: 'Monto', className: 'text-end' },
  { header: 'Acciones', className: 'text-center' },
];

interface MovementsTabProps {
  list: ServerList<InventoryMovementDto, MovementFilters>;
  onOpenDetail: (movementId: string) => void;
}

/** Entries, exits, adjustments and transfers, with their folio and amount. */
export default function MovementsTab({ list, onOpenDetail }: MovementsTabProps) {
  return (
    <DataTable
      data={list.data.items}
      columns={COLUMNS}
      keyExtractor={(item) => item.id}
      loading={list.loading}
      emptyMessage="No hay movimientos registrados."
      filters={(
        <>
          <DataTableFilter label="Tipo" htmlFor="inventorymanagement-tipo">
            <select id="inventorymanagement-tipo"
              className="form-select form-select-sm"
              value={list.filters.type}
              onChange={(e) => list.changeFilters({ type: e.target.value })}
            >
              <option value="">Todos</option>
              {MOVEMENT_TYPE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </DataTableFilter>
          <DataTableFilter label="Motivo" htmlFor="inventorymanagement-motivo">
            <select id="inventorymanagement-motivo"
              className="form-select form-select-sm"
              value={list.filters.reason}
              onChange={(e) => list.changeFilters({ reason: e.target.value })}
            >
              <option value="">Todos</option>
              {MOVEMENT_REASON_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </DataTableFilter>
          <DataTableFilter label="Desde" htmlFor="inventorymanagement-desde">
            <input id="inventorymanagement-desde"
              type="date"
              className="form-control form-control-sm"
              value={list.filters.fromDate}
              onChange={(e) => list.changeFilters({ fromDate: e.target.value })}
            />
          </DataTableFilter>
          <DataTableFilter label="Hasta" htmlFor="inventorymanagement-hasta">
            <input id="inventorymanagement-hasta"
              type="date"
              className="form-control form-control-sm"
              value={list.filters.toDate}
              onChange={(e) => list.changeFilters({ toDate: e.target.value })}
            />
          </DataTableFilter>
        </>
      )}
      serverSide={list.table}
      initialSearch={list.search}
      renderRow={(item) => (
        <>
          <td className="fw-semibold">{item.movementNumber}</td>
          <td className="text-center">{getMovementTypeLabel(item.movementType)}</td>
          <td className="text-center">{getMovementReasonLabel(item.movementReason)}</td>
          <td className="text-center">{formatDateTime(item.movementDate)}</td>
          <td>
            <div>{item.referenceNumber || '-'}</div>
            <div className="text-muted small">{item.counterpartyName || '-'}</div>
          </td>
          <td className="text-end">{formatMoney(item.totalAmount)}</td>
          <td className="text-center">
            <button
              type="button"
              className="btn btn-sm btn-light"
              aria-label={`Ver el movimiento ${item.movementNumber}`}
              onClick={() => onOpenDetail(item.id)}
            >
              Ver
            </button>
          </td>
        </>
      )}
    />
  );
}
