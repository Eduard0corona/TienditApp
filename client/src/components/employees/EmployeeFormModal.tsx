import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import {
  AppRole,
  BusinessRole,
  CUSTOM_ROLE_KEY,
  appRoleFromName,
  isManagerPreset,
  matchPreset,
  type EmployeeRecord,
  type EmployeeRolePreset,
} from '../../utils/teamRoles';

interface EmployeeFormModalProps {
  /** The person being edited; null invites someone new. */
  employee: EmployeeRecord | null;
  presets: EmployeeRolePreset[];
  permissionOptions: Schemas['EmployeePermissionOptionDto'][];
  onClose: () => void;
  onSaved: (employee: EmployeeRecord) => void;
}

const DEFAULT_PRESET = 'cashier';

/**
 * Invite a person or change what they can do. A named role (Gerente, Cajero, Mesero, Cocina, Técnico) fills the
 * permissions; changing any of them makes it a custom role. Nobody invents a password: the person chooses it from
 * the invitation link.
 */
export default function EmployeeFormModal({ employee, presets, permissionOptions, onClose, onSaved }: EmployeeFormModalProps) {
  const editingOwner = employee?.tenantRole === 'Owner';
  const initialAppRole = employee ? appRoleFromName(employee.appRole) : AppRole.Staff;
  const initialPreset = employee
    ? matchPreset(presets, initialAppRole, employee.tenantRole !== 'Staff', employee.permissions)
    : DEFAULT_PRESET;
  const initialPermissions = employee
    ? employee.permissions
    : presets.find((preset) => preset.key === DEFAULT_PRESET)?.permissions ?? [];

  const [firstName, setFirstName] = useState(employee?.firstName ?? '');
  const [lastName, setLastName] = useState(employee?.lastName ?? '');
  const [email, setEmail] = useState(employee?.email ?? '');
  const [phone, setPhone] = useState(employee?.phoneNumber ?? '');
  const [presetKey, setPresetKey] = useState(initialPreset);
  const [appRole, setAppRole] = useState<number>(initialAppRole);
  const [permissions, setPermissions] = useState<string[]>(initialPermissions);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const isManager = isManagerPreset(presets.find((preset) => preset.key === presetKey));
  const noAccess = !isManager && !editingOwner && permissions.length === 0;

  const choosePreset = (preset: EmployeeRolePreset) => {
    setPresetKey(preset.key);
    setAppRole(preset.appRole);
    setPermissions(preset.permissions);
  };

  const togglePermission = (code: string, checked: boolean) => {
    const next = checked ? [...permissions, code] : permissions.filter((item) => item !== code);
    // Any change makes it a custom role, with a counter account (a kitchen account cannot open the counter screens).
    const nextAppRole = appRole === AppRole.TenantAdmin ? AppRole.Staff : appRole;
    const matched = matchPreset(presets, nextAppRole, false, next);
    setPermissions(next);
    setPresetKey(matched);
    setAppRole(matched === CUSTOM_ROLE_KEY ? AppRole.Staff : nextAppRole);
  };

  const roleFields = () => isManager
    ? { appRole: AppRole.TenantAdmin as Schemas['UserRole'], tenantRole: BusinessRole.Admin as Schemas['TenantUserRole'], permissions: null }
    // An explicit list: an empty one is "sin acceso", not the defaults of the role.
    : { appRole: appRole as Schemas['UserRole'], tenantRole: BusinessRole.Staff as Schemas['TenantUserRole'], permissions };

  const save = async () => {
    if (!firstName.trim() || !lastName.trim() || (!employee && !email.trim())) {
      setError('Escribe el nombre, el apellido y el correo.');
      return;
    }
    setSaving(true);
    setError('');
    try {
      const saved = employee
        ? await apiRequest('put', '/api/v1/employees/{tenantUserId}', {
          path: { tenantUserId: employee.tenantUserId },
          body: {
            tenantUserId: employee.tenantUserId,
            firstName: firstName.trim(),
            lastName: lastName.trim(),
            // The owner keeps their role whatever is sent.
            ...(editingOwner
              ? { appRole: AppRole.TenantAdmin as Schemas['UserRole'], tenantRole: BusinessRole.Owner as Schemas['TenantUserRole'], permissions: null }
              : roleFields()),
          },
        })
        : await apiRequest('post', '/api/v1/employees', {
          body: {
            email: email.trim(),
            firstName: firstName.trim(),
            lastName: lastName.trim(),
            phoneNumber: phone.trim() || null,
            ...roleFields(),
          },
        });
      onSaved(saved);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal show onHide={() => !saving && onClose()} centered size="lg" scrollable fullscreen="sm-down">
      <Modal.Header closeButton>
        <Modal.Title as="h2" className="h5">{employee ? `${employee.firstName} ${employee.lastName}` : 'Invitar persona'}</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger py-2" role="alert">{error}</div>}

        <div className="row g-2 mb-3">
          <div className="col-md-6">
            <label className="form-label" htmlFor="employee-first-name">Nombre</label>
            <input id="employee-first-name" className="form-control" maxLength={100} value={firstName} onChange={(event) => setFirstName(event.target.value)} />
          </div>
          <div className="col-md-6">
            <label className="form-label" htmlFor="employee-last-name">Apellido</label>
            <input id="employee-last-name" className="form-control" maxLength={100} value={lastName} onChange={(event) => setLastName(event.target.value)} />
          </div>
          <div className="col-md-6">
            <label className="form-label" htmlFor="employee-email">Correo</label>
            <input
              id="employee-email"
              type="email"
              className="form-control"
              value={email}
              disabled={!!employee}
              onChange={(event) => setEmail(event.target.value)}
            />
            {!employee && <div className="form-text">Con este correo entra. Si ya tiene cuenta en TienditApp, se une con ella.</div>}
          </div>
          <div className="col-md-6">
            <label className="form-label" htmlFor="employee-phone">WhatsApp</label>
            <input
              id="employee-phone"
              type="tel"
              className="form-control"
              value={phone}
              disabled={!!employee}
              placeholder="10 dígitos"
              onChange={(event) => setPhone(event.target.value)}
            />
            <div className="form-text">
              {employee ? 'Lo cambia la persona desde su perfil, con un código.' : 'Para mandarle la invitación por WhatsApp.'}
            </div>
          </div>
        </div>

        {editingOwner ? (
          <div className="alert alert-info small mb-0" role="status">
            Es el dueño del negocio: puede hacer todo y su rol no se puede cambiar ni desactivar.
          </div>
        ) : (
          <>
            <fieldset className="mb-3">
              <legend className="form-label fs-6 fw-semibold">Rol</legend>
              <div className="row g-2">
                {presets.map((preset) => (
                  <div className="col-sm-6 col-lg-4" key={preset.key}>
                    <label className={`border rounded p-2 h-100 d-flex gap-2 ${presetKey === preset.key ? 'border-primary bg-primary-subtle' : ''}`}>
                      <input
                        type="radio"
                        className="form-check-input mt-1"
                        name="employee-role"
                        checked={presetKey === preset.key}
                        onChange={() => choosePreset(preset)}
                      />
                      <span>
                        <span className="fw-semibold d-block">{preset.name}</span>
                        <small className="text-muted">{preset.description}</small>
                      </span>
                    </label>
                  </div>
                ))}
                {presetKey === CUSTOM_ROLE_KEY && (
                  <div className="col-sm-6 col-lg-4">
                    <div className="border border-primary bg-primary-subtle rounded p-2 h-100">
                      <span className="fw-semibold d-block">Personalizado</span>
                      <small className="text-muted">Los permisos que elijas abajo.</small>
                    </div>
                  </div>
                )}
              </div>
            </fieldset>

            <fieldset>
              <legend className="form-label fs-6 fw-semibold">Permisos</legend>
              {isManager && <p className="small text-muted">Un gerente puede hacer todo, incluido invitar y cambiar permisos.</p>}
              <div className="row">
                {permissionOptions.map((permission) => (
                  <div className="col-md-6 mb-2" key={permission.code}>
                    <label className="d-flex gap-2">
                      <input
                        className="form-check-input mt-1"
                        type="checkbox"
                        checked={isManager || permissions.includes(permission.code)}
                        disabled={isManager}
                        onChange={(event) => togglePermission(permission.code, event.target.checked)}
                      />
                      <span>
                        <span className="fw-semibold d-block">{permission.name}</span>
                        <small className="text-muted">{permission.description}</small>
                      </span>
                    </label>
                  </div>
                ))}
              </div>
              {noAccess && (
                <div className="alert alert-warning small mb-0" role="status">
                  Sin acceso: podrá entrar, pero no verá ninguna pantalla del negocio hasta que le des algún permiso.
                </div>
              )}
            </fieldset>
          </>
        )}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose} disabled={saving}>Cancelar</button>
        <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={saving}>
          {saving ? 'Guardando…' : employee ? 'Guardar cambios' : 'Invitar'}
        </button>
      </Modal.Footer>
    </Modal>
  );
}
