import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import DataTable from '../../components/common/DataTable';
import PageTitle from '../../components/common/PageTitle';
import EmployeeFormModal from '../../components/employees/EmployeeFormModal';
import InvitationShare from '../../components/employees/InvitationShare';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { describeEmployeeRole, type EmployeeRecord } from '../../utils/teamRoles';

const COLUMNS = [
  { header: 'Persona' },
  { header: 'Rol' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Permisos' },
  { header: 'Acciones', className: 'text-end' },
];

const STATUS_BADGES: Record<string, { label: string; className: string }> = {
  Active: { label: 'Activo', className: 'bg-success' },
  Invited: { label: 'Invitación pendiente', className: 'bg-warning text-dark' },
  Disabled: { label: 'Desactivado', className: 'bg-secondary' },
};

interface ShareTarget {
  path: string;
  firstName: string;
  email: string;
  phone?: string | null;
}

/** Equipo: who enters the business, with which role, and their pending invitations. */
export default function EmployeesList() {
  const toast = useToast();
  const { confirm } = useConfirm();
  const { tenant } = useTenant();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [editing, setEditing] = useState<EmployeeRecord | 'new' | null>(null);
  const [share, setShare] = useState<ShareTarget | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const employees = useQuery({
    queryKey: ['employees', { page, pageSize, search, status }],
    queryFn: () => apiRequest('get', '/api/v1/employees', {
      query: { pageNumber: page, pageSize, search: search || undefined, status: status || undefined },
    }),
    placeholderData: keepPreviousData,
  });
  const presets = useQuery({
    queryKey: ['employees', 'role-presets'],
    queryFn: () => apiRequest('get', '/api/v1/employees/role-presets'),
    staleTime: Infinity,
  });
  const permissionOptions = useQuery({
    queryKey: ['employees', 'permission-options'],
    queryFn: () => apiRequest('get', '/api/v1/employees/permission-options'),
    staleTime: Infinity,
  });
  const presetList = presets.data ?? [];
  const optionNames = new Map((permissionOptions.data ?? []).map((option) => [option.code, option.name]));
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['employees'] });

  const onSaved = (saved: EmployeeRecord) => {
    setEditing(null);
    void refresh();
    if (saved.invitationUrl) {
      setShare({ path: saved.invitationUrl, firstName: saved.firstName, email: saved.email, phone: saved.phoneNumber });
    } else {
      toast.success(`${saved.firstName} ${saved.lastName}: guardado.`);
    }
  };

  const resendInvitation = async (employee: EmployeeRecord) => {
    setBusyId(employee.tenantUserId);
    try {
      const invitation = await apiRequest('post', '/api/v1/employees/{tenantUserId}/resend-invitation', {
        path: { tenantUserId: employee.tenantUserId },
      });
      setShare({ path: invitation.invitationUrl, firstName: employee.firstName, email: invitation.email, phone: employee.phoneNumber });
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo crear la invitación.'));
    } finally {
      setBusyId(null);
    }
  };

  const setActive = async (employee: EmployeeRecord, isActive: boolean) => {
    const name = `${employee.firstName} ${employee.lastName}`;
    if (!isActive) {
      const ok = await confirm({
        title: employee.status === 'Invited' ? 'Cancelar invitación' : 'Desactivar acceso',
        message: employee.status === 'Invited'
          ? `El enlace de ${name} deja de servir. Puedes volver a invitarle después.`
          : `${name} deja de entrar a este negocio. Su historial se conserva.`,
        confirmLabel: employee.status === 'Invited' ? 'Cancelar invitación' : 'Desactivar',
        danger: true,
      });
      if (!ok) return;
    }
    setBusyId(employee.tenantUserId);
    try {
      await apiRequest('patch', '/api/v1/employees/{tenantUserId}/active', {
        path: { tenantUserId: employee.tenantUserId },
        body: { isActive },
      });
      await refresh();
      toast.success(isActive ? `${name} puede entrar de nuevo.` : `${name} ya no tiene acceso.`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cambiar el acceso.'));
    } finally {
      setBusyId(null);
    }
  };

  const data = employees.data;

  return (
    <>
      <PageTitle title="Equipo" />

      <div className="card">
        <div className="card-body">
          <div className="d-flex flex-wrap justify-content-between gap-2 mb-2">
            <button type="button" className="btn btn-primary" onClick={() => setEditing('new')} disabled={!presets.data}>
              <i className="ri-user-add-line me-1" aria-hidden="true"></i>Invitar persona
            </button>
            <select
              className="form-select form-select-sm w-auto"
              aria-label="Estado"
              value={status}
              onChange={(event) => {
                setStatus(event.target.value);
                setPage(1);
              }}
            >
              <option value="">Todos</option>
              <option value="Active">Activos</option>
              <option value="Invited">Invitación pendiente</option>
              <option value="Disabled">Desactivados</option>
            </select>
          </div>

          {employees.isError && <div className="alert alert-danger">No se pudo cargar el equipo.</div>}

          <DataTable
            data={data?.items ?? []}
            columns={COLUMNS}
            keyExtractor={(item) => item.tenantUserId}
            loading={employees.isLoading}
            emptyMessage="Nadie en el equipo con ese filtro."
            searchPlaceholder="Nombre, correo o teléfono"
            serverSide={{
              totalCount: data?.totalCount ?? 0,
              page,
              pageSize,
              onPageChange: setPage,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPage(1);
              },
              onSearchChange: (term) => {
                setSearch(term);
                setPage(1);
              },
            }}
            renderRow={(employee) => {
              const badge = STATUS_BADGES[employee.status] ?? STATUS_BADGES.Disabled;
              const isOwner = employee.tenantRole === 'Owner';
              const isManager = isOwner || employee.tenantRole === 'Admin';
              const role = describeEmployeeRole(employee, presetList);
              const busy = busyId === employee.tenantUserId;
              return (
                <>
                  <td>
                    <div className="fw-semibold">{employee.firstName} {employee.lastName}</div>
                    <small className="text-muted d-block">{employee.email}</small>
                    {employee.phoneNumber && <small className="text-muted">{employee.phoneNumber}</small>}
                  </td>
                  <td>{role === 'Sin acceso' ? <span className="badge bg-warning-subtle text-warning">Sin acceso</span> : role}</td>
                  <td className="text-center"><span className={`badge ${badge.className}`}>{badge.label}</span></td>
                  <td>
                    <small className="text-muted">
                      {isManager ? 'Todo' : employee.permissions.map((code) => optionNames.get(code) ?? code).join(', ') || 'Ninguno'}
                    </small>
                  </td>
                  <td className="text-end text-nowrap">
                    {employee.status === 'Invited' && (
                      <button
                        type="button"
                        className="btn btn-sm btn-soft-success me-1"
                        disabled={busy}
                        onClick={() => void resendInvitation(employee)}
                      >
                        <i className="ri-send-plane-line me-1" aria-hidden="true"></i>Invitación
                      </button>
                    )}
                    <button
                      type="button"
                      className="btn btn-sm btn-soft-primary me-1"
                      aria-label={`Editar a ${employee.firstName} ${employee.lastName}`}
                      onClick={() => setEditing(employee)}
                      disabled={!presets.data}
                    >
                      <i className="ri-pencil-line" aria-hidden="true"></i>
                    </button>
                    {!isOwner && (
                      <button
                        type="button"
                        className="btn btn-sm btn-soft-secondary"
                        disabled={busy}
                        aria-label={`${employee.status === 'Disabled' ? 'Activar' : 'Quitar acceso a'} ${employee.firstName} ${employee.lastName}`}
                        title={employee.status === 'Disabled' ? 'Activar' : employee.status === 'Invited' ? 'Cancelar invitación' : 'Desactivar'}
                        onClick={() => void setActive(employee, employee.status === 'Disabled')}
                      >
                        <i className={employee.status === 'Disabled' ? 'ri-play-line' : 'ri-pause-line'} aria-hidden="true"></i>
                      </button>
                    )}
                  </td>
                </>
              );
            }}
          />
        </div>
      </div>

      {editing && presets.data && (
        <EmployeeFormModal
          key={editing === 'new' ? 'new' : editing.tenantUserId}
          employee={editing === 'new' ? null : editing}
          presets={presets.data}
          permissionOptions={permissionOptions.data ?? []}
          onClose={() => setEditing(null)}
          onSaved={onSaved}
        />
      )}

      <Modal show={!!share} onHide={() => setShare(null)} centered>
        <Modal.Header closeButton>
          <Modal.Title as="h2" className="h5">Invitación lista</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {share && (
            <InvitationShare
              invitationPath={share.path}
              firstName={share.firstName}
              email={share.email}
              phone={share.phone}
              businessName={tenant?.name ?? 'el negocio'}
            />
          )}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={() => setShare(null)}>Listo</button>
        </Modal.Footer>
      </Modal>
    </>
  );
}
