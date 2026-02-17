import { useCallback, useEffect, useState } from 'react';
import { Modal } from 'react-bootstrap';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { appointmentsApi } from '../../api/appointmentsApi';
import type {
  AppointmentDto,
  AppointmentStatus,
  CreateAppointmentRequest,
  PaginatedList,
} from '../../types';
import { AppointmentStatus as AppointmentStatusConst } from '../../types';

const COLUMNS = [
  { header: 'Cliente' },
  { header: 'Servicio' },
  { header: 'Horario' },
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

interface FormState {
  customerName: string;
  customerPhone: string;
  serviceName: string;
  staffName: string;
  startAt: string;
  endAt: string;
  notes: string;
}

const buildForm = (): FormState => ({
  customerName: '',
  customerPhone: '',
  serviceName: '',
  staffName: '',
  startAt: '',
  endAt: '',
  notes: '',
});

const getErrorMessage = (err: unknown, fallback: string) =>
  (err as { response?: { data?: { error?: string } } })?.response?.data?.error || fallback;

const statusLabel = (status: AppointmentStatus) => {
  switch (status) {
    case AppointmentStatusConst.Scheduled: return 'Programada';
    case AppointmentStatusConst.Confirmed: return 'Confirmada';
    case AppointmentStatusConst.InService: return 'En servicio';
    case AppointmentStatusConst.Completed: return 'Completada';
    case AppointmentStatusConst.NoShow: return 'No asistió';
    case AppointmentStatusConst.Cancelled: return 'Cancelada';
    default: return String(status);
  }
};

const statusBadge = (status: AppointmentStatus) => {
  switch (status) {
    case AppointmentStatusConst.Completed: return 'bg-success';
    case AppointmentStatusConst.Confirmed: return 'bg-primary';
    case AppointmentStatusConst.InService: return 'bg-info';
    case AppointmentStatusConst.NoShow: return 'bg-warning text-dark';
    case AppointmentStatusConst.Cancelled: return 'bg-danger';
    default: return 'bg-secondary';
  }
};

export default function AppointmentsList() {
  const [data, setData] = useState<PaginatedList<AppointmentDto>>(EMPTY_PAGINATED);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [showModal, setShowModal] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState<FormState>(buildForm());

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const params: {
        status?: AppointmentStatus;
        pageNumber: number;
        pageSize: number;
      } = { pageNumber, pageSize };
      if (statusFilter !== '') params.status = Number(statusFilter) as AppointmentStatus;
      const { data } = await appointmentsApi.getAll(params);
      setData(data);
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'No se pudieron cargar las citas.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, statusFilter]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const submit = async () => {
    setError('');
    setSuccess('');
    if (!form.customerName.trim() || !form.customerPhone.trim() || !form.serviceName.trim()) {
      setError('Cliente, teléfono y servicio son obligatorios.');
      return;
    }
    if (!form.startAt || !form.endAt) {
      setError('Debes indicar horario de inicio y fin.');
      return;
    }

    const payload: CreateAppointmentRequest = {
      customerName: form.customerName.trim(),
      customerPhone: form.customerPhone.trim(),
      serviceName: form.serviceName.trim(),
      staffName: form.staffName.trim() || undefined,
      startAt: new Date(form.startAt).toISOString(),
      endAt: new Date(form.endAt).toISOString(),
      notes: form.notes.trim() || undefined,
    };

    try {
      setSaving(true);
      await appointmentsApi.create(payload);
      setSuccess('Cita registrada.');
      setShowModal(false);
      setForm(buildForm());
      await loadData();
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'No se pudo registrar la cita.'));
    } finally {
      setSaving(false);
    }
  };

  const updateStatus = async (item: AppointmentDto, newStatus: AppointmentStatus) => {
    setError('');
    setSuccess('');
    try {
      await appointmentsApi.updateStatus(item.id, newStatus);
      setSuccess('Estado de cita actualizado.');
      await loadData();
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'No se pudo actualizar el estado.'));
    }
  };

  return (
    <>
      <PageTitle title="Citas" subtitle="Agenda del negocio" />

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
                value={statusFilter}
                onChange={(e) => {
                  setStatusFilter(e.target.value);
                  setPageNumber(1);
                }}
              >
                <option value="">Todos</option>
                <option value={AppointmentStatusConst.Scheduled}>Programada</option>
                <option value={AppointmentStatusConst.Confirmed}>Confirmada</option>
                <option value={AppointmentStatusConst.InService}>En servicio</option>
                <option value={AppointmentStatusConst.Completed}>Completada</option>
                <option value={AppointmentStatusConst.NoShow}>No asistió</option>
                <option value={AppointmentStatusConst.Cancelled}>Cancelada</option>
              </select>
            </div>
            <button className="btn btn-primary btn-sm" onClick={() => setShowModal(true)}>
              <i className="ri-add-line me-1"></i>Nueva cita
            </button>
          </div>

          <DataTable
            data={data.items}
            columns={COLUMNS}
            keyExtractor={(item) => item.id}
            loading={loading}
            emptyMessage="No hay citas registradas."
            searchFilter={(item, search) =>
              item.customerName.toLowerCase().includes(search) ||
              item.customerPhone.toLowerCase().includes(search) ||
              item.serviceName.toLowerCase().includes(search) ||
              (item.staffName || '').toLowerCase().includes(search)
            }
            serverSide={{
              totalCount: data.totalCount,
              page: pageNumber,
              pageSize,
              onPageChange: setPageNumber,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPageNumber(1);
              },
            }}
            renderRow={(item) => (
              <>
                <td>
                  <div className="fw-semibold">{item.customerName}</div>
                  <div className="text-muted small">{item.customerPhone}</div>
                </td>
                <td>
                  <div>{item.serviceName}</div>
                  <div className="text-muted small">{item.staffName || '-'}</div>
                </td>
                <td>
                  <div>{new Date(item.startAt).toLocaleString('es-MX')}</div>
                  <div className="text-muted small">a {new Date(item.endAt).toLocaleString('es-MX')}</div>
                </td>
                <td className="text-center">
                  <span className={`badge ${statusBadge(item.status)}`}>{statusLabel(item.status)}</span>
                </td>
                <td className="text-center">
                  <select
                    className="form-select form-select-sm"
                    value={item.status}
                    onChange={(e) => updateStatus(item, Number(e.target.value) as AppointmentStatus)}
                  >
                    <option value={AppointmentStatusConst.Scheduled}>Programada</option>
                    <option value={AppointmentStatusConst.Confirmed}>Confirmada</option>
                    <option value={AppointmentStatusConst.InService}>En servicio</option>
                    <option value={AppointmentStatusConst.Completed}>Completada</option>
                    <option value={AppointmentStatusConst.NoShow}>No asistió</option>
                    <option value={AppointmentStatusConst.Cancelled}>Cancelada</option>
                  </select>
                </td>
              </>
            )}
          />
        </div>
      </div>

      <Modal show={showModal} onHide={() => setShowModal(false)} centered>
        <Modal.Header closeButton>
          <Modal.Title>Nueva cita</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <div className="row">
            <div className="col-md-6 mb-2">
              <label className="form-label">Cliente *</label>
              <input className="form-control" value={form.customerName} onChange={(e) => setForm((x) => ({ ...x, customerName: e.target.value }))} />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Teléfono *</label>
              <input className="form-control" value={form.customerPhone} onChange={(e) => setForm((x) => ({ ...x, customerPhone: e.target.value }))} />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Servicio *</label>
              <input className="form-control" value={form.serviceName} onChange={(e) => setForm((x) => ({ ...x, serviceName: e.target.value }))} />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Colaborador</label>
              <input className="form-control" value={form.staffName} onChange={(e) => setForm((x) => ({ ...x, staffName: e.target.value }))} />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Inicio *</label>
              <input type="datetime-local" className="form-control" value={form.startAt} onChange={(e) => setForm((x) => ({ ...x, startAt: e.target.value }))} />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label">Fin *</label>
              <input type="datetime-local" className="form-control" value={form.endAt} onChange={(e) => setForm((x) => ({ ...x, endAt: e.target.value }))} />
            </div>
            <div className="col-12 mb-0">
              <label className="form-label">Notas</label>
              <textarea className="form-control" rows={2} value={form.notes} onChange={(e) => setForm((x) => ({ ...x, notes: e.target.value }))} />
            </div>
          </div>
        </Modal.Body>
        <Modal.Footer>
          <button className="btn btn-light" onClick={() => setShowModal(false)}>Cancelar</button>
          <button className="btn btn-primary" onClick={submit} disabled={saving}>
            {saving ? 'Guardando...' : 'Registrar cita'}
          </button>
        </Modal.Footer>
      </Modal>
    </>
  );
}
