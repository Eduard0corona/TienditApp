import DataTable from '../../components/common/DataTable';
import type { InventoryKardexEntryDto, InventoryLocationDto, SupplyItemDto } from '../../types';
import { formatUnitCost } from '../../utils/money';
import { formatStockQuantity } from '../../utils/units';
import { formatDateTime } from './inventoryLabels';
import type { KardexFilters } from './inventoryLists';
import type { ServerList } from './useServerList';

const COLUMNS = [
  { header: 'Fecha', className: 'text-center' },
  { header: 'Folio' },
  { header: 'Insumo' },
  { header: 'Ubicación' },
  { header: 'Entrada', className: 'text-end' },
  { header: 'Salida', className: 'text-end' },
  { header: 'Saldo', className: 'text-end' },
  { header: 'Costo', className: 'text-end' },
  { header: 'Referencia' },
];

interface KardexTabProps {
  list: ServerList<InventoryKardexEntryDto, KardexFilters>;
  supplyOptions: SupplyItemDto[];
  locations: InventoryLocationDto[];
}

/** Every entry and exit of each supply with the balance it left. */
export default function KardexTab({ list, supplyOptions, locations }: KardexTabProps) {
  return (
    <>
      <div className="row mb-2">
        <div className="col-md-3 mb-2">
          <label htmlFor="inventorymanagement-insumo" className="form-label">Insumo</label>
          <select id="inventorymanagement-insumo"
            className="form-select form-select-sm"
            value={list.filters.supplyId}
            onChange={(e) => list.changeFilters({ supplyId: e.target.value })}
          >
            <option value="">Todos</option>
            {supplyOptions.map((supply) => (
              <option key={supply.id} value={supply.id}>
                {supply.name}
              </option>
            ))}
          </select>
        </div>
        <div className="col-md-3 mb-2">
          <label htmlFor="inventorymanagement-ubicacion-2" className="form-label">Ubicación</label>
          <select id="inventorymanagement-ubicacion-2"
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
        <div className="col-md-3 mb-2">
          <label htmlFor="inventorymanagement-desde-2" className="form-label">Desde</label>
          <input id="inventorymanagement-desde-2"
            type="date"
            className="form-control form-control-sm"
            value={list.filters.fromDate}
            onChange={(e) => list.changeFilters({ fromDate: e.target.value })}
          />
        </div>
        <div className="col-md-3 mb-2">
          <label htmlFor="inventorymanagement-hasta-2" className="form-label">Hasta</label>
          <input id="inventorymanagement-hasta-2"
            type="date"
            className="form-control form-control-sm"
            value={list.filters.toDate}
            onChange={(e) => list.changeFilters({ toDate: e.target.value })}
          />
        </div>
      </div>

      <DataTable
        data={list.data.items}
        columns={COLUMNS}
        keyExtractor={(item) => item.movementLineId}
        loading={list.loading}
        emptyMessage="No hay movimientos para los filtros seleccionados."
        serverSide={list.table}
        initialSearch={list.search}
        renderRow={(item) => (
          <>
            <td className="text-center">{formatDateTime(item.movementDate)}</td>
            <td className="fw-semibold">{item.movementNumber}</td>
            <td>{item.supplyItemName}</td>
            <td>{item.inventoryLocationName}</td>
            <td className="text-end">{item.quantityIn > 0 ? formatStockQuantity(item.quantityIn) : '-'}</td>
            <td className="text-end">{item.quantityOut > 0 ? formatStockQuantity(item.quantityOut) : '-'}</td>
            <td className="text-end">{formatStockQuantity(item.quantityAfter)}</td>
            <td className="text-end">{item.unitCost !== undefined ? formatUnitCost(item.unitCost) : '-'}</td>
            <td>
              <div>{item.referenceNumber || '-'}</div>
              <div className="text-muted small">{item.externalReference || '-'}</div>
            </td>
          </>
        )}
      />
    </>
  );
}
