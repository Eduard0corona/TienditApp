import { useId, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useCreateInventoryTransfer, useOriginStock, type InventoryTransfer } from '../../api/inventoryTransfers';
import { useScrollFade } from '../../hooks/useScrollFade';
import type { InventoryLocationDto, UnitType } from '../../types';
import { formatStockQuantity } from '../../utils/units';
import { newUuid } from '../../utils/uuid';
import { toTransferRequest } from './transferForm';

/** How many items of the origin the list shows at once: a search finds the rest. */
const SHOWN = 50;

interface ChosenLine {
  supplyItemId: string;
  name: string;
  unit: UnitType;
  quantity: string;
}

interface NewTransferModalProps {
  show: boolean;
  activeLocations: InventoryLocationDto[];
  onHide: () => void;
  onCreated: (transfer: InventoryTransfer) => void;
}

/**
 * A transfer between two locations (Fase 27B): the items with stock in the origin, how much of each, and they leave one
 * and enter the other at once. The dialog keeps one key, so sending it again never transfers twice.
 */
export default function NewTransferModal({ show, activeLocations, onHide, onCreated }: NewTransferModalProps) {
  const titleId = useId();
  const create = useCreateInventoryTransfer();
  const [idempotencyKey] = useState(newUuid);
  // Until one is chosen, the main location, and the first other one as destination: also when the locations arrive
  // after the dialog opened.
  const [chosenFrom, setChosenFrom] = useState('');
  const [chosenTo, setChosenTo] = useState('');
  const fromId = chosenFrom || ((activeLocations.find((location) => location.isDefault) ?? activeLocations[0])?.id ?? '');
  const toId = chosenTo && chosenTo !== fromId ? chosenTo : (activeLocations.find((location) => location.id !== fromId)?.id ?? '');
  const origin = activeLocations.find((location) => location.id === fromId);
  const [lines, setLines] = useState<ChosenLine[]>([]);
  const [search, setSearch] = useState('');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState('');

  const stock = useOriginStock(fromId);
  const onHand = useMemo(
    () => new Map((stock.data ?? []).map((row) => [row.supplyItemId, row.onHandQuantity])),
    [stock.data],
  );
  const offered = useMemo(() => {
    const chosen = new Set(lines.map((line) => line.supplyItemId));
    const term = search.trim().toLowerCase();
    return (stock.data ?? []).filter((row) => !chosen.has(row.supplyItemId) && (!term
      || [row.supplyItemName, row.supplyCode, row.supplySku, row.supplyBarcode].some((value) => value?.toLowerCase().includes(term))));
  }, [stock.data, lines, search]);
  const { ref, fadeClass } = useScrollFade<HTMLUListElement>(Math.min(offered.length, SHOWN), 'y');

  // Another origin has other stock: what was chosen goes.
  const changeOrigin = (locationId: string) => {
    setChosenFrom(locationId);
    setLines([]);
    setSearch('');
  };

  const updateQuantity = (supplyItemId: string, quantity: string) =>
    setLines((current) => current.map((line) => (line.supplyItemId === supplyItemId ? { ...line, quantity } : line)));

  const submit = async () => {
    setError('');
    const result = toTransferRequest(
      {
        fromLocationId: fromId,
        toLocationId: toId,
        notes,
        lines: lines.map((line) => ({ ...line, onHand: onHand.get(line.supplyItemId) ?? 0 })),
      },
      idempotencyKey,
      origin?.name ?? 'el origen',
    );
    if (result.error !== undefined) {
      setError(result.error);
      return;
    }

    try {
      onCreated(await create.mutateAsync(result.request));
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo hacer la transferencia.'));
    }
  };

  return (
    <Modal show={show} onHide={onHide} size="lg" centered aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId}>Nueva transferencia</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger" role="alert">{error}</div>}
        {activeLocations.length < 2 && (
          <div className="alert alert-info" role="status">
            Para transferir necesitas dos ubicaciones activas. Agrega otra en «Ubicaciones».
          </div>
        )}
        <div className="row g-2 mb-3">
          <div className="col-sm-6">
            <label htmlFor="transferencia-de" className="form-label">De</label>
            <select id="transferencia-de" className="form-select" value={fromId} onChange={(e) => changeOrigin(e.target.value)}>
              {activeLocations.length === 0 && <option value="">No hay ubicaciones activas</option>}
              {activeLocations.map((location) => (
                <option key={location.id} value={location.id}>{location.name}</option>
              ))}
            </select>
          </div>
          <div className="col-sm-6">
            <label htmlFor="transferencia-a" className="form-label">A</label>
            <select id="transferencia-a" className="form-select" value={toId} onChange={(e) => setChosenTo(e.target.value)}>
              {toId === '' && <option value="">Elige otra ubicación</option>}
              {activeLocations.filter((location) => location.id !== fromId).map((location) => (
                <option key={location.id} value={location.id}>{location.name}</option>
              ))}
            </select>
          </div>
        </div>

        <div className="mb-3">
          <label htmlFor="transferencia-buscar" className="form-label">
            Agregar artículo{origin ? ` con existencias en ${origin.name}` : ''}
          </label>
          <input
            id="transferencia-buscar"
            type="search"
            className="form-control mb-2"
            placeholder="Nombre, código o código de barras"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {stock.isError && (
            <div className="alert alert-danger" role="alert">{getApiErrorMessage(stock.error, 'No se pudo leer lo que hay en el origen.')}</div>
          )}
          {stock.isLoading && fromId !== '' && <p className="text-muted small mb-0" role="status">Leyendo lo que hay…</p>}
          {stock.isSuccess && (
            offered.length === 0 ? (
              <p className="text-muted small mb-0">
                {search.trim() ? 'Ningún artículo con existencias coincide.' : 'No quedan artículos con existencias por agregar.'}
              </p>
            ) : (
              <ul ref={ref} className={`list-group overflow-auto ta-scroll-fade-y ${fadeClass}`} style={{ maxHeight: '12rem' }}
                aria-label="Artículos con existencias">
                {offered.slice(0, SHOWN).map((row) => (
                  <li key={row.supplyItemId} className="list-group-item d-flex align-items-center justify-content-between gap-2 py-1">
                    <span className="text-wrap">
                      {row.supplyItemName}
                      <span className="text-muted small ms-1">hay {formatStockQuantity(row.onHandQuantity, row.unit)}</span>
                    </span>
                    <button
                      type="button"
                      className="btn btn-sm btn-light text-nowrap"
                      aria-label={`Agregar ${row.supplyItemName}`}
                      onClick={() => setLines((current) => [
                        ...current,
                        { supplyItemId: row.supplyItemId, name: row.supplyItemName, unit: row.unit, quantity: '' },
                      ])}
                    >
                      Agregar
                    </button>
                  </li>
                ))}
              </ul>
            )
          )}
          {offered.length > SHOWN && (
            <div className="form-text">Se muestran {SHOWN} de {offered.length}: escribe para encontrar los demás.</div>
          )}
        </div>

        {lines.length > 0 && (
          <div className="table-responsive mb-3">
            <table className="table table-sm align-middle mb-0">
              <caption className="visually-hidden">Artículos a transferir</caption>
              <thead>
                <tr>
                  <th scope="col">Artículo</th>
                  <th scope="col" className="text-end" style={{ width: '9rem' }}>Cantidad</th>
                  <th scope="col" className="text-center"><span className="visually-hidden">Quitar</span></th>
                </tr>
              </thead>
              <tbody>
                {lines.map((line) => (
                  <tr key={line.supplyItemId}>
                    <td>
                      <div className="text-wrap">{line.name}</div>
                      <div className="text-muted small">Hay {formatStockQuantity(onHand.get(line.supplyItemId) ?? 0, line.unit)}</div>
                    </td>
                    <td>
                      <input
                        type="text"
                        inputMode="decimal"
                        className="form-control form-control-sm text-end"
                        aria-label={`Cantidad de ${line.name}`}
                        value={line.quantity}
                        onChange={(e) => updateQuantity(line.supplyItemId, e.target.value)}
                      />
                    </td>
                    <td className="text-center">
                      <button
                        type="button"
                        className="btn btn-sm btn-link text-danger text-nowrap"
                        aria-label={`Quitar ${line.name}`}
                        onClick={() => setLines((current) => current.filter((item) => item.supplyItemId !== line.supplyItemId))}
                      >
                        Quitar
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div>
          <label htmlFor="transferencia-notas" className="form-label">Notas (opcional)</label>
          <textarea
            id="transferencia-notas"
            className="form-control"
            rows={2}
            maxLength={500}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
        </div>
        <p className="form-text mb-0 mt-2">
          Los artículos salen de una ubicación y entran a la otra al mismo tiempo, con el mismo valor. Para deshacerla, haz
          otra transferencia al revés.
        </p>
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onHide}>Cancelar</button>
        <button type="button" className="btn btn-primary" onClick={submit} disabled={create.isPending || activeLocations.length < 2}>
          {create.isPending ? 'Transfiriendo…' : 'Transferir'}
        </button>
      </Modal.Footer>
    </Modal>
  );
}
