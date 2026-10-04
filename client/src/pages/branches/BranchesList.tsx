import { useCallback, useEffect, useState } from 'react';
import { Modal } from 'react-bootstrap';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { branchesApi } from '../../api/branchesApi';
import type {
  BranchDto,
  CreateBranchRequest,
  UpdateBranchRequest,
} from '../../types';
import { getApiErrorMessage } from '../../api/errors';

const COLUMNS = [
  { header: 'Sucursal' },
  { header: 'Contacto' },
  { header: 'Ciudad / Estado' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

interface BranchFormState {
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
}

const buildEmptyForm = (): BranchFormState => ({
  name: '',
  code: '',
  description: '',
  addressLine1: '',
  addressLine2: '',
  city: '',
  state: '',
  country: '',
  postalCode: '',
  contactName: '',
  contactPhone: '',
  isDefault: false,
});

export default function BranchesList() {
  const [branches, setBranches] = useState<BranchDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [activeFilter, setActiveFilter] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [showModal, setShowModal] = useState(false);
  const [editing, setEditing] = useState<BranchDto | null>(null);
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState<BranchFormState>(buildEmptyForm());

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const params: { isActive?: boolean } = {};
      if (activeFilter !== '') params.isActive = activeFilter === 'true';
      const response = await branchesApi.getAll(params);
      setBranches(response.data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron cargar las sucursales.'));
    } finally {
      setLoading(false);
    }
  }, [activeFilter]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const openCreate = () => {
    setEditing(null);
    setForm(buildEmptyForm());
    setShowModal(true);
  };

  const openEdit = (branch: BranchDto) => {
    setEditing(branch);
    setForm({
      name: branch.name,
      code: branch.code,
      description: branch.description || '',
      addressLine1: branch.addressLine1 || '',
      addressLine2: branch.addressLine2 || '',
      city: branch.city || '',
      state: branch.state || '',
      country: branch.country || '',
      postalCode: branch.postalCode || '',
      contactName: branch.contactName || '',
      contactPhone: branch.contactPhone || '',
      isDefault: branch.isDefault,
    });
    setShowModal(true);
  };

  const submit = async () => {
    setError('');
    setSuccess('');

    if (!form.name.trim()) {
      setError('El nombre de la sucursal es obligatorio.');
      return;
    }

    if (!form.code.trim()) {
      setError('El codigo de la sucursal es obligatorio.');
      return;
    }

    const payload: CreateBranchRequest = {
      name: form.name.trim(),
      code: form.code.trim().toUpperCase(),
      description: form.description.trim() || undefined,
      addressLine1: form.addressLine1.trim() || undefined,
      addressLine2: form.addressLine2.trim() || undefined,
      city: form.city.trim() || undefined,
      state: form.state.trim() || undefined,
      country: form.country.trim() || undefined,
      postalCode: form.postalCode.trim() || undefined,
      contactName: form.contactName.trim() || undefined,
      contactPhone: form.contactPhone.trim() || undefined,
      isDefault: form.isDefault,
    };

    try {
      setSaving(true);
      if (editing) {
        const updatePayload: UpdateBranchRequest = {
          ...payload,
          id: editing.id,
        };
        await branchesApi.update(editing.id, updatePayload);
        setSuccess('Sucursal actualizada.');
      } else {
        await branchesApi.create(payload);
        setSuccess('Sucursal creada.');
      }

      setShowModal(false);
      await loadData();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar la sucursal.'));
    } finally {
      setSaving(false);
    }
  };

  const toggleActive = async (branch: BranchDto) => {
    setError('');
    setSuccess('');

    try {
      await branchesApi.setActive(branch.id, !branch.isActive);
      setSuccess('Estado actualizado.');
      await loadData();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo actualizar el estado.'));
    }
  };

  return (
    <>
      <PageTitle title="Sucursales" subtitle="Operacion" />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError('')}></button>
        </div>
      )}
      {success && (
        <div className="alert alert-success alert-dismissible fade show">
          {success}
          <button type="button" className="btn-close" onClick={() => setSuccess('')}></button>
        </div>
      )}

      <div className="card">
        <div className="card-body">
          <div className="d-flex justify-content-between align-items-center mb-2">
            <div className="d-flex align-items-center gap-2">
              <label className="form-label mb-0">Estado</label>
              <select
                className="form-select form-select-sm"
                value={activeFilter}
                onChange={(e) => setActiveFilter(e.target.value)}
              >
                <option value="">Todos</option>
                <option value="true">Activas</option>
                <option value="false">Inactivas</option>
              </select>
            </div>
            <button className="btn btn-primary btn-sm" onClick={openCreate}>
              <i className="ri-add-line me-1"></i>Nueva sucursal
            </button>
          </div>

          <DataTable
            data={branches}
            columns={COLUMNS}
            keyExtractor={(item) => item.id}
            loading={loading}
            emptyMessage="No hay sucursales registradas."
            searchFilter={(item, search) =>
              item.name.toLowerCase().includes(search) ||
              item.code.toLowerCase().includes(search) ||
              (item.city || '').toLowerCase().includes(search) ||
              (item.state || '').toLowerCase().includes(search) ||
              (item.contactName || '').toLowerCase().includes(search)
            }
            renderRow={(item) => (
              <>
                <td>
                  <div className="fw-semibold">{item.name}</div>
                  <div className="text-muted small">
                    {item.code}
                    {item.isDefault && <span className="badge bg-info ms-2">Principal</span>}
                  </div>
                </td>
                <td>
                  <div>{item.contactName || '-'}</div>
                  <div className="text-muted small">{item.contactPhone || '-'}</div>
                </td>
                <td>
                  <div>{item.city || '-'}</div>
                  <div className="text-muted small">{item.state || '-'}</div>
                </td>
                <td className="text-center">
                  <span className={`badge ${item.isActive ? 'bg-success' : 'bg-danger'}`}>
                    {item.isActive ? 'Activa' : 'Inactiva'}
                  </span>
                </td>
                <td className="text-center">
                  <button className="btn btn-sm btn-soft-primary me-1" onClick={() => openEdit(item)}>
                    <i className="ri-pencil-line"></i>
                  </button>
                  <button
                    className={`btn btn-sm ${item.isActive ? 'btn-soft-warning' : 'btn-soft-success'}`}
                    onClick={() => toggleActive(item)}
                  >
                    <i className={item.isActive ? 'ri-pause-line' : 'ri-play-line'}></i>
                  </button>
                </td>
              </>
            )}
          />
        </div>
      </div>

      <Modal show={showModal} onHide={() => setShowModal(false)} size="lg" centered>
        <Modal.Header closeButton>
          <Modal.Title>{editing ? 'Editar sucursal' : 'Nueva sucursal'}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <div className="row">
            <div className="col-md-6 mb-2">
              <label className="form-label">Nombre *</label>
              <input
                type="text"
                className="form-control"
                value={form.name}
                onChange={(e) => setForm((current) => ({ ...current, name: e.target.value }))}
              />
            </div>
            <div className="col-md-3 mb-2">
              <label className="form-label">Codigo *</label>
              <input
                type="text"
                className="form-control"
                value={form.code}
                onChange={(e) => setForm((current) => ({ ...current, code: e.target.value }))}
              />
            </div>
            <div className="col-md-3 mb-2 d-flex align-items-end">
              <div className="form-check">
                <input
                  className="form-check-input"
                  type="checkbox"
                  checked={form.isDefault}
                  onChange={(e) => setForm((current) => ({ ...current, isDefault: e.target.checked }))}
                />
                <label className="form-check-label">Sucursal principal</label>
              </div>
            </div>

            <div className="col-12 mb-2">
              <label className="form-label">Descripcion</label>
              <textarea
                className="form-control"
                rows={2}
                value={form.description}
                onChange={(e) => setForm((current) => ({ ...current, description: e.target.value }))}
              />
            </div>

            <div className="col-md-6 mb-2">
              <label className="form-label">Direccion linea 1</label>
              <input
                type="text"
                className="form-control"
                value={form.addressLine1}
                onChange={(e) => setForm((current) => ({ ...current, addressLine1: e.target.value }))}
              />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Direccion linea 2</label>
              <input
                type="text"
                className="form-control"
                value={form.addressLine2}
                onChange={(e) => setForm((current) => ({ ...current, addressLine2: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Ciudad</label>
              <input
                type="text"
                className="form-control"
                value={form.city}
                onChange={(e) => setForm((current) => ({ ...current, city: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Estado</label>
              <input
                type="text"
                className="form-control"
                value={form.state}
                onChange={(e) => setForm((current) => ({ ...current, state: e.target.value }))}
              />
            </div>
            <div className="col-md-4 mb-2">
              <label className="form-label">Codigo postal</label>
              <input
                type="text"
                className="form-control"
                value={form.postalCode}
                onChange={(e) => setForm((current) => ({ ...current, postalCode: e.target.value }))}
              />
            </div>

            <div className="col-md-6 mb-2">
              <label className="form-label">Contacto</label>
              <input
                type="text"
                className="form-control"
                value={form.contactName}
                onChange={(e) => setForm((current) => ({ ...current, contactName: e.target.value }))}
              />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Telefono contacto</label>
              <input
                type="text"
                className="form-control"
                value={form.contactPhone}
                onChange={(e) => setForm((current) => ({ ...current, contactPhone: e.target.value }))}
              />
            </div>
            <div className="col-12 mb-0">
              <label className="form-label">Pais</label>
              <input
                type="text"
                className="form-control"
                value={form.country}
                onChange={(e) => setForm((current) => ({ ...current, country: e.target.value }))}
              />
            </div>
          </div>
        </Modal.Body>
        <Modal.Footer>
          <button className="btn btn-light" onClick={() => setShowModal(false)}>Cancelar</button>
          <button className="btn btn-primary" onClick={submit} disabled={saving}>
            {saving ? 'Guardando...' : editing ? 'Guardar cambios' : 'Crear sucursal'}
          </button>
        </Modal.Footer>
      </Modal>
    </>
  );
}
