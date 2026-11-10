import DataTable from '../../components/common/DataTable';
import type { InventoryAlertsDto } from '../../types';
import { formatStockQuantity } from '../../utils/units';

const COLUMNS = [
  { header: 'Insumo' },
  { header: 'Ubicación' },
  { header: 'Existencia', className: 'text-end' },
  { header: 'Pedir al llegar a', className: 'text-end' },
  { header: 'Déficit', className: 'text-end' },
  { header: 'Estado', className: 'text-center' },
];

const CARDS: Array<{ label: string; border: string; count: (alerts: InventoryAlertsDto) => number }> = [
  { label: 'Agotado', border: 'border-danger', count: (alerts) => alerts.outOfStockCount },
  { label: 'Bajo stock crítico', border: 'border-danger', count: (alerts) => alerts.criticalLowStockCount },
  { label: 'Bajo stock', border: 'border-warning', count: (alerts) => alerts.lowStockCount },
  { label: 'Ítems sin receta', border: 'border-info', count: (alerts) => alerts.missingRecipeProductCount },
];

interface AlertsTabProps {
  /** Null until they are read: the counts show a dash, not a zero that would say there is nothing to restock. */
  alerts: InventoryAlertsDto | null;
  loading: boolean;
}

/** What ran out or is about to, by location, and the items that still have no recipe. */
export default function AlertsTab({ alerts, loading }: AlertsTabProps) {
  return (
    <>
      <div className="row mb-3">
        {CARDS.map((card) => (
          <div key={card.label} className="col-md-3 mb-2">
            <div className={`card ${card.border} h-100 mb-0`}>
              <div className="card-body py-2">
                <div className="text-muted small">{card.label}</div>
                <div className="h3 mb-0">{alerts ? card.count(alerts) : '–'}</div>
              </div>
            </div>
          </div>
        ))}
      </div>

      <DataTable
        data={alerts?.items ?? []}
        columns={COLUMNS}
        keyExtractor={(item) => `${item.supplyItemId}-${item.inventoryLocationId}`}
        loading={loading}
        emptyMessage={alerts ? 'No hay alertas de stock actualmente.' : 'No se pudieron leer las alertas.'}
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
