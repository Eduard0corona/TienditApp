import DataTable from '../../components/common/DataTable';
import type { InventoryLocationDto, InventoryStockDto } from '../../types';
import { formatUnitCost } from '../../utils/money';
import { formatStockQuantity, getUnitLabel } from '../../utils/units';
import { formatDateTime } from './inventoryLabels';
import type { StockFilters } from './inventoryLists';
import type { ServerList } from './useServerList';

const COLUMNS = [
  { header: 'Insumo' },
  { header: 'Ubicación' },
  { header: 'Existencia', className: 'text-end' },
  { header: 'Reservado', className: 'text-end' },
  { header: 'Disponible', className: 'text-end' },
  { header: 'Último costo', className: 'text-end' },
  { header: 'Estado', className: 'text-center' },
];

interface StocksTabProps {
  list: ServerList<InventoryStockDto, StockFilters>;
  locations: InventoryLocationDto[];
}

/** What each location has of each supply: on hand, reserved and available. */
export default function StocksTab({ list, locations }: StocksTabProps) {
  return (
    <>
      <div className="row mb-2">
        <div className="col-md-3 mb-2">
          <label htmlFor="inventorymanagement-ubicacion" className="form-label">Ubicación</label>
          <select id="inventorymanagement-ubicacion"
            className="form-select form-select-sm"
            value={list.filters.locationId}
            onChange={(e) => list.changeFilters({ locationId: e.target.value })}
          >
            <option value="">Todas</option>
            {locations.map((location) => (
              <option key={location.id} value={location.id}>
                {location.name}
              </option>
            ))}
          </select>
        </div>
        <div className="col-md-3 mb-2 d-flex align-items-end">
          <div className="form-check form-switch">
            <input id="inventorymanagement-solo-bajo-stock-2"
              className="form-check-input"
              type="checkbox"
              checked={list.filters.lowStockOnly}
              onChange={(e) => list.changeFilters({ lowStockOnly: e.target.checked })}
            />
            <label htmlFor="inventorymanagement-solo-bajo-stock-2" className="form-check-label">Solo bajo stock</label>
          </div>
        </div>
      </div>

      <DataTable
        data={list.data.items}
        columns={COLUMNS}
        keyExtractor={(item) => item.stockId}
        loading={list.loading}
        emptyMessage="No hay existencias registradas."
        serverSide={list.table}
        initialSearch={list.search}
        renderRow={(item) => (
          <>
            <td>
              <div className="fw-semibold">{item.supplyItemName}</div>
              <div className="text-muted small">{item.supplyCode || item.supplySku || '-'}</div>
            </td>
            <td>
              <div>{item.inventoryLocationName}</div>
              <div className="text-muted small">{item.inventoryLocationCode}</div>
            </td>
            <td className="text-end">
              {formatStockQuantity(item.onHandQuantity)} {getUnitLabel(item.unit)}
            </td>
            <td className="text-end">{formatStockQuantity(item.reservedQuantity)}</td>
            <td className="text-end">
              {formatStockQuantity(item.availableQuantity)}
              {item.isLowStock && (
                <span className="badge bg-warning text-dark ms-1">Bajo</span>
              )}
            </td>
            <td className="text-end">{formatUnitCost(item.lastCost)}</td>
            <td className="text-center">
              <span className={`badge ${item.isSupplyActive && item.isLocationActive ? 'bg-success' : 'bg-secondary'}`}>
                {item.isSupplyActive && item.isLocationActive ? 'Activo' : 'Inactivo'}
              </span>
              {item.lastMovementAt && (
                <div className="text-muted small mt-1">{formatDateTime(item.lastMovementAt)}</div>
              )}
            </td>
          </>
        )}
      />
    </>
  );
}
