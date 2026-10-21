import { useId, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useStartInventoryCount, type InventoryCount } from '../../api/inventoryCounts';
import type { InventoryLocationDto, SupplyItemDto } from '../../types';
import SupplyPicker from './SupplyPicker';

type Scope = 'all' | 'category' | 'chosen';

interface NewCountModalProps {
  show: boolean;
  activeLocations: InventoryLocationDto[];
  supplyOptions: SupplyItemDto[];
  onHide: () => void;
  onStarted: (count: InventoryCount) => void;
}

/** What a count takes: everything with stock in the location, one category of it, or the items chosen. */
export default function NewCountModal({ show, activeLocations, supplyOptions, onHide, onStarted }: NewCountModalProps) {
  const titleId = useId();
  const start = useStartInventoryCount();
  const [locationId, setLocationId] = useState(
    () => (activeLocations.find((location) => location.isDefault) ?? activeLocations[0])?.id ?? '',
  );
  const [scope, setScope] = useState<Scope>('all');
  const [category, setCategory] = useState('');
  const [chosen, setChosen] = useState<string[]>([]);
  const [notes, setNotes] = useState('');
  const [error, setError] = useState('');

  const categories = useMemo(
    () => [...new Set(supplyOptions.map((supply) => supply.categoryName?.trim()).filter((name): name is string => Boolean(name)))]
      .sort((a, b) => a.localeCompare(b, 'es')),
    [supplyOptions],
  );

  const submit = async () => {
    setError('');
    if (!locationId) {
      setError('Elige la ubicación que vas a contar.');
      return;
    }
    if (scope === 'category' && !category) {
      setError('Elige la categoría que vas a contar.');
      return;
    }
    if (scope === 'chosen' && chosen.length === 0) {
      setError('Elige al menos un artículo.');
      return;
    }

    try {
      const count = await start.mutateAsync({
        inventoryLocationId: locationId,
        categoryName: scope === 'category' ? category : null,
        supplyItemIds: scope === 'chosen' ? chosen : [],
        notes: notes.trim() || null,
      });
      onStarted(count);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo empezar el conteo.'));
    }
  };

  return (
    <Modal show={show} onHide={onHide} centered aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId}>Nuevo conteo</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger" role="alert">{error}</div>}
        <div className="mb-3">
          <label htmlFor="nuevo-conteo-ubicacion" className="form-label">Ubicación</label>
          <select
            id="nuevo-conteo-ubicacion"
            className="form-select"
            value={locationId}
            onChange={(e) => setLocationId(e.target.value)}
          >
            {activeLocations.length === 0 && <option value="">No hay ubicaciones activas</option>}
            {activeLocations.map((location) => (
              <option key={location.id} value={location.id}>{location.name}</option>
            ))}
          </select>
        </div>

        <fieldset className="mb-3">
          <legend className="form-label fs-6">¿Qué vas a contar?</legend>
          <div className="form-check">
            <input id="nuevo-conteo-todo" type="radio" name="nuevo-conteo-alcance" className="form-check-input"
              checked={scope === 'all'} onChange={() => setScope('all')} />
            <label htmlFor="nuevo-conteo-todo" className="form-check-label">Todo lo que tiene existencias en esta ubicación</label>
          </div>
          <div className="form-check">
            <input id="nuevo-conteo-categoria" type="radio" name="nuevo-conteo-alcance" className="form-check-input"
              checked={scope === 'category'} onChange={() => setScope('category')} disabled={categories.length === 0} />
            <label htmlFor="nuevo-conteo-categoria" className="form-check-label">Una categoría</label>
          </div>
          <div className="form-check">
            <input id="nuevo-conteo-elegidos" type="radio" name="nuevo-conteo-alcance" className="form-check-input"
              checked={scope === 'chosen'} onChange={() => setScope('chosen')} />
            <label htmlFor="nuevo-conteo-elegidos" className="form-check-label">Solo los artículos que elija</label>
          </div>
        </fieldset>

        {scope === 'category' && (
          <div className="mb-3">
            <label htmlFor="nuevo-conteo-cual-categoria" className="form-label">Categoría</label>
            <select
              id="nuevo-conteo-cual-categoria"
              className="form-select"
              value={category}
              onChange={(e) => setCategory(e.target.value)}
            >
              <option value="">Elige una categoría</option>
              {categories.map((name) => <option key={name} value={name}>{name}</option>)}
            </select>
          </div>
        )}

        {scope === 'chosen' && (
          <div className="mb-3">
            <SupplyPicker supplies={supplyOptions} selected={chosen} onChange={setChosen} idPrefix="nuevo-conteo-articulo" />
          </div>
        )}

        <div>
          <label htmlFor="nuevo-conteo-notas" className="form-label">Notas (opcional)</label>
          <textarea
            id="nuevo-conteo-notas"
            className="form-control"
            rows={2}
            maxLength={500}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
        </div>
        <p className="form-text mb-0 mt-2">
          Cuenta lo que hay en el anaquel. Lo que se venda mientras cuentas no se pierde: cada diferencia se mide contra lo
          que había cuando contaste ese artículo.
        </p>
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onHide}>Cancelar</button>
        <button type="button" className="btn btn-primary" onClick={submit} disabled={start.isPending}>
          {start.isPending ? 'Empezando…' : 'Empezar conteo'}
        </button>
      </Modal.Footer>
    </Modal>
  );
}
