import DataTable from '../../components/common/DataTable';
import type { InventoryAlertsDto } from '../../types';
import { formatStockQuantity } from '../../utils/units';

const COLUMNS = [
  { header: 'Insumo' },
  { header: 'Ubicación' },
  { header: 'Existencia', className: 'text-end' },
  { header: 'Reorden', className: 'text-end' },
  { header: 'Déficit', className: 'text-end' },
  { header: 'Estado', className: 'text-center' },
];

interface AlertsTabProps {
  alerts: InventoryAlertsDto;
  loading: boolean;
}

/** What ran out or is about to, by location, and the items that still have no recipe. */
export default function AlertsTab({ alerts, loading }: AlertsTabProps) {
  return (
    <>
      <div className="row mb-3">
        <div className="col-md-3 mb-2">
          <div className="card border-danger h-100 mb-0">
            <div className="card-body py-2">
              <div className="text-muted small">Sin stock</div>
              <div className="h3 mb-0">{alerts.outOfStockCount}</div>
            </div>
          </div>
        </div>
        <div className="col-md-3 mb-2">
          <div className="card border-danger h-100 mb-0">
            <div className="card-body py-2">
              <div className="text-muted small">Bajo stock crítico</div>
              <div className="h3 mb-0">{alerts.criticalLowStockCount}</div>
            </div>
          </div>
        </div>
        <div className="col-md-3 mb-2">
          <div className="card border-warning h-100 mb-0">
            <div className="card-body py-2">
              <div className="text-muted small">Bajo stock</div>
              <div className="h3 mb-0">{alerts.lowStockCount}</div>
            </div>
          </div>
        </div>
        <div className="col-md-3 mb-2">
          <div className="card border-info h-100 mb-0">
            <div className="card-body py-2">
              <div className="text-muted small">Ítems sin receta</div>
              <div className="h3 mb-0">{alerts.missingRecipeProductCount}</div>
            </div>
          </div>
        </div>
      </div>

      <DataTable
        data={alerts.items}
        columns={COLUMNS}
        keyExtractor={(item) => `${item.supplyItemId}-${item.inventoryLocationId}`}
        loading={loading}
        emptyMessage="No hay alertas de stock actualmente."
        searchFilter={(item, search) =>
          item.supplyItemName.toLowerCase().includes(search) ||
          item.inventoryLocationName.toLowerCase().includes(search)
        }
        renderRow={(item) => (
          <>
            <td>{item.supplyItemName}</td>
            <td>{item.inventoryLocationName}</td>
            <td className="text-end">{formatStockQuantity(item.onHandQuantity)}</td>
            <td className="text-end">{formatStockQuantity(item.reorderPoint)}</td>
            <td className="text-end">{formatStockQuantity(item.deficit)}</td>
            <td className="text-center">
              <span
                className={`badge ${
                  item.isOutOfStock
                    ? 'bg-danger'
                    : item.isCritical
                      ? 'bg-danger-subtle text-danger'
                      : 'bg-warning text-dark'
                }`}
              >
                {item.isOutOfStock ? 'Sin stock' : item.isCritical ? 'Bajo stock crítico' : 'Bajo stock'}
              </span>
            </td>
          </>
        )}
      />
    </>
  );
}
