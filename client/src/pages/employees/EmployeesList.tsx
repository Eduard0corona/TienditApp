import { useCallback, useEffect, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useForm } from 'react-hook-form';
import DataTable from '../../components/common/DataTable';
import PageTitle from '../../components/common/PageTitle';
import { employeesApi } from '../../api/employeesApi';
import type {
  CreateEmployeeRequest,
  EmployeeDto,
  EmployeePermissionOptionDto,
  PaginatedList,
  UpdateEmployeeRequest,
} from '../../types';
import { getApiErrorMessage } from '../../api/errors';

const COLUMNS = [
  { header: 'Empleado' },
  { header: 'Rol app', className: 'text-center' },
  { header: 'Rol tenant', className: 'text-center' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Permisos' },
  { header: 'Acciones', className: 'text-center' },
];

const APP_ROLE_OPTIONS = [
  { value: 1, label: 'Administrador' },
  { value: 2, label: 'Staff' },
  { value: 3, label: 'Kitchen' },
];

const TENANT_ROLE_OPTIONS = [
  { value: 1, label: 'Admin' },
  { value: 2, label: 'Staff' },
];

type EmployeeForm = {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
  appRole: number;
  tenantRole: number;
  permissions: string[];
};

export default function EmployeesList() {
  const [data, setData] = useState<PaginatedList<EmployeeDto>>({
    items: [],
    pageNumber: 1,
    totalPages: 0,
    totalCount: 0,
    hasPreviousPage: false,
    hasNextPage: false,
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showModal, setShowModal] = useState(false);
  const [editingEmployee, setEditingEmployee] = useState<EmployeeDto | null>(null);
  const [permissionOptions, setPermissionOptions] = useState<EmployeePermissionOptionDto[]>([]);
  const [selectedPermissions, setSelectedPermissions] = useState<string[]>([]);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [filterStatus, setFilterStatus] = useState('');
  const [filterRole, setFilterRole] = useState('');

  const {
    register,
    handleSubmit,
    reset,
    watch,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<EmployeeForm>({
    defaultValues: {
      email: '',
      password: '',
      firstName: '',
      lastName: '',
      phoneNumber: '',
      appRole: 2,
      tenantRole: 2,
      permissions: [],
    },
  });

  const watchedAppRole = watch('appRole');

  useEffect(() => {
    if (watchedAppRole === 1) {
      setValue('tenantRole', 1);
    }
  }, [setValue, watchedAppRole]);

  const loadEmployees = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const params: {
        status?: string;
        appRole?: string;
        pageNumber: number;
        pageSize: number;
      } = {
        pageNumber,
        pageSize,
      };

      if (filterStatus) params.status = filterStatus;
      if (filterRole) params.appRole = filterRole;

      const { data: response } = await employeesApi.getAll(params);
      setData(response);
    } catch {
      setError('No se pudieron cargar los empleados.');
    } finally {
      setLoading(false);
    }
  }, [filterRole, filterStatus, pageNumber, pageSize]);

  const loadPermissionOptions = useCallback(async () => {
    try {
      const { data: response } = await employeesApi.getPermissionOptions();
      setPermissionOptions(response);
    } catch {
      setPermissionOptions([]);
    }
  }, []);

  useEffect(() => {
    void loadEmployees();
  }, [loadEmployees]);

  useEffect(() => {
    void loadPermissionOptions();
  }, [loadPermissionOptions]);

  const closeModal = () => {
    setShowModal(false);
    setEditingEmployee(null);
    setSelectedPermissions([]);
  };

  const openCreateModal = () => {
    setEditingEmployee(null);
    setSelectedPermissions([]);
    reset({
      email: '',
      password: '',
      firstName: '',
      lastName: '',
      phoneNumber: '',
      appRole: 2,
      tenantRole: 2,
      permissions: [],
    });
    setShowModal(true);
  };

  const openEditModal = (employee: EmployeeDto) => {
    setEditingEmployee(employee);
    setSelectedPermissions(employee.permissions);
    reset({
      email: employee.email,
      password: '',
      firstName: employee.firstName,
      lastName: employee.lastName,
      phoneNumber: employee.phoneNumber || '',
      appRole: employee.appRole === 'TenantAdmin' ? 1 : employee.appRole === 'Kitchen' ? 3 : 2,
      tenantRole: employee.tenantRole === 'Admin' ? 1 : 2,
      permissions: employee.permissions,
    });
    setShowModal(true);
  };

  const togglePermission = (code: string, checked: boolean) => {
    setSelectedPermissions((current) =>
      checked ? [...current, code] : current.filter((item) => item !== code),
    );
  };

  const permissionSummary = useMemo(() => {
    const map = new Map(permissionOptions.map((option) => [option.code, option.name]));
    return (codes: string[]) => codes.map((code) => map.get(code) || code).join(', ');
  }, [permissionOptions]);

  const onSubmit = async (form: EmployeeForm) => {
    try {
      if (editingEmployee) {
        const payload: UpdateEmployeeRequest = {
          tenantUserId: editingEmployee.tenantUserId,
          firstName: form.firstName.trim(),
          lastName: form.lastName.trim(),
          phoneNumber: form.phoneNumber?.trim() || undefined,
          appRole: form.appRole,
          tenantRole: form.appRole === 1 ? 1 : form.tenantRole,
          permissions: selectedPermissions,
        };

        await employeesApi.update(editingEmployee.tenantUserId, payload);
      } else {
        const payload: CreateEmployeeRequest = {
          email: form.email.trim(),
          password: form.password,
          firstName: form.firstName.trim(),
          lastName: form.lastName.trim(),
          phoneNumber: form.phoneNumber?.trim() || undefined,
          appRole: form.appRole,
          tenantRole: form.appRole === 1 ? 1 : form.tenantRole,
          permissions: selectedPermissions,
        };

        await employeesApi.create(payload);
      }

      closeModal();
      await loadEmployees();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'No se pudo guardar el empleado.');
      setError(msg);
    }
  };

  const toggleActive = async (employee: EmployeeDto) => {
    try {
      await employeesApi.setActive(employee.tenantUserId, !employee.isActive);
      await loadEmployees();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'No se pudo actualizar el estado del empleado.');
      setError(msg);
    }
  };

  return (
    <>
      <PageTitle title="Empleados" subtitle="Administracion de accesos" />

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              <div className="row mb-2">
                <div className="col-sm-4">
                  <button className="btn btn-primary mb-2" onClick={openCreateModal}>
                    <i className="ri-user-add-line me-1"></i> Nuevo Empleado
                  </button>
                </div>
                <div className="col-sm-8">
                  <div className="d-flex justify-content-sm-end gap-2">
                    <select
                      className="form-select form-select-sm"
                      style={{ width: 'auto' }}
                      value={filterRole}
                      onChange={(e) => {
                        setFilterRole(e.target.value);
                        setPageNumber(1);
                      }}
                    >
                      <option value="">Todos los roles</option>
                      <option value="TenantAdmin">Administrador</option>
                      <option value="Staff">Staff</option>
                      <option value="Kitchen">Kitchen</option>
                    </select>
                    <select
                      className="form-select form-select-sm"
                      style={{ width: 'auto' }}
                      value={filterStatus}
                      onChange={(e) => {
                        setFilterStatus(e.target.value);
                        setPageNumber(1);
                      }}
                    >
                      <option value="">Todos</option>
                      <option value="Active">Activos</option>
                      <option value="Disabled">Inactivos</option>
                    </select>
                  </div>
                </div>
              </div>

              {error && (
                <div className="alert alert-danger alert-dismissible fade show">
                  {error}
                  <button type="button" className="btn-close" onClick={() => setError('')}></button>
                </div>
              )}

              <DataTable
                data={data.items}
                columns={COLUMNS}
                keyExtractor={(item) => item.tenantUserId}
                loading={loading}
                emptyMessage="No hay empleados registrados."
                searchFilter={(item, search) =>
                  item.email.toLowerCase().includes(search) ||
                  item.firstName.toLowerCase().includes(search) ||
                  item.lastName.toLowerCase().includes(search)
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
                renderRow={(employee) => (
                  <>
                    <td>
                      <div className="fw-semibold">{employee.firstName} {employee.lastName}</div>
                      <small className="text-muted">{employee.email}</small>
                    </td>
                    <td className="text-center">{employee.appRole}</td>
                    <td className="text-center">{employee.tenantRole}</td>
                    <td className="text-center">
                      <span className={`badge ${employee.isActive ? 'bg-success' : 'bg-danger'}`}>
                        {employee.isActive ? 'Activo' : 'Inactivo'}
                      </span>
                    </td>
                    <td>
                      <small className="text-muted">{permissionSummary(employee.permissions) || '-'}</small>
                    </td>
                    <td className="text-center">
                      <button
                        className="btn btn-sm btn-soft-secondary me-1"
                        title={employee.isActive ? 'Desactivar' : 'Activar'}
                        onClick={() => void toggleActive(employee)}
                      >
                        <i className={employee.isActive ? 'ri-pause-line' : 'ri-play-line'}></i>
                      </button>
                      <button
                        className="btn btn-sm btn-soft-primary"
                        title="Editar"
                        onClick={() => openEditModal(employee)}
                      >
                        <i className="ri-pencil-line"></i>
                      </button>
                    </td>
                  </>
                )}
              />
            </div>
          </div>
        </div>
      </div>

      <Modal show={showModal} onHide={closeModal} centered size="lg">
        <Modal.Header closeButton>
          <Modal.Title>{editingEmployee ? 'Editar Empleado' : 'Nuevo Empleado'}</Modal.Title>
        </Modal.Header>
        <form onSubmit={handleSubmit(onSubmit)}>
          <Modal.Body>
            <div className="row">
              <div className="col-md-6 mb-3">
                <label className="form-label">Nombre *</label>
                <input
                  type="text"
                  className={`form-control ${errors.firstName ? 'is-invalid' : ''}`}
                  {...register('firstName', { required: 'Requerido', maxLength: 100 })}
                />
              </div>
              <div className="col-md-6 mb-3">
                <label className="form-label">Apellido *</label>
                <input
                  type="text"
                  className={`form-control ${errors.lastName ? 'is-invalid' : ''}`}
                  {...register('lastName', { required: 'Requerido', maxLength: 100 })}
                />
              </div>
            </div>

            <div className="row">
              <div className="col-md-6 mb-3">
                <label className="form-label">Email *</label>
                <input
                  type="email"
                  className={`form-control ${errors.email ? 'is-invalid' : ''}`}
                  disabled={!!editingEmployee}
                  {...register('email', { required: 'Requerido' })}
                />
              </div>
              <div className="col-md-6 mb-3">
                <label className="form-label">Telefono</label>
                <input
                  type="text"
                  className="form-control"
                  {...register('phoneNumber')}
                />
              </div>
            </div>

            {!editingEmployee && (
              <div className="mb-3">
                <label className="form-label">Password temporal *</label>
                <input
                  type="password"
                  className={`form-control ${errors.password ? 'is-invalid' : ''}`}
                  {...register('password', { required: 'Requerido', minLength: 6 })}
                />
              </div>
            )}

            <div className="row">
              <div className="col-md-6 mb-3">
                <label className="form-label">Rol de aplicacion</label>
                <select className="form-select" {...register('appRole', { valueAsNumber: true })}>
                  {APP_ROLE_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </div>
              <div className="col-md-6 mb-3">
                <label className="form-label">Rol de tenant</label>
                <select
                  className="form-select"
                  disabled={watchedAppRole === 1}
                  {...register('tenantRole', { valueAsNumber: true })}
                >
                  {TENANT_ROLE_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </div>
            </div>

            <div className="mb-0">
              <label className="form-label d-block">Permisos</label>
              <div className="row">
                {permissionOptions.map((permission) => {
                  const checked = selectedPermissions.includes(permission.code);
                  return (
                    <div className="col-md-6 mb-2" key={permission.code}>
                      <label className="form-check-label d-flex gap-2">
                        <input
                          className="form-check-input mt-1"
                          type="checkbox"
                          checked={checked}
                          onChange={(e) => togglePermission(permission.code, e.target.checked)}
                        />
                        <span>
                          <span className="fw-semibold d-block">{permission.name}</span>
                          <small className="text-muted">{permission.description}</small>
                        </span>
                      </label>
                    </div>
                  );
                })}
              </div>
            </div>
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light" onClick={closeModal}>
              Cancelar
            </button>
            <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
              {isSubmitting ? 'Guardando...' : editingEmployee ? 'Guardar cambios' : 'Crear empleado'}
            </button>
          </Modal.Footer>
        </form>
      </Modal>
    </>
  );
}
