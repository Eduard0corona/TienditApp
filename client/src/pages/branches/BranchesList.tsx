import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import PageTitle from '../../components/common/PageTitle';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import { branchesApi, type Branch } from '../../api/branchesApi';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { activeBranchesKey } from '../../hooks/useWorkingBranch';
import { BranchServiceMode } from '../../types';

const COLUMNS = [
  { header: 'Sucursal' },
  { header: 'Contacto' },
  { header: 'Ciudad / Estado' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-end' },
];

interface BranchForm {
  name: string;
  code: string;
  description: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  state: string;
  country: string;
  postalCode: string;
  contactName: string;
  contactPhone: string;
  isDefault: boolean;
  serviceMode: BranchServiceMode;
}

const EMPTY_FORM: BranchForm = {
  name: '',
  code: '',
  description: '',
  addressLine1: '',
  addressLine2: '',
  city: '',
  state: '',
  country: 'México',
  postalCode: '',
  contactName: '',
  contactPhone: '',
  isDefault: false,
  serviceMode: BranchServiceMode.Premises,
};

/** How a branch serves its customers; one that only delivers keeps its address for the team. */
const SERVICE_MODES = [
  {
    value: BranchServiceMode.Premises,
    label: 'Local abierto al público',
    hint: 'Tus clientes llegan a comer, a comprar o a recoger.',
  },
  {
    value: BranchServiceMode.DeliveryOnly,
    label: 'Solo a domicilio, sin local',
    hint: 'Solo entregas. Su dirección es para tu equipo y nunca se muestra a tus clientes.',
  },
];

const TEXT_FIELDS: Array<{ key: keyof Omit<BranchForm, 'isDefault' | 'name' | 'code' | 'serviceMode'>; label: string; col: string; maxLength: number }> = [
  { key: 'addressLine1', label: 'Calle y número', col: 'col-md-6', maxLength: 250 },
  { key: 'addressLine2', label: 'Colonia o referencia', col: 'col-md-6', maxLength: 250 },
  { key: 'city', label: 'Ciudad', col: 'col-md-4', maxLength: 120 },
  { key: 'state', label: 'Estado', col: 'col-md-4', maxLength: 120 },
  { key: 'postalCode', label: 'Código postal', col: 'col-md-4', maxLength: 20 },
  { key: 'contactName', label: 'Encargado', col: 'col-md-6', maxLength: 120 },
  { key: 'contactPhone', label: 'Teléfono', col: 'col-md-6', maxLength: 30 },
  { key: 'country', label: 'País', col: 'col-md-6', maxLength: 120 },
];

function toForm(branch: Branch): BranchForm {
  return {
    name: branch.name,
    code: branch.code,
    description: branch.description ?? '',
    addressLine1: branch.addressLine1 ?? '',
    addressLine2: branch.addressLine2 ?? '',
    city: branch.city ?? '',
    state: branch.state ?? '',
    country: branch.country ?? '',
    postalCode: branch.postalCode ?? '',
    contactName: branch.contactName ?? '',
    contactPhone: branch.contactPhone ?? '',
    isDefault: branch.isDefault,
    serviceMode: branch.serviceMode ?? BranchServiceMode.Premises,
  };
}

const blankToNull = (value: string) => value.trim() || null;

/**
 * Branches of the business: where it sells. Each one has its drawers, its sales in the reports and, with
 * inventory, its own stock. The default branch takes what names no branch and cannot be turned off.
 */
export default function BranchesList() {
  const titleId = useId();
  const toast = useToast();
  const { confirm } = useConfirm();
  const queryClient = useQueryClient();
  const [activeFilter, setActiveFilter] = useState<'' | 'true' | 'false'>('');
  const branches = useQuery({
    queryKey: ['branches', 'list', activeFilter],
    queryFn: () => branchesApi.getAll(activeFilter === '' ? undefined : activeFilter === 'true'),
  });
  const [editing, setEditing] = useState<Branch | null>(null);
  const [form, setForm] = useState<BranchForm | null>(null);
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['branches'] });
    await queryClient.invalidateQueries({ queryKey: activeBranchesKey });
  };

  const openCreate = () => {
    // A new branch starts serving like the main one: the second kitchen of a business without premises is one too.
    const main = branches.data?.find((branch) => branch.isDefault);
    setEditing(null);
    setForm({ ...EMPTY_FORM, serviceMode: main?.serviceMode ?? BranchServiceMode.Premises });
    setFormError('');
  };

  const openEdit = (branch: Branch) => {
    setEditing(branch);
    setForm(toForm(branch));
    setFormError('');
  };

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!form) return;
    if (!form.name.trim() || !form.code.trim()) {
      setFormError('Escribe el nombre y la clave de la sucursal.');
      return;
    }
    const fields = {
      name: form.name.trim(),
      code: form.code.trim().toUpperCase(),
      description: blankToNull(form.description),
      addressLine1: blankToNull(form.addressLine1),
      addressLine2: blankToNull(form.addressLine2),
      city: blankToNull(form.city),
      state: blankToNull(form.state),
      country: blankToNull(form.country),
      postalCode: blankToNull(form.postalCode),
      contactName: blankToNull(form.contactName),
      contactPhone: blankToNull(form.contactPhone),
    };
    // How it serves goes only when it is new or was changed here: a list read before someone else marked the branch
    // «Solo a domicilio» never turns it back into premises (left out, the server keeps what the branch has).
    const serviceMode = !editing || form.serviceMode !== (editing.serviceMode ?? BranchServiceMode.Premises)
      ? form.serviceMode
      : undefined;
    setSaving(true);
    setFormError('');
    try {
      if (editing) {
        await branchesApi.update(editing.id, { ...fields, id: editing.id, serviceMode });
        toast.success('Sucursal actualizada.');
      } else {
        await branchesApi.create({ ...fields, isDefault: form.isDefault, serviceMode });
        toast.success('Sucursal creada.');
      }
      setForm(null);
      await refresh();
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, 'No se pudo guardar la sucursal.'));
    } finally {
      setSaving(false);
    }
  };

  const makeDefault = async (branch: Branch) => {
    const accepted = await confirm({
      title: 'Cambiar la sucursal principal',
      message: `Las ventas y cajas que no elijan sucursal irán a ${branch.name}. ${branch.serviceMode === BranchServiceMode.DeliveryOnly
        ? 'Tu menú en línea dirá «Solo a domicilio» y no mostrará ninguna dirección.'
        : 'Su dirección se mostrará en tu menú en línea.'}`,
      confirmLabel: 'Hacer principal',
    });
    if (!accepted) return;
    try {
      await branchesApi.setDefault(branch.id);
      toast.success(`${branch.name} es ahora la sucursal principal.`);
      await refresh();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cambiar la sucursal principal.'));
    }
  };

  const toggleActive = async (branch: Branch) => {
    if (branch.isActive) {
      const accepted = await confirm({
        title: `Desactivar ${branch.name}`,
        message: 'Ya no se podrá vender ni abrir caja en ella. Sus ventas siguen en los reportes.',
        confirmLabel: 'Desactivar',
      });
      if (!accepted) return;
    }
    try {
      await branchesApi.setActive(branch.id, !branch.isActive);
      toast.success(branch.isActive ? 'Sucursal desactivada.' : 'Sucursal activada.');
      await refresh();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cambiar el estado de la sucursal.'));
    }
  };

  return (
    <>
      <PageTitle title="Sucursales" />

      <div className="card">
        <div className="card-body">
          {branches.isError && (
            <div className="alert alert-danger">{getApiErrorMessage(branches.error, 'No se pudieron cargar las sucursales.')}</div>
          )}

          <DataTable
            data={branches.data ?? []}
            columns={COLUMNS}
            keyExtractor={(item) => item.id}
            loading={branches.isLoading}
            emptyMessage="No hay sucursales."
            searchPlaceholder="Nombre, clave o ciudad"
            filters={(
              <DataTableFilter label="Estado" htmlFor="branches-filter">
                <select
                  id="branches-filter"
                  className="form-select form-select-sm"
                  value={activeFilter}
                  onChange={(event) => setActiveFilter(event.target.value as '' | 'true' | 'false')}
                >
                  <option value="">Todas</option>
                  <option value="true">Activas</option>
                  <option value="false">Inactivas</option>
                </select>
              </DataTableFilter>
            )}
            toolbarEnd={(
              <button type="button" className="btn btn-primary btn-sm" onClick={openCreate}>
                <i className="ri-add-line me-1" aria-hidden="true"></i>Nueva sucursal
              </button>
            )}
            searchFilter={(item, search) =>
              [item.name, item.code, item.city, item.state, item.contactName]
                .some((value) => (value ?? '').toLowerCase().includes(search))
            }
            renderRow={(item) => (
              <>
                <td>
                  <div className="fw-semibold">{item.name}</div>
                  <div className="text-muted small">
                    {item.code}
                    {item.isDefault && <span className="badge text-bg-info ms-2">Principal</span>}
                    {item.serviceMode === BranchServiceMode.DeliveryOnly && (
                      <span className="badge text-bg-light border ms-2">Solo a domicilio</span>
                    )}
                  </div>
                </td>
                <td>
                  <div>{item.contactName || '—'}</div>
                  <div className="text-muted small">{item.contactPhone || '—'}</div>
                </td>
                <td>
                  <div>{item.city || '—'}</div>
                  <div className="text-muted small">{item.state || '—'}</div>
                </td>
                <td className="text-center">
                  <span className={`badge ${item.isActive ? 'text-bg-success' : 'text-bg-secondary'}`}>
                    {item.isActive ? 'Activa' : 'Inactiva'}
                  </span>
                </td>
                <td className="text-end text-nowrap">
                  {!item.isDefault && item.isActive && (
                    <button type="button" className="btn btn-sm btn-light me-1" onClick={() => void makeDefault(item)}
                      aria-label={`Hacer principal ${item.name}`}>
                      Hacer principal
                    </button>
                  )}
                  <button type="button" className="btn btn-sm btn-light me-1" onClick={() => openEdit(item)}
                    aria-label={`Editar ${item.name}`}>
                    Editar
                  </button>
                  {!item.isDefault && (
                    <button type="button" className="btn btn-sm btn-light" onClick={() => void toggleActive(item)}
                      aria-label={item.isActive ? `Desactivar ${item.name}` : `Activar ${item.name}`}>
                      {item.isActive ? 'Desactivar' : 'Activar'}
                    </button>
                  )}
                </td>
              </>
            )}
          />
        </div>
      </div>

      <Modal show={form !== null} onHide={() => setForm(null)} size="lg" centered aria-labelledby={titleId}>
        {form && (
          <form onSubmit={(event) => void save(event)}>
            <Modal.Header closeButton>
              <Modal.Title id={titleId}>{editing ? `Editar ${editing.name}` : 'Nueva sucursal'}</Modal.Title>
            </Modal.Header>
            <Modal.Body>
              <div className="row g-2">
                <div className="col-md-6">
                  <label className="form-label" htmlFor="branch-name">Nombre</label>
                  <input id="branch-name" className="form-control" maxLength={200} required placeholder="Ej. Sucursal Centro"
                    value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} />
                </div>
                <div className="col-md-3">
                  <label className="form-label" htmlFor="branch-code">Clave</label>
                  <input id="branch-code" className="form-control text-uppercase" maxLength={50} required placeholder="CENTRO"
                    value={form.code} onChange={(event) => setForm({ ...form, code: event.target.value })} />
                </div>
                {!editing && (
                  <div className="col-md-3 d-flex align-items-end">
                    <div className="form-check mb-2">
                      <input id="branch-default" type="checkbox" className="form-check-input" checked={form.isDefault}
                        onChange={(event) => setForm({ ...form, isDefault: event.target.checked })} />
                      <label className="form-check-label" htmlFor="branch-default">Sucursal principal</label>
                    </div>
                  </div>
                )}
                <div className="col-12">
                  <label className="form-label" htmlFor="branch-description">Descripción (opcional)</label>
                  <input id="branch-description" className="form-control" maxLength={500}
                    value={form.description} onChange={(event) => setForm({ ...form, description: event.target.value })} />
                </div>
                {/* A radio group whose title matches the other labels of the form, as in DeliveryPlatformForm. */}
                <div className="col-12" role="radiogroup" aria-labelledby="branch-service-mode-label">
                  <div id="branch-service-mode-label" className="form-label">¿Cómo atiende?</div>
                  {SERVICE_MODES.map((mode) => (
                    <div className="form-check" key={mode.value}>
                      <input className="form-check-input" type="radio" name="branch-service-mode" id={`branch-service-mode-${mode.value}`}
                        checked={form.serviceMode === mode.value} aria-describedby={`branch-service-mode-${mode.value}-hint`}
                        onChange={() => setForm({ ...form, serviceMode: mode.value })} />
                      <label className="form-check-label" htmlFor={`branch-service-mode-${mode.value}`}>{mode.label}</label>
                      <div id={`branch-service-mode-${mode.value}-hint`} className="form-text mt-0">{mode.hint}</div>
                    </div>
                  ))}
                </div>
                {TEXT_FIELDS.map((field) => (
                  <div key={field.key} className={field.col}>
                    <label className="form-label" htmlFor={`branch-${field.key}`}>{field.label}</label>
                    <input id={`branch-${field.key}`} className="form-control" maxLength={field.maxLength}
                      value={form[field.key]} onChange={(event) => setForm({ ...form, [field.key]: event.target.value })} />
                  </div>
                ))}
              </div>
              {(editing ? editing.isDefault : form.isDefault) && (form.serviceMode === BranchServiceMode.DeliveryOnly ? (
                <p className="text-muted small mt-3 mb-0">
                  <i className="ri-lock-line me-1" aria-hidden="true"></i>
                  Tu menú en línea dice «Solo a domicilio» y no muestra esta dirección. Solo la ve tu equipo.
                </p>
              ) : (
                <p className="text-muted small mt-3 mb-0">
                  <i className="ri-map-pin-line me-1" aria-hidden="true"></i>
                  La dirección de la sucursal principal se muestra en tu menú en línea, con un enlace al mapa.
                </p>
              ))}
              {!editing && (
                <p className="text-muted small mt-3 mb-0">
                  Si el negocio usa inventario, la sucursal tendrá su propio almacén y sus ventas descontarán de él.
                </p>
              )}
              {formError && <div className="alert alert-danger mt-3 mb-0">{formError}</div>}
            </Modal.Body>
            <Modal.Footer>
              <button type="button" className="btn btn-light" onClick={() => setForm(null)}>Cancelar</button>
              <button type="submit" className="btn btn-primary" disabled={saving}>
                {saving ? 'Guardando…' : editing ? 'Guardar cambios' : 'Crear sucursal'}
              </button>
            </Modal.Footer>
          </form>
        )}
      </Modal>
    </>
  );
}
