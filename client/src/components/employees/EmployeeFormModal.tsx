import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import { useTenant } from '../../hooks/useTenant';
import { TenantCapability } from '../../types';
import { COMMERCE_CAPABILITIES, hasAnyCapability, hasCapability } from '../../utils/tenantCapabilities';
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
 * the invitation link. Whoever runs the team also chooses the notices by email the person gets (Fase 29Q), except the
 * owner's, who chooses them in Mi perfil.
 */
export default function EmployeeFormModal({ employee, presets, permissionOptions, onClose, onSaved }: EmployeeFormModalProps) {
  const titleId = useId();
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
  const initialRequestEmails = employee?.noticeEmails?.requests ?? false;
  const initialQuoteReplyEmails = employee?.noticeEmails?.quoteReplies ?? false;
  const initialOrderCancellationEmails = employee?.noticeEmails?.orderCancellations ?? false;
  const [requestEmails, setRequestEmails] = useState(initialRequestEmails);
  const [quoteReplyEmails, setQuoteReplyEmails] = useState(initialQuoteReplyEmails);
  const [orderCancellationEmails, setOrderCancellationEmails] = useState(initialOrderCancellationEmails);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const { capabilities } = useTenant();
  // The notices the business has: the agenda's with appointments, the answers to quotes with services or appointments, and
  // the online orders their customers cancel in a business that sells (Fase 30B).
  const offersRequests = hasCapability(capabilities, TenantCapability.Appointments);
  const offersQuoteReplies = hasAnyCapability(capabilities, TenantCapability.Service | TenantCapability.Appointments);
  const offersOrderCancellations = hasAnyCapability(capabilities, COMMERCE_CAPABILITIES);

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

  // Only what someone changed here is sent: a notice left as it was (or one the business does not have) stays as it is,
  // so the form never undoes what the person chose in Mi perfil after the list was read.
  const noticeFields = () => ({
    requestEmails: offersRequests && requestEmails !== initialRequestEmails ? requestEmails : null,
    quoteReplyEmails: offersQuoteReplies && quoteReplyEmails !== initialQuoteReplyEmails ? quoteReplyEmails : null,
    orderCancellationEmails:
      offersOrderCancellations && orderCancellationEmails !== initialOrderCancellationEmails ? orderCancellationEmails : null,
  });
  // The permission a notice still needs, by its name; nothing while the names have not loaded.
  const missingPermission = (code: string) =>
    !isManager && !permissions.includes(code) ? permissionOptions.find((option) => option.code === code)?.name ?? null : null;

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
            // The owner keeps their role whatever is sent, and chooses their notices in Mi perfil.
            ...(editingOwner
              ? { appRole: AppRole.TenantAdmin as Schemas['UserRole'], tenantRole: BusinessRole.Owner as Schemas['TenantUserRole'], permissions: null }
              : { ...roleFields(), ...noticeFields() }),
          },
        })
        : await apiRequest('post', '/api/v1/employees', {
          body: {
            email: email.trim(),
            firstName: firstName.trim(),
            lastName: lastName.trim(),
            phoneNumber: phone.trim() || null,
            ...roleFields(),
            ...noticeFields(),
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
    <Modal show onHide={() => !saving && onClose()} centered size="lg" scrollable fullscreen="sm-down" aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">{employee ? `${employee.firstName} ${employee.lastName}` : 'Invitar persona'}</Modal.Title>
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
            Es el dueño del negocio: puede hacer todo y su rol no se puede cambiar ni desactivar. Sus avisos por correo los
            elige en Mi perfil.
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

            {(offersRequests || offersQuoteReplies || offersOrderCancellations) && (
              <fieldset className="mt-3">
                <legend className="form-label fs-6 fw-semibold">Avisos por correo</legend>
                <p className="small text-muted mb-2">
                  Le llegan a {employee?.email || email.trim() || 'su correo'}
                  {!employee
                    ? ' en cuanto su correo esté confirmado'
                    : !employee.noticeEmails?.emailConfirmed ? ' cuando confirme su correo' : ''}. También los puede cambiar en
                  Mi perfil.
                </p>
                {offersRequests && (
                  <NoticeSwitch
                    label="Citas en línea"
                    description="Cada solicitud de cita o de visita, su recordatorio si sigue sin respuesta y cada cita que un cliente cancela."
                    checked={requestEmails}
                    onChange={setRequestEmails}
                    missing={missingPermission('ManageAppointments')}
                  />
                )}
                {offersQuoteReplies && (
                  <NoticeSwitch
                    label="Cotizaciones"
                    description="Cada vez que un cliente acepta o rechaza una cotización desde su enlace."
                    checked={quoteReplyEmails}
                    onChange={setQuoteReplyEmails}
                    missing={missingPermission('ManageOrders')}
                  />
                )}
                {offersOrderCancellations && (
                  <NoticeSwitch
                    label="Pedidos cancelados"
                    description="Cada pedido en línea que su cliente cancela antes de que lo acepten."
                    checked={orderCancellationEmails}
                    onChange={setOrderCancellationEmails}
                    missing={missingPermission('ManageOrders')}
                  />
                )}
              </fieldset>
            )}
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

/** One notice by email, and the permission it still needs to reach the person, if any. */
function NoticeSwitch({
  label,
  description,
  checked,
  onChange,
  missing,
}: {
  label: string;
  description: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  /** The name of the permission without which this notice does not reach them. */
  missing: string | null;
}) {
  const switchId = useId();
  const descriptionId = useId();
  return (
    <div className="form-check form-switch mb-2">
      <input
        className="form-check-input"
        type="checkbox"
        role="switch"
        id={switchId}
        aria-describedby={descriptionId}
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
      />
      <label className="form-check-label fw-semibold" htmlFor={switchId}>{label}</label>
      <div className="form-text mt-0" id={descriptionId}>
        {description}
        {/* Always there, so a screen reader hears the warning when the switch turns on. */}
        <span className="d-block text-warning-emphasis" aria-live="polite">
          {checked && missing ? `Sin el permiso «${missing}» no le llegan.` : ''}
        </span>
      </div>
    </div>
  );
}
