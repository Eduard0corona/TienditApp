import DataTable from '../../components/common/DataTable';
import type { InventoryLocationDto, SupplyItemDto } from '../../types';
import { formatStockQuantity } from '../../utils/units';
import { effectiveRecipeQuantity } from './inventoryForms';
import type { RecipeEditor } from './useRecipeEditor';

const COLUMNS = [
  { header: 'Insumo' },
  { header: 'Ubicación' },
  { header: 'Cantidad', className: 'text-end' },
  { header: 'Merma %', className: 'text-end' },
  { header: 'Cantidad efectiva', className: 'text-end' },
  { header: 'Notas' },
  { header: 'Acciones', className: 'text-center' },
];

interface RecipesTabProps {
  editor: RecipeEditor;
  supplyOptions: SupplyItemDto[];
  locations: InventoryLocationDto[];
  activeLocations: InventoryLocationDto[];
}

/** The supplies each product or service takes: a sale takes them from the inventory. */
export default function RecipesTab({ editor, supplyOptions, locations, activeLocations }: RecipesTabProps) {
  return (
    <>
      <div className="row mb-3">
        <div className="col-md-6 mb-2">
          <label htmlFor="inventorymanagement-producto-o-servicio" className="form-label">Producto o servicio</label>
          <select id="inventorymanagement-producto-o-servicio"
            className="form-select"
            value={editor.productId}
            onChange={(e) => editor.selectProduct(e.target.value)}
          >
            <option value="">Seleccionar...</option>
            {editor.products.map((product) => (
              <option key={product.id} value={product.id}>
                {product.name}
              </option>
            ))}
          </select>
        </div>
        <div className="col-md-6 mb-2 d-flex align-items-end justify-content-between">
          <div>
            <div className="text-muted small">Ítem seleccionado</div>
            <div className="fw-semibold">{editor.productName || '-'}</div>
          </div>
          <div>
            <span className={`badge ${editor.productIsAvailable ? 'bg-success' : 'bg-secondary'}`}>
              {editor.productIsAvailable ? 'Disponible' : 'No disponible'}
            </span>
          </div>
        </div>
      </div>

      {editor.productId ? (
        <>
          <div className="d-flex justify-content-between align-items-center mb-2">
            <h5 className="mb-0">Receta de insumos</h5>
            <button type="button" className="btn btn-sm btn-light" onClick={editor.addLine}>
              <i className="ri-add-line me-1"></i>Agregar línea
            </button>
          </div>

          <DataTable
            data={editor.lines}
            columns={COLUMNS}
            keyExtractor={(item) => item.clientId}
            loading={editor.loading}
            emptyMessage="Este ítem aún no tiene receta."
            searchFilter={(item, search) => {
              const supplyName = supplyOptions.find((supply) => supply.id === item.supplyItemId)?.name || '';
              const locationName = locations.find((location) => location.id === item.inventoryLocationId)?.name || '';
              return (
                supplyName.toLowerCase().includes(search) ||
                locationName.toLowerCase().includes(search) ||
                item.notes.toLowerCase().includes(search)
              );
            }}
            renderRow={(item) => {
              const lineIndex = editor.lines.findIndex((line) => line.clientId === item.clientId);
              const lineNumber = lineIndex + 1;

              return (
                <>
                  <td>
                    <select
                      className="form-select form-select-sm"
                      aria-label={`Insumo de la línea ${lineNumber}`}
                      value={item.supplyItemId}
                      onChange={(e) => editor.updateLine(lineIndex, { supplyItemId: e.target.value })}
                    >
                      <option value="">Seleccionar...</option>
                      {supplyOptions.map((option) => (
                        <option key={option.id} value={option.id}>
                          {option.name} ({option.code || option.sku || '-'})
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <select
                      className="form-select form-select-sm"
                      aria-label={`Ubicación de la línea ${lineNumber}`}
                      value={item.inventoryLocationId}
                      onChange={(e) => editor.updateLine(lineIndex, { inventoryLocationId: e.target.value })}
                    >
                      <option value="">Ubicación por defecto</option>
                      {activeLocations.map((option) => (
                        <option key={option.id} value={option.id}>
                          {option.name}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td className="text-end" style={{ minWidth: 120 }}>
                    <input
                      type="number"
                      step="0.0001"
                      className="form-control form-control-sm text-end"
                      aria-label={`Cantidad de la línea ${lineNumber}`}
                      value={item.quantity}
                      onChange={(e) => editor.updateLine(lineIndex, { quantity: e.target.value })}
                    />
                  </td>
                  <td className="text-end" style={{ minWidth: 120 }}>
                    <input
                      type="number"
                      step="0.01"
                      className="form-control form-control-sm text-end"
                      aria-label={`Merma de la línea ${lineNumber}`}
                      value={item.wastePercent}
                      onChange={(e) => editor.updateLine(lineIndex, { wastePercent: e.target.value })}
                    />
                  </td>
                  <td className="text-end">{formatStockQuantity(effectiveRecipeQuantity(item))}</td>
                  <td style={{ minWidth: 220 }}>
                    <input
                      type="text"
                      className="form-control form-control-sm"
                      aria-label={`Notas de la línea ${lineNumber}`}
                      value={item.notes}
                      onChange={(e) => editor.updateLine(lineIndex, { notes: e.target.value })}
                    />
                  </td>
                  <td className="text-center">
                    <button
                      type="button"
                      className="btn btn-sm btn-soft-danger"
                      aria-label={`Quitar la línea ${lineNumber}`}
                      onClick={() => editor.removeLine(lineIndex)}
                    >
                      <i className="ri-delete-bin-line"></i>
                    </button>
                  </td>
                </>
              );
            }}
          />
        </>
      ) : (
        <p className="text-muted mb-0">Selecciona un producto o servicio para administrar su receta.</p>
      )}
    </>
  );
}
