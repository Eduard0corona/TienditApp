import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { inventoryApi } from '../../api/inventoryApi';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { UnitType, type InventoryLocationDto, type ProductDto, type ProductStockDto, type SupplyItemDto } from '../../types';
import { allowsFractions, formatQuantity, parseQuantity } from '../../utils/units';

interface ProductStockCardProps {
  product: ProductDto;
  /** Inventory permission and module: without them the stock is only shown. */
  canManage: boolean;
}

const SERVICE_UNITS: (UnitType | undefined | null)[] = [UnitType.Hour, UnitType.Session, UnitType.Service];

/**
 * The stock of a product in inventory: one model for everything that moves stock. Each unit sold takes one from the
 * product's item; purchases and adjustments are inventory movements.
 */
export default function ProductStockCard({ product, canManage }: ProductStockCardProps) {
  const toast = useToast();
  const { confirm } = useConfirm();
  const [stock, setStock] = useState<ProductStockDto | null>(null);
  const [locations, setLocations] = useState<InventoryLocationDto[]>([]);
  const [items, setItems] = useState<SupplyItemDto[]>([]);
  const [loading, setLoading] = useState(canManage);
  const [saving, setSaving] = useState(false);
  const [mode, setMode] = useState<'new' | 'existing'>('new');
  const [initialQuantity, setInitialQuantity] = useState('');
  const [locationId, setLocationId] = useState('');
  const [unitCost, setUnitCost] = useState('');
  const [reorderPoint, setReorderPoint] = useState('');
  const [itemId, setItemId] = useState('');
  const isService = SERVICE_UNITS.includes(product.unit);

  const load = useCallback(async () => {
    if (!canManage) return;
    try {
      const [{ data: current }, { data: activeLocations }, { data: supplies }] = await Promise.all([
        inventoryApi.getProductStock(product.id),
        inventoryApi.getLocations({ isActive: true }),
        inventoryApi.getSupplies({ isActive: true, pageSize: 100 }),
      ]);
      setStock(current);
      setLocations(activeLocations);
      setLocationId((selected) => selected || activeLocations.find((l) => l.isDefault)?.id || activeLocations[0]?.id || '');
      // An item the business already keeps for this product: same unit and not the stock of another product.
      setItems(supplies.items.filter((item) => !item.productId && item.unit === (product.unit ?? UnitType.Unit)));
      setInitialQuantity((value) => value || (current.capturedStockQuantity != null ? String(current.capturedStockQuantity) : ''));
      setReorderPoint((value) => value || (current.capturedLowStockThreshold != null ? String(current.capturedLowStockThreshold) : ''));
    } catch (err) {
      toast.error(getApiErrorMessage(err, 'No se pudieron cargar las existencias.'));
    } finally {
      setLoading(false);
    }
  }, [canManage, product.id, product.unit, toast]);

  useEffect(() => {
    load();
  }, [load]);

  const start = async () => {
    const text = initialQuantity.trim();
    const quantity = text === '' || Number(text.replace(',', '.')) === 0 ? 0 : parseQuantity(text, product.unit);
    if (mode === 'new' && quantity === null) {
      toast.error(allowsFractions(product.unit) ? 'Escribe una existencia válida.' : 'La existencia es en piezas enteras.');
      return;
    }
    if (mode === 'existing' && !itemId) {
      toast.error('Elige el artículo de inventario.');
      return;
    }

    setSaving(true);
    try {
      const { data } = await inventoryApi.trackProductStock(product.id, mode === 'existing'
        ? { supplyItemId: itemId, initialQuantity: 0, reorderPoint: 0 }
        : {
            initialQuantity: quantity ?? 0,
            inventoryLocationId: locationId || null,
            unitCost: unitCost.trim() === '' ? null : Number(unitCost),
            reorderPoint: reorderPoint.trim() === '' ? 0 : Number(reorderPoint),
          });
      setStock(data);
      toast.success(`${product.name} ya controla sus existencias.`);
    } catch (err) {
      toast.error(getApiErrorMessage(err, 'No se pudo empezar a controlar las existencias.'));
    } finally {
      setSaving(false);
    }
  };

  const stop = async () => {
    const accepted = await confirm({
      title: 'Dejar de controlar existencias',
      message: `Las ventas de ${product.name} ya no descontarán existencias. Su artículo se queda en Inventario con sus movimientos.`,
      confirmLabel: 'Dejar de controlar',
      danger: true,
    });
    if (!accepted) return;

    setSaving(true);
    try {
      const { data } = await inventoryApi.stopTrackingProductStock(product.id);
      setStock(data);
      toast.success('Listo: el producto ya no controla existencias.');
    } catch (err) {
      toast.error(getApiErrorMessage(err, 'No se pudo dejar de controlar las existencias.'));
    } finally {
      setSaving(false);
    }
  };

  const tracks = canManage ? stock?.tracksStock ?? false : product.tracksStock;
  const onHand = canManage ? stock?.onHand ?? 0 : product.onHand ?? 0;
  const isLow = canManage ? stock?.isLowStock ?? false : product.isLowStock;

  return (
    <div className="card">
      <div className="card-body">
        <h5 className="header-title mb-1">Existencias</h5>
        <p className="text-muted small mb-3">
          Cada venta descuenta una unidad por unidad vendida; las compras y ajustes se registran en Inventario.
        </p>

        {isService ? (
          <p className="text-muted mb-0">Los servicios no tienen existencias.</p>
        ) : loading ? (
          <p className="text-muted mb-0">Cargando existencias…</p>
        ) : tracks ? (
          <>
            <div className="d-flex align-items-baseline gap-2 mb-2">
              <span className="fs-3 fw-semibold">{formatQuantity(onHand, product.unit)}</span>
              {onHand <= 0 ? (
                <span className="badge bg-danger-subtle text-danger">Agotado</span>
              ) : isLow ? (
                <span className="badge bg-warning-subtle text-warning-emphasis">Poca existencia</span>
              ) : null}
            </div>
            {canManage && stock && (
              <>
                {stock.locations.length > 1 && (
                  <ul className="list-unstyled small mb-2">
                    {stock.locations.map((location) => (
                      <li key={location.inventoryLocationId}>
                        {location.locationName}: {formatQuantity(location.onHand, product.unit)}
                      </li>
                    ))}
                  </ul>
                )}
                <p className="small text-muted mb-3">Punto de reorden: {formatQuantity(stock.reorderPoint, product.unit)}</p>
                <div className="d-flex flex-wrap gap-2">
                  <Link to="/inventory" className="btn btn-outline-primary btn-sm">Registrar entrada o ajuste</Link>
                  <button type="button" className="btn btn-outline-danger btn-sm" onClick={stop} disabled={saving}>
                    Dejar de controlar
                  </button>
                </div>
              </>
            )}
            {!canManage && <p className="small text-muted mb-0">Quien maneja Inventario registra entradas y ajustes.</p>}
          </>
        ) : !canManage ? (
          <p className="text-muted mb-0">Este producto no controla existencias. Quien maneja Inventario puede activarlas.</p>
        ) : (
          <>
            {items.length > 0 && (
              <div className="btn-group mb-3" role="group" aria-label="Origen de la existencia">
                <button type="button" className={`btn btn-sm ${mode === 'new' ? 'btn-primary' : 'btn-outline-primary'}`} onClick={() => setMode('new')}>
                  Empezar desde cero
                </button>
                <button type="button" className={`btn btn-sm ${mode === 'existing' ? 'btn-primary' : 'btn-outline-primary'}`} onClick={() => setMode('existing')}>
                  Usar un artículo de inventario
                </button>
              </div>
            )}

            {mode === 'existing' ? (
              <div className="mb-3">
                <label className="form-label" htmlFor="stock-item">Artículo de inventario</label>
                <select id="stock-item" className="form-select" value={itemId} onChange={(e) => setItemId(e.target.value)}>
                  <option value="">Elige el artículo…</option>
                  {items.map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.name} ({formatQuantity(item.totalOnHand, item.unit)})
                    </option>
                  ))}
                </select>
                <div className="form-text">Conserva sus existencias y su historial.</div>
              </div>
            ) : (
              <div className="row">
                <div className="col-sm-6 mb-3">
                  <label className="form-label" htmlFor="stock-initial">Existencia inicial</label>
                  <input
                    id="stock-initial"
                    type="text"
                    inputMode="decimal"
                    className="form-control"
                    value={initialQuantity}
                    onChange={(e) => setInitialQuantity(e.target.value)}
                    placeholder="0"
                  />
                  {stock?.capturedStockQuantity != null && (
                    <div className="form-text">Antes se había capturado {stock.capturedStockQuantity}; confírmalo contando.</div>
                  )}
                </div>
                <div className="col-sm-6 mb-3">
                  <label className="form-label" htmlFor="stock-location">Ubicación</label>
                  <select id="stock-location" className="form-select" value={locationId} onChange={(e) => setLocationId(e.target.value)}>
                    {locations.map((location) => (
                      <option key={location.id} value={location.id}>{location.name}</option>
                    ))}
                  </select>
                </div>
                <div className="col-sm-6 mb-3">
                  <label className="form-label" htmlFor="stock-cost">Costo por unidad (opcional)</label>
                  <input
                    id="stock-cost"
                    type="number"
                    min="0"
                    step="0.01"
                    className="form-control"
                    value={unitCost}
                    onChange={(e) => setUnitCost(e.target.value)}
                  />
                </div>
                <div className="col-sm-6 mb-3">
                  <label className="form-label" htmlFor="stock-reorder">Avisar cuando queden</label>
                  <input
                    id="stock-reorder"
                    type="number"
                    min="0"
                    className="form-control"
                    value={reorderPoint}
                    onChange={(e) => setReorderPoint(e.target.value)}
                    placeholder="0"
                  />
                </div>
              </div>
            )}

            <button type="button" className="btn btn-primary" onClick={start} disabled={saving || (mode === 'new' && locations.length === 0)}>
              Controlar existencias
            </button>
            {mode === 'new' && locations.length === 0 && (
              <div className="form-text">Crea una ubicación en Inventario para registrar la existencia inicial.</div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
