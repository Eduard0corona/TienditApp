import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import type { SupplyItemDto } from '../../types';
import { formatUnitCost } from '../../utils/money';
import { formatStockQuantity, getUnitLabel } from '../../utils/units';
import type { SupplyFilters } from './inventoryLists';
import type { ServerList } from './useServerList';

const COLUMNS = [
  { header: 'Insumo' },
  { header: 'Clave' },
  { header: 'Unidad', className: 'text-center' },
  { header: 'Costo promedio', className: 'text-end' },
  { header: 'Existencia', className: 'text-end' },
  { header: 'Pedir al llegar a', className: 'text-end' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

interface SuppliesTabProps {
  list: ServerList<SupplyItemDto, SupplyFilters>;
  onEdit: (supply: SupplyItemDto) => void;
  onToggleActive: (supply: SupplyItemDto) => void;
}

/** Supplies, and the stock of the products that control their own, with what each has on hand. */
export default function SuppliesTab({ list, onEdit, onToggleActive }: SuppliesTabProps) {
  return (
    <DataTable
      data={list.data.items}
      columns={COLUMNS}
      keyExtractor={(item) => item.id}
      loading={list.loading}
      emptyMessage="No hay insumos registrados."
      filters={(
        <>
          <DataTableFilter label="Estado" htmlFor="inventorymanagement-estado">
            <select id="inventorymanagement-estado"
              className="form-select form-select-sm"
              value={list.filters.active}
              onChange={(e) => list.changeFilters({ active: e.target.value })}
            >
              <option value="">Todos</option>
              <option value="true">Activos</option>
              <option value="false">Inactivos</option>
            </select>
          </DataTableFilter>
          <div className="form-check form-switch mb-1">
            <input id="inventorymanagement-solo-bajo-stock"
              className="form-check-input"
              type="checkbox"
              checked={list.filters.lowStockOnly}
              onChange={(e) => list.changeFilters({ lowStockOnly: e.target.checked })}
            />
            <label htmlFor="inventorymanagement-solo-bajo-stock" className="form-check-label">Solo con pocas existencias</label>
          </div>
        </>
      )}
      serverSide={list.table}
      initialSearch={list.search}
      renderRow={(item) => (
        <>
          <td>
            <div className="fw-semibold">
              {item.name}
              {item.productId && (
                <span className="badge bg-info-subtle text-info-emphasis ms-1" title={`Existencia del producto ${item.productName ?? ''}`.trim()}>
                  Producto
                </span>
              )}
            </div>
            <div className="text-muted small">
              {item.categoryName || '-'}
              {item.isCritical && <span className="badge bg-danger ms-1">Crítico</span>}
            </div>
          </td>
          <td>
            <div>{item.code || '-'}</div>
            <div className="text-muted small">{item.sku || '-'}</div>
          </td>
          <td className="text-center">{getUnitLabel(item.unit)}</td>
          <td className="text-end">{formatUnitCost(item.averageCost)}</td>
          <td className="text-end">
            {formatStockQuantity(item.totalOnHand)}
            {item.isLowStock && (
              <span className="badge bg-warning text-dark ms-1">Por pedir</span>
            )}
          </td>
          <td className="text-end">{formatStockQuantity(item.reorderPoint)}</td>
          <td className="text-center">
            <span className={`badge ${item.isActive ? 'bg-success' : 'bg-danger'}`}>
              {item.isActive ? 'Activo' : 'Inactivo'}
            </span>
          </td>
          <td className="text-center text-nowrap">
            <button
              type="button"
              className="btn btn-sm btn-light me-1"
              aria-label={`Editar ${item.name}`}
              onClick={() => onEdit(item)}
            >
              Editar
            </button>
            <button
              type="button"
              className="btn btn-sm btn-light"
              title={item.productId && item.isActive
                ? 'Es la existencia de un producto: deja de controlarla desde el producto'
                : item.isActive ? 'Desactivar' : 'Activar'}
              aria-label={item.isActive ? `Desactivar ${item.name}` : `Activar ${item.name}`}
              disabled={!!item.productId && item.isActive}
              onClick={() => onToggleActive(item)}
            >
              {item.isActive ? 'Desactivar' : 'Activar'}
            </button>
          </td>
        </>
      )}
    />
  );
}
