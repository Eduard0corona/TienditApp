import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { tablesApi, type DiningTable } from '../../api/tablesApi';
import { useToast } from '../../hooks/useToast';

interface TablesConfigModalProps {
  onClose: () => void;
}

interface TableForm {
  id?: string;
  name: string;
  area: string;
  seats: string;
  sortOrder: number;
  isActive: boolean;
}

const EMPTY: TableForm = { name: '', area: '', seats: '4', sortOrder: 0, isActive: true };

/** Adds, renames and deactivates the tables of the dining room. */
export default function TablesConfigModal({ onClose }: TablesConfigModalProps) {
  const titleId = useId();
  const toast = useToast();
  const queryClient = useQueryClient();
  const tables = useQuery({
    queryKey: ['tables', 'all'],
    queryFn: () => tablesApi.getDiningRoom(true),
  });
  const [form, setForm] = useState<TableForm | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const list = tables.data?.tables ?? [];

  const edit = (table: DiningTable) => setForm({
    id: table.id,
    name: table.name,
    area: table.area ?? '',
    seats: table.seats ? String(table.seats) : '',
    sortOrder: table.sortOrder,
    isActive: table.isActive,
  });

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!form) return;
    setSaving(true);
    setError('');
    const body = {
      name: form.name.trim(),
      area: form.area.trim() || null,
      seats: form.seats ? Number(form.seats) : null,
      sortOrder: form.sortOrder,
    };
    try {
      if (form.id) await tablesApi.update(form.id, { ...body, id: form.id, isActive: form.isActive });
      else await tablesApi.create(body);
      toast.success(form.id ? 'Mesa actualizada.' : `Mesa ${body.name} agregada.`);
      setForm(null);
      await queryClient.invalidateQueries({ queryKey: ['tables'] });
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar la mesa.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal show onHide={onClose} centered size="lg" scrollable aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">Configurar mesas</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger">{error}</div>}
        {form ? (
          <form id="table-form" onSubmit={(event) => void save(event)}>
            <div className="row g-2">
              <div className="col-sm-4">
                <label className="form-label" htmlFor="table-name">Nombre</label>
                <input id="table-name" className="form-control" maxLength={30} required autoFocus placeholder="Mesa 1"
                  value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} />
              </div>
              <div className="col-sm-4">
                <label className="form-label" htmlFor="table-area">Área (opcional)</label>
                <input id="table-area" className="form-control" maxLength={50} placeholder="Terraza"
                  value={form.area} onChange={(event) => setForm({ ...form, area: event.target.value })} />
              </div>
              <div className="col-sm-2">
                <label className="form-label" htmlFor="table-seats">Lugares</label>
                <input id="table-seats" className="form-control" type="number" min={1} max={100}
                  value={form.seats} onChange={(event) => setForm({ ...form, seats: event.target.value })} />
              </div>
              <div className="col-sm-2">
                <label className="form-label" htmlFor="table-order">Orden</label>
                <input id="table-order" className="form-control" type="number"
                  value={form.sortOrder} onChange={(event) => setForm({ ...form, sortOrder: Number(event.target.value) || 0 })} />
              </div>
              {form.id && (
                <div className="col-12">
                  <div className="form-check form-switch">
                    <input id="table-active" type="checkbox" role="switch" className="form-check-input"
                      checked={form.isActive} onChange={(event) => setForm({ ...form, isActive: event.target.checked })} />
                    <label className="form-check-label" htmlFor="table-active">Activa (aparece en Mesas)</label>
                  </div>
                </div>
              )}
            </div>
          </form>
        ) : tables.isPending ? (
          <p className="text-muted">Cargando…</p>
        ) : list.length === 0 ? (
          <p className="text-muted">Todavía no hay mesas. Agrega la primera.</p>
        ) : (
          <ul className="list-group">
            {list.map((table) => (
              <li key={table.id} className="list-group-item d-flex align-items-center justify-content-between gap-2">
                <span>
                  <strong>{table.name}</strong>
                  {table.area && <span className="text-muted"> · {table.area}</span>}
                  {table.seats && <span className="text-muted"> · {table.seats} lugares</span>}
                  {!table.isActive && <span className="badge bg-light text-dark ms-2">Desactivada</span>}
                </span>
                <button type="button" className="btn btn-sm btn-light" onClick={() => edit(table)}>Editar</button>
              </li>
            ))}
          </ul>
        )}
      </Modal.Body>
      <Modal.Footer>
        {form ? (
          <>
            <button type="button" className="btn btn-light" onClick={() => setForm(null)} disabled={saving}>Volver</button>
            <button type="submit" form="table-form" className="btn btn-primary" disabled={saving}>{saving ? 'Guardando…' : 'Guardar'}</button>
          </>
        ) : (
          <>
            <button type="button" className="btn btn-light" onClick={onClose}>Cerrar</button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => setForm({ ...EMPTY, name: `Mesa ${list.length + 1}`, sortOrder: list.length + 1 })}
            >
              <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar mesa
            </button>
          </>
        )}
      </Modal.Footer>
    </Modal>
  );
}
