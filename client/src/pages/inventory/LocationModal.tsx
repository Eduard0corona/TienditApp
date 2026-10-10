import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { inventoryApi } from '../../api/inventoryApi';
import { useActiveBranches } from '../../hooks/useWorkingBranch';
import type { InventoryLocationDto, UpdateInventoryLocationRequest } from '../../types';
import { buildEmptyLocationForm, toLocationForm, toLocationRequest, type LocationFormState } from './inventoryForms';

interface LocationModalProps {
  show: boolean;
  /** The location to edit; null for a new one. */
  location: InventoryLocationDto | null;
  onHide: () => void;
  onSaved: (message: string) => void;
}

/** A warehouse, the branch it serves and its contact. */
export default function LocationModal({ show, location, onHide, onSaved }: LocationModalProps) {
  const titleId = useId();
  const branches = useActiveBranches();
  const [form, setForm] = useState<LocationFormState>(() => (location ? toLocationForm(location) : buildEmptyLocationForm()));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const change = (patch: Partial<LocationFormState>) => setForm((current) => ({ ...current, ...patch }));

  const submit = async () => {
    setError('');
    const result = toLocationRequest(form);
    if (result.error !== undefined) {
      setError(result.error);
      return;
    }

    try {
      setSaving(true);
      if (location) {
        const request: UpdateInventoryLocationRequest = {
          ...result.request,
          id: location.id,
          detachFromBranch: !form.branchId,
        };
        await inventoryApi.updateLocation(location.id, request);
        onSaved('Ubicación actualizada correctamente.');
      } else {
        await inventoryApi.createLocation(result.request);
        onSaved('Ubicación creada correctamente.');
      }
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al guardar la ubicación.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal show={show} onHide={onHide} size="lg" centered aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId}>{location ? 'Editar ubicación' : 'Nueva ubicación'}</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger" role="alert">{error}</div>}
        <div className="row">
          <div className="col-md-6 mb-2">
            <label htmlFor="inventorymanagement-nombre-2" className="form-label">Nombre *</label>
            <input id="inventorymanagement-nombre-2"
              type="text"
              className="form-control"
              value={form.name}
              onChange={(e) => change({ name: e.target.value })}
            />
          </div>
          <div className="col-md-3 mb-2">
            <label htmlFor="inventorymanagement-codigo-2" className="form-label">Código *</label>
            <input id="inventorymanagement-codigo-2"
              type="text"
              className="form-control"
              value={form.code}
              onChange={(e) => change({ code: e.target.value })}
            />
          </div>
          <div className="col-md-3 mb-2 d-flex align-items-end">
            <div className="form-check">
              <input id="inventorymanagement-ubicacion-por-defecto"
                className="form-check-input"
                type="checkbox"
                checked={form.isDefault}
                onChange={(e) => change({ isDefault: e.target.checked })}
              />
              <label htmlFor="inventorymanagement-ubicacion-por-defecto" className="form-check-label">Ubicación por defecto</label>
            </div>
          </div>
          <div className="col-12 mb-2">
            <label className="form-label" htmlFor="location-branch">Sucursal</label>
            <select
              id="location-branch"
              className="form-select"
              value={form.branchId}
              onChange={(e) => change({ branchId: e.target.value })}
            >
              <option value="">Almacén general (surte a todas las sucursales)</option>
              {(branches.data ?? []).map((branch) => (
                <option key={branch.id} value={branch.id}>{branch.name}</option>
              ))}
            </select>
            <div className="form-text">Las ventas de una sucursal descuentan del almacén de esa sucursal.</div>
          </div>
          <div className="col-12 mb-2">
            <label htmlFor="inventorymanagement-descripcion-2" className="form-label">Descripción</label>
            <textarea id="inventorymanagement-descripcion-2"
              className="form-control"
              rows={2}
              value={form.description}
              onChange={(e) => change({ description: e.target.value })}
            />
          </div>
          <div className="col-md-6 mb-2">
            <label htmlFor="inventorymanagement-direccion-linea-1" className="form-label">Dirección línea 1</label>
            <input id="inventorymanagement-direccion-linea-1"
              type="text"
              className="form-control"
              value={form.addressLine1}
              onChange={(e) => change({ addressLine1: e.target.value })}
            />
          </div>
          <div className="col-md-6 mb-2">
            <label htmlFor="inventorymanagement-direccion-linea-2" className="form-label">Dirección línea 2</label>
            <input id="inventorymanagement-direccion-linea-2"
              type="text"
              className="form-control"
              value={form.addressLine2}
              onChange={(e) => change({ addressLine2: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-ciudad" className="form-label">Ciudad</label>
            <input id="inventorymanagement-ciudad"
              type="text"
              className="form-control"
              value={form.city}
              onChange={(e) => change({ city: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-estado-2" className="form-label">Estado</label>
            <input id="inventorymanagement-estado-2"
              type="text"
              className="form-control"
              value={form.state}
              onChange={(e) => change({ state: e.target.value })}
            />
          </div>
          <div className="col-md-4 mb-2">
            <label htmlFor="inventorymanagement-codigo-postal" className="form-label">Código postal</label>
            <input id="inventorymanagement-codigo-postal"
              type="text"
              className="form-control"
              value={form.postalCode}
              onChange={(e) => change({ postalCode: e.target.value })}
            />
          </div>
          <div className="col-md-6 mb-2">
            <label htmlFor="inventorymanagement-contacto" className="form-label">Contacto</label>
            <input id="inventorymanagement-contacto"
              type="text"
              className="form-control"
              value={form.contactName}
              onChange={(e) => change({ contactName: e.target.value })}
            />
          </div>
          <div className="col-md-6 mb-2">
            <label htmlFor="inventorymanagement-telefono-contacto" className="form-label">Teléfono contacto</label>
            <input id="inventorymanagement-telefono-contacto"
              type="text"
              className="form-control"
              value={form.contactPhone}
              onChange={(e) => change({ contactPhone: e.target.value })}
            />
          </div>
        </div>
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onHide}>
          Cancelar
        </button>
        <button type="button" className="btn btn-primary" onClick={submit} disabled={saving}>
          {saving ? 'Guardando...' : location ? 'Guardar cambios' : 'Crear ubicación'}
        </button>
      </Modal.Footer>
    </Modal>
  );
}
