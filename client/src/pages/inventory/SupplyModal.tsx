import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { inventoryApi } from '../../api/inventoryApi';
import type { SupplyItemDto, UnitType, UpdateSupplyItemRequest } from '../../types';
import { buildEmptySupplyForm, toSupplyForm, toSupplyRequest, type SupplyFormState } from './inventoryForms';
import { UNIT_OPTIONS } from './inventoryLabels';

interface SupplyModalProps {
  show: boolean;
  /** The supply to edit; null for a new one. */
  supply: SupplyItemDto | null;
  onHide: () => void;
  onSaved: (message: string) => void;
}

/** A supply's data, its stock limits and how it is tracked. Its problems show here, where the person is looking. */
export default function SupplyModal({ show, supply, onHide, onSaved }: SupplyModalProps) {
  const [form, setForm] = useState<SupplyFormState>(() => (supply ? toSupplyForm(supply) : buildEmptySupplyForm()));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const change = (patch: Partial<SupplyFormState>) => setForm((current) => ({ ...current, ...patch }));
  // The stock of a product is counted in the product's unit (the API refuses another).
  const isProductStock = !!supply?.productId;

  const submit = async () => {
    setError('');
    const result = toSupplyRequest(form);
    if (result.error !== undefined) {
      setError(result.error);
      return;
    }

    try {
      setSaving(true);
      if (supply) {
        const request: UpdateSupplyItemRequest = { ...result.request, id: supply.id };
        await inventoryApi.updateSupply(supply.id, request);
        onSaved('Insumo actualizado correctamente.');
      } else {
        await inventoryApi.createSupply(result.request);
        onSaved('Insumo creado correctamente.');
      }
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al guardar el insumo.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal show={show} onHide={onHide} size="lg" centered>
      <Modal.Header closeButton>
        <Modal.Title>{supply ? 'Editar insumo' : 'Nuevo insumo'}</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger" role="alert">{error}</div>}
        <div className="row">
          <div className="col-md-6 mb-2">
            <label htmlFor="inventorymanagement-nombre" className="form-label">Nombre *</label>
            <input id="inventorymanagement-nombre"
              type="text"
              className="form-control"
              value={form.name}
              onChange={(e) => change({ name: e.target.value })}
            />
          </div>
          <div className="col-md-3 mb-2">
            <label htmlFor="inventorymanagement-codigo" className="form-label">Código</label>
            <input id="inventorymanagement-codigo"
              type="text"
              className="form-control"
              value={form.code}
              onChange={(e) => change({ code: e.target.value })}
            />
          </div>
          <div className="col-md-3 mb-2">
            <label htmlFor="inventorymanagement-sku" className="form-label">SKU</label>
            <input id="inventorymanagement-sku"
              type="text"
              className="form-control"
              value={form.sku}
              onChange={(e) => change({ sku: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-codigo-de-barras" className="form-label">Código de barras</label>
            <input id="inventorymanagement-codigo-de-barras"
              type="text"
              className="form-control"
              value={form.barcode}
              onChange={(e) => change({ barcode: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-categoria" className="form-label">Categoría</label>
            <input id="inventorymanagement-categoria"
              type="text"
              className="form-control"
              value={form.categoryName}
              onChange={(e) => change({ categoryName: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-unidad" className="form-label">Unidad</label>
            <select id="inventorymanagement-unidad"
              className="form-select"
              value={form.unit}
              disabled={isProductStock}
              aria-describedby={isProductStock ? 'inventorymanagement-unidad-producto' : undefined}
              onChange={(e) => change({ unit: Number(e.target.value) as UnitType })}
            >
              {UNIT_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            {isProductStock && (
              <div id="inventorymanagement-unidad-producto" className="form-text">Es la unidad del producto.</div>
            )}
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-marca" className="form-label">Marca</label>
            <input id="inventorymanagement-marca"
              type="text"
              className="form-control"
              value={form.brand}
              onChange={(e) => change({ brand: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-modelo" className="form-label">Modelo</label>
            <input id="inventorymanagement-modelo"
              type="text"
              className="form-control"
              value={form.model}
              onChange={(e) => change({ model: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-costo-por-defecto" className="form-label">Costo por defecto</label>
            <input id="inventorymanagement-costo-por-defecto"
              type="number"
              step="0.01"
              className="form-control"
              value={form.defaultCost}
              onChange={(e) => change({ defaultCost: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-stock-minimo" className="form-label">Stock mínimo</label>
            <input id="inventorymanagement-stock-minimo"
              type="number"
              step="0.01"
              className="form-control"
              value={form.minStock}
              onChange={(e) => change({ minStock: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-stock-maximo" className="form-label">Stock máximo</label>
            <input id="inventorymanagement-stock-maximo"
              type="number"
              step="0.01"
              className="form-control"
              value={form.maxStock}
              onChange={(e) => change({ maxStock: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-punto-de-reorden" className="form-label">Punto de reorden</label>
            <input id="inventorymanagement-punto-de-reorden"
              type="number"
              step="0.01"
              className="form-control"
              value={form.reorderPoint}
              onChange={(e) => change({ reorderPoint: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-impuesto" className="form-label">Impuesto (%)</label>
            <input id="inventorymanagement-impuesto"
              type="number"
              step="0.01"
              className="form-control"
              value={form.taxRate}
              onChange={(e) => change({ taxRate: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2 d-flex align-items-end">
            <div>
              <div className="form-check mb-1">
                <input id="inventorymanagement-control-por-lotes"
                  className="form-check-input"
                  type="checkbox"
                  checked={form.trackBatches}
                  onChange={(e) => change({ trackBatches: e.target.checked })}
                />
                <label htmlFor="inventorymanagement-control-por-lotes" className="form-check-label">Control por lotes</label>
              </div>
              <div className="form-check mb-1">
                <input id="inventorymanagement-control-caducidad"
                  className="form-check-input"
                  type="checkbox"
                  checked={form.trackExpiration}
                  onChange={(e) => change({ trackExpiration: e.target.checked })}
                />
                <label htmlFor="inventorymanagement-control-caducidad" className="form-check-label">Control caducidad</label>
              </div>
              <div className="form-check">
                <input id="inventorymanagement-es-perecedero"
                  className="form-check-input"
                  type="checkbox"
                  checked={form.isPerishable}
                  onChange={(e) => change({ isPerishable: e.target.checked })}
                />
                <label htmlFor="inventorymanagement-es-perecedero" className="form-check-label">Es perecedero</label>
              </div>
              <div className="form-check">
                <input id="inventorymanagement-insumo-critico"
                  className="form-check-input"
                  type="checkbox"
                  checked={form.isCritical}
                  onChange={(e) => change({ isCritical: e.target.checked })}
                />
                <label htmlFor="inventorymanagement-insumo-critico" className="form-check-label">Insumo crítico</label>
              </div>
            </div>
          </div>
          <div className="col-12 mb-2">
            <label htmlFor="inventorymanagement-descripcion" className="form-label">Descripción</label>
            <textarea id="inventorymanagement-descripcion"
              className="form-control"
              rows={2}
              value={form.description}
              onChange={(e) => change({ description: e.target.value })}
            />
          </div>
          <div className="col-12 mb-0">
            <label htmlFor="inventorymanagement-notas" className="form-label">Notas</label>
            <textarea id="inventorymanagement-notas"
              className="form-control"
              rows={2}
              value={form.notes}
              onChange={(e) => change({ notes: e.target.value })}
            />
          </div>
        </div>
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onHide}>
          Cancelar
        </button>
        <button type="button" className="btn btn-primary" onClick={submit} disabled={saving}>
          {saving ? 'Guardando...' : supply ? 'Guardar cambios' : 'Crear insumo'}
        </button>
      </Modal.Footer>
    </Modal>
  );
}
