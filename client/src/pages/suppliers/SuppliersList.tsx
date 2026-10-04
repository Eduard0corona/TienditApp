import { useCallback, useEffect, useState } from 'react';
import { Modal } from 'react-bootstrap';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { suppliersApi } from '../../api/suppliersApi';
import type { CreateSupplierRequest, PaginatedList, SupplierDto, UpdateSupplierRequest } from '../../types';
import { getApiErrorMessage } from '../../api/errors';

const COLUMNS = [
  { header: 'Proveedor' },
  { header: 'Contacto' },
  { header: 'Email / Telefono' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

const EMPTY_PAGINATED = {
  items: [],
  pageNumber: 1,
  totalPages: 0,
  totalCount: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

interface SupplierFormState {
  name: string;
  contactName: string;
  phone: string;
  email: string;
  taxId: string;
  address: string;
  notes: string;
}

const buildEmptyForm = (): SupplierFormState => ({
  name: '',
  contactName: '',
  phone: '',
  email: '',
  taxId: '',
  address: '',
  notes: '',
});

export default function SuppliersList() {
  const [data, setData] = useState<PaginatedList<SupplierDto>>(EMPTY_PAGINATED);
  const [loading, setLoading] = useState(true);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [activeFilter, setActiveFilter] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [showModal, setShowModal] = useState(false);
  const [editing, setEditing] = useState<SupplierDto | null>(null);
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState<SupplierFormState>(buildEmptyForm());
  const [search, setSearch] = useState('');

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const params: { isActive?: boolean; search?: string; pageNumber: number; pageSize: number } = {
        pageNumber,
        pageSize,
      };
      if (activeFilter !== '') params.isActive = activeFilter === 'true';
      if (search) params.search = search;
      const response = await suppliersApi.getAll(params);
      setData(response.data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron cargar los proveedores.'));
    } finally {
      setLoading(false);
    }
  }, [activeFilter, pageNumber, pageSize, search]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const openCreate = () => {
    setEditing(null);
    setForm(buildEmptyForm());
    setShowModal(true);
  };

  const openEdit = (supplier: SupplierDto) => {
    setEditing(supplier);
    setForm({
      name: supplier.name,
      contactName: supplier.contactName || '',
      phone: supplier.phone || '',
      email: supplier.email || '',
      taxId: supplier.taxId || '',
      address: supplier.address || '',
      notes: supplier.notes || '',
    });
    setShowModal(true);
  };

  const submit = async () => {
    setError('');
    setSuccess('');
    if (!form.name.trim()) {
      setError('El nombre del proveedor es obligatorio.');
      return;
    }

    const payload: CreateSupplierRequest = {
      name: form.name.trim(),
      contactName: form.contactName.trim() || undefined,
      phone: form.phone.trim() || undefined,
      email: form.email.trim() || undefined,
      taxId: form.taxId.trim() || undefined,
      address: form.address.trim() || undefined,
      notes: form.notes.trim() || undefined,
    };

    try {
      setSaving(true);
      if (editing) {
        const updatePayload: UpdateSupplierRequest = { ...payload, id: editing.id };
        await suppliersApi.update(editing.id, updatePayload);
        setSuccess('Proveedor actualizado.');
      } else {
        await suppliersApi.create(payload);
        setSuccess('Proveedor creado.');
      }
      setShowModal(false);
      await loadData();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar el proveedor.'));
    } finally {
      setSaving(false);
    }
  };

  const toggleActive = async (item: SupplierDto) => {
    setError('');
    setSuccess('');
    try {
      await suppliersApi.setActive(item.id, !item.isActive);
      setSuccess('Estado actualizado.');
      await loadData();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo actualizar el estado.'));
    }
  };

  return (
    <>
      <PageTitle title="Proveedores" subtitle="Compras" />

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
                onChange={(e) => {
                  setActiveFilter(e.target.value);
                  setPageNumber(1);
                }}
              >
                <option value="">Todos</option>
                <option value="true">Activos</option>
                <option value="false">Inactivos</option>
              </select>
            </div>
            <button className="btn btn-primary btn-sm" onClick={openCreate}>
              <i className="ri-add-line me-1"></i>Nuevo proveedor
            </button>
          </div>

          <DataTable
            data={data.items}
            columns={COLUMNS}
            keyExtractor={(item) => item.id}
            loading={loading}
            emptyMessage="No hay proveedores registrados."
            searchPlaceholder="Nombre, contacto, teléfono o correo"
            serverSide={{
              totalCount: data.totalCount,
              page: pageNumber,
              pageSize,
              onPageChange: setPageNumber,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPageNumber(1);
              },
              onSearchChange: (term) => {
                setSearch(term);
                setPageNumber(1);
              },
            }}
            renderRow={(item) => (
              <>
                <td>
                  <div className="fw-semibold">{item.name}</div>
                  <div className="text-muted small">{item.taxId || '-'}</div>
                </td>
                <td>
                  <div>{item.contactName || '-'}</div>
                  <div className="text-muted small">{item.address || '-'}</div>
                </td>
                <td>
                  <div>{item.email || '-'}</div>
                  <div className="text-muted small">{item.phone || '-'}</div>
                </td>
                <td className="text-center">
                  <span className={`badge ${item.isActive ? 'bg-success' : 'bg-danger'}`}>
                    {item.isActive ? 'Activo' : 'Inactivo'}
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

      <Modal show={showModal} onHide={() => setShowModal(false)} centered>
        <Modal.Header closeButton>
          <Modal.Title>{editing ? 'Editar proveedor' : 'Nuevo proveedor'}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <div className="row">
            <div className="col-12 mb-2">
              <label className="form-label">Nombre *</label>
              <input
                className="form-control"
                value={form.name}
                onChange={(e) => setForm((x) => ({ ...x, name: e.target.value }))}
              />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Contacto</label>
              <input
                className="form-control"
                value={form.contactName}
                onChange={(e) => setForm((x) => ({ ...x, contactName: e.target.value }))}
              />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Telefono</label>
              <input
                className="form-control"
                value={form.phone}
                onChange={(e) => setForm((x) => ({ ...x, phone: e.target.value }))}
              />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Email</label>
              <input
                className="form-control"
                value={form.email}
                onChange={(e) => setForm((x) => ({ ...x, email: e.target.value }))}
              />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">RFC/NIF</label>
              <input
                className="form-control"
                value={form.taxId}
                onChange={(e) => setForm((x) => ({ ...x, taxId: e.target.value }))}
              />
            </div>
            <div className="col-12 mb-2">
              <label className="form-label">Direccion</label>
              <input
                className="form-control"
                value={form.address}
                onChange={(e) => setForm((x) => ({ ...x, address: e.target.value }))}
              />
            </div>
            <div className="col-12">
              <label className="form-label">Notas</label>
              <textarea
                className="form-control"
                rows={2}
                value={form.notes}
                onChange={(e) => setForm((x) => ({ ...x, notes: e.target.value }))}
              />
            </div>
          </div>
        </Modal.Body>
        <Modal.Footer>
          <button className="btn btn-light" onClick={() => setShowModal(false)}>Cancelar</button>
          <button className="btn btn-primary" onClick={submit} disabled={saving}>
            {saving ? 'Guardando...' : editing ? 'Guardar cambios' : 'Crear proveedor'}
          </button>
        </Modal.Footer>
      </Modal>
    </>
  );
}
