import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { inventoryApi } from '../../api/inventoryApi';
import type { InventoryLocationDto, InventoryMovementReason, InventoryMovementType, SupplyItemDto } from '../../types';
import {
  buildEmptyMovementForm,
  buildEmptyMovementLineForm,
  toMovementRequest,
  type MovementFormState,
  type MovementLineFormState,
} from './inventoryForms';
import { MOVEMENT_REASON_OPTIONS, MOVEMENT_TYPE_OPTIONS } from './inventoryLabels';

interface MovementModalProps {
  show: boolean;
  supplyOptions: SupplyItemDto[];
  /** Only active locations receive or give stock. */
  activeLocations: InventoryLocationDto[];
  onHide: () => void;
  onSaved: (message: string) => void;
}

/** An entry, exit, adjustment or transfer with one line per supply and location. */
export default function MovementModal({ show, supplyOptions, activeLocations, onHide, onSaved }: MovementModalProps) {
  const titleId = useId();
  const [form, setForm] = useState<MovementFormState>(buildEmptyMovementForm);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const change = (patch: Partial<MovementFormState>) => setForm((current) => ({ ...current, ...patch }));

  const updateLine = (index: number, patch: Partial<MovementLineFormState>) => {
    setForm((current) => ({
      ...current,
      lines: current.lines.map((line, lineIndex) => (lineIndex === index ? { ...line, ...patch } : line)),
    }));
  };

  const addLine = () => {
    setForm((current) => ({ ...current, lines: [...current.lines, buildEmptyMovementLineForm()] }));
  };

  const removeLine = (index: number) => {
    setForm((current) => (current.lines.length <= 1
      ? current
      : { ...current, lines: current.lines.filter((_, lineIndex) => lineIndex !== index) }));
  };

  const submit = async () => {
    setError('');
    const result = toMovementRequest(form);
    if (result.error !== undefined) {
      setError(result.error);
      return;
    }

    try {
      setSaving(true);
      await inventoryApi.createMovement(result.request);
      onSaved('Movimiento registrado correctamente.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al registrar movimiento.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal show={show} onHide={onHide} size="xl" centered aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId}>Registrar movimiento de inventario</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger" role="alert">{error}</div>}
        <div className="row mb-2">
          <div className="col-md-3 mb-2">
            <label htmlFor="inventorymanagement-tipo-2" className="form-label">Tipo *</label>
            <select id="inventorymanagement-tipo-2"
              className="form-select"
              value={form.movementType}
              onChange={(e) => change({ movementType: Number(e.target.value) as InventoryMovementType })}
            >
              {MOVEMENT_TYPE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>{option.label}</option>
              ))}
            </select>
          </div>
          <div className="col-md-3 mb-2">
            <label htmlFor="inventorymanagement-motivo-2" className="form-label">Motivo *</label>
            <select id="inventorymanagement-motivo-2"
              className="form-select"
              value={form.movementReason}
              onChange={(e) => change({ movementReason: Number(e.target.value) as InventoryMovementReason })}
            >
              {MOVEMENT_REASON_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>{option.label}</option>
              ))}
            </select>
          </div>
          <div className="col-md-3 mb-2">
            <label htmlFor="inventorymanagement-fecha-y-hora" className="form-label">Fecha y hora</label>
            <input id="inventorymanagement-fecha-y-hora"
              type="datetime-local"
              className="form-control"
              value={form.movementDate}
              onChange={(e) => change({ movementDate: e.target.value })}
            />
          </div>
          <div className="col-md-3 mb-2">
            <label htmlFor="inventorymanagement-referencia" className="form-label">Referencia</label>
            <input id="inventorymanagement-referencia"
              type="text"
              className="form-control"
              value={form.referenceNumber}
              onChange={(e) => change({ referenceNumber: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-referencia-externa" className="form-label">Referencia externa</label>
            <input id="inventorymanagement-referencia-externa"
              type="text"
              className="form-control"
              value={form.externalReference}
              onChange={(e) => change({ externalReference: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-contraparte" className="form-label">Contraparte</label>
            <input id="inventorymanagement-contraparte"
              type="text"
              className="form-control"
              value={form.counterpartyName}
              onChange={(e) => change({ counterpartyName: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-documento" className="form-label">Documento</label>
            <input id="inventorymanagement-documento"
              type="text"
              className="form-control"
              value={form.documentNumber}
              onChange={(e) => change({ documentNumber: e.target.value })}
            />
          </div>
          <div className="col-12 mb-2">
            <label htmlFor="inventorymanagement-notas-2" className="form-label">Notas</label>
            <textarea id="inventorymanagement-notas-2"
              className="form-control"
              rows={2}
              value={form.notes}
              onChange={(e) => change({ notes: e.target.value })}
            />
          </div>
        </div>

        <div className="d-flex justify-content-between align-items-center mb-2">
          <h5 className="mb-0">Detalles</h5>
          <button type="button" className="btn btn-sm btn-light" onClick={addLine}>
            <i className="ri-add-line me-1"></i>Agregar línea
          </button>
        </div>

        {form.lines.map((line, index) => (
          <div className="border rounded p-2 mb-2" key={`line-${index}`}>
            <div className="d-flex justify-content-between align-items-center mb-2">
              <strong>Línea {index + 1}</strong>
              {form.lines.length > 1 && (
                <button
                  type="button"
                  className="btn btn-sm btn-soft-danger"
                  aria-label={`Quitar la línea ${index + 1}`}
                  onClick={() => removeLine(index)}
                >
                  <i className="ri-delete-bin-line"></i>
                </button>
              )}
            </div>
            <div className="row">
              <div className="col-md-4 mb-2">
                <label htmlFor={`inventorymanagement-insumo-${index}`} className="form-label">Insumo *</label>
                <select id={`inventorymanagement-insumo-${index}`}
                  className="form-select"
                  value={line.supplyItemId}
                  onChange={(e) => updateLine(index, { supplyItemId: e.target.value })}
                >
                  <option value="">Seleccionar...</option>
                  {supplyOptions.map((option) => (
                    <option key={option.id} value={option.id}>
                      {option.name} ({option.code || option.sku || '-'})
                    </option>
                  ))}
                </select>
              </div>
              <div className="col-md-3 mb-2">
                <label htmlFor={`inventorymanagement-ubicacion-${index}`} className="form-label">Ubicación *</label>
                <select id={`inventorymanagement-ubicacion-${index}`}
                  className="form-select"
                  value={line.inventoryLocationId}
                  onChange={(e) => updateLine(index, { inventoryLocationId: e.target.value })}
                >
                  <option value="">Seleccionar...</option>
                  {activeLocations.map((option) => (
                    <option key={option.id} value={option.id}>
                      {option.name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="col-md-2 mb-2">
                <label htmlFor={`inventorymanagement-cantidad-${index}`} className="form-label">Cantidad *</label>
                <input id={`inventorymanagement-cantidad-${index}`}
                  type="number"
                  step="0.01"
                  className="form-control"
                  value={line.quantity}
                  onChange={(e) => updateLine(index, { quantity: e.target.value })}
                />
              </div>
              <div className="col-md-3 mb-2">
                <label htmlFor={`inventorymanagement-costo-unitario-${index}`} className="form-label">Costo unitario</label>
                <input id={`inventorymanagement-costo-unitario-${index}`}
                  type="number"
                  step="0.01"
                  className="form-control"
                  value={line.unitCost}
                  onChange={(e) => updateLine(index, { unitCost: e.target.value })}
                />
              </div>
              <div className="col-md-3 mb-2">
                <label htmlFor={`inventorymanagement-lote-${index}`} className="form-label">Lote</label>
                <input id={`inventorymanagement-lote-${index}`}
                  type="text"
                  className="form-control"
                  value={line.lotNumber}
                  onChange={(e) => updateLine(index, { lotNumber: e.target.value })}
                />
              </div>
              <div className="col-md-3 mb-2">
                <label htmlFor={`inventorymanagement-caducidad-${index}`} className="form-label">Caducidad</label>
                <input id={`inventorymanagement-caducidad-${index}`}
                  type="date"
                  className="form-control"
                  value={line.expirationDate}
                  onChange={(e) => updateLine(index, { expirationDate: e.target.value })}
                />
              </div>
              <div className="col-md-6 mb-2">
                <label htmlFor={`inventorymanagement-notas-${index}`} className="form-label">Notas</label>
                <input id={`inventorymanagement-notas-${index}`}
                  type="text"
                  className="form-control"
                  value={line.notes}
                  onChange={(e) => updateLine(index, { notes: e.target.value })}
                />
              </div>
            </div>
          </div>
        ))}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onHide}>
          Cancelar
        </button>
        <button type="button" className="btn btn-primary" onClick={submit} disabled={saving}>
          {saving ? 'Guardando...' : 'Registrar movimiento'}
        </button>
      </Modal.Footer>
    </Modal>
  );
}
