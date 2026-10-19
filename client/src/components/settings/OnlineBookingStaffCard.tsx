import { useId, useState, type FormEvent } from 'react';
import { Modal } from 'react-bootstrap';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  appointmentsApi,
  type OnlineBookingStaffMember,
  type OnlineBookingStaffSettings,
} from '../../api/appointmentsApi';
import { getApiErrorMessage } from '../../api/errors';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { TenantCapability } from '../../types';
import { fromDraft } from '../../utils/openingHours';
import { hasCapability } from '../../utils/tenantCapabilities';
import { chosenServices, describeStaff, draftOf, staffDraftProblem, type StaffDraft } from './onlineBookingStaff';
import WeeklyHoursFields from './WeeklyHoursFields';
import { PERSON_HOURS_WORDS } from './weeklyHoursWords';

const STAFF_KEY = ['online-booking-staff'];

interface StaffFormProps {
  member: OnlineBookingStaffMember;
  settings: OnlineBookingStaffSettings;
  draft: StaffDraft;
  problem: string;
  saving: boolean;
  onChange: (draft: StaffDraft) => void;
  onCancel: () => void;
  onSave: () => void;
}

function StaffForm({ member, settings, draft, problem, saving, onChange, onCancel, onSave }: StaffFormProps) {
  const titleId = useId();
  const id = useId();
  const change = (patch: Partial<StaffDraft>) => onChange({ ...draft, ...patch });
  const toggleService = (serviceId: string, checked: boolean) =>
    change({ serviceIds: checked ? [...draft.serviceIds, serviceId] : draft.serviceIds.filter((item) => item !== serviceId) });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSave();
  };

  return (
    <Modal show onHide={() => !saving && onCancel()} centered scrollable fullscreen="sm-down" aria-labelledby={titleId}>
      <form noValidate onSubmit={submit} className="d-flex flex-column overflow-hidden">
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">{member.fullName}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {problem && <div className="alert alert-danger py-2" role="alert">{problem}</div>}
          <fieldset disabled={saving}>
            <div className="form-check form-switch mb-3">
              <input
                id={`${id}-enabled`}
                className="form-check-input"
                type="checkbox"
                role="switch"
                checked={draft.enabled}
                onChange={(event) => change({ enabled: event.target.checked })}
              />
              <label className="form-check-label" htmlFor={`${id}-enabled`}>Agenda citas en línea</label>
              <div className="form-text">Apagada, tus clientes no la ven; lo que configures aquí se guarda.</div>
            </div>

            <div className="mb-3">
              <label className="form-label" htmlFor={`${id}-name`}>Nombre que ven tus clientes</label>
              <input
                id={`${id}-name`}
                className="form-control"
                maxLength={80}
                autoComplete="off"
                required
                aria-describedby={`${id}-name-help`}
                value={draft.name}
                onChange={(event) => change({ name: event.target.value })}
              />
              <div id={`${id}-name-help`} className="form-text">
                Por ejemplo, su nombre y la inicial del apellido. Sin teléfonos ni enlaces.
              </div>
            </div>

            <fieldset className="mb-3">
              <legend className="form-label fw-semibold fs-6 mb-1">Servicios</legend>
              <div className="form-check">
                <input id={`${id}-all`} className="form-check-input" type="radio" name={`${id}-services`}
                  checked={draft.allServices} onChange={() => change({ allServices: true })} />
                <label className="form-check-label" htmlFor={`${id}-all`}>Todos los servicios, también los que agregues</label>
              </div>
              <div className="form-check">
                <input id={`${id}-some`} className="form-check-input" type="radio" name={`${id}-services`}
                  checked={!draft.allServices} onChange={() => change({ allServices: false })} />
                <label className="form-check-label" htmlFor={`${id}-some`}>Solo algunos</label>
              </div>
              {!draft.allServices && (
                settings.services.length === 0 ? (
                  <p className="small text-muted mt-2 mb-0">Aún no tienes servicios para agendar en línea.</p>
                ) : (
                  <div className="mt-2 ps-4">
                    {settings.services.map((service, index) => (
                      <div key={service.id} className="form-check">
                        <input
                          id={`${id}-service-${index}`}
                          className="form-check-input"
                          type="checkbox"
                          checked={draft.serviceIds.includes(service.id)}
                          onChange={(event) => toggleService(service.id, event.target.checked)}
                        />
                        <label className="form-check-label" htmlFor={`${id}-service-${index}`}>
                          {service.name}
                          {/* Paused in the catalog: it stays with the person and comes back to the page with them. */}
                          {!service.bookableNow && <span className="small text-muted"> · No se agenda ahora</span>}
                        </label>
                      </div>
                    ))}
                  </div>
                )
              )}
            </fieldset>

            <fieldset className="mb-0">
              <legend className="form-label fw-semibold fs-6 mb-1">Horario</legend>
              <div className="form-check">
                <input id={`${id}-business-hours`} className="form-check-input" type="radio" name={`${id}-hours`}
                  checked={!draft.ownHours} onChange={() => change({ ownHours: false })} />
                <label className="form-check-label" htmlFor={`${id}-business-hours`}>El horario del negocio</label>
              </div>
              <div className="form-check">
                <input id={`${id}-own-hours`} className="form-check-input" type="radio" name={`${id}-hours`}
                  checked={draft.ownHours} onChange={() => change({ ownHours: true })} />
                <label className="form-check-label" htmlFor={`${id}-own-hours`}>Su propio horario</label>
              </div>
              {draft.ownHours && (
                <div className="mt-2">
                  <WeeklyHoursFields
                    week={draft.week}
                    onChange={(week) => change({ week })}
                    idPrefix={`${id}-week`}
                    words={PERSON_HOURS_WORDS}
                  />
                  <p className="small text-muted mt-2 mb-0">Solo se agenda cuando el negocio también está abierto, y nunca en tus días especiales cerrados.</p>
                </div>
              )}
            </fieldset>
          </fieldset>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" disabled={saving} onClick={onCancel}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? 'Guardando…' : 'Guardar'}</button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}

/**
 * «Quién atiende» (Fase 24J, ADR 0032), below «Citas en línea» for owners and admins of a business with the agenda: the
 * active team, and for each person whether customers book them online, the name customers see, their services and their
 * own hours. While someone takes online appointments, customers choose the person (or no preference) and the free times
 * come from each one; the server decides them.
 */
export default function OnlineBookingStaffCard() {
  const { tenant, capabilities } = useTenant();
  const toast = useToast();
  const queryClient = useQueryClient();
  const titleId = useId();
  const withAgenda = hasCapability(capabilities, TenantCapability.Appointments);
  const staff = useQuery({
    queryKey: STAFF_KEY,
    queryFn: ({ signal }) => appointmentsApi.getOnlineBookingStaff(signal),
    enabled: !!tenant && withAgenda,
  });
  const [editing, setEditing] = useState<{ member: OnlineBookingStaffMember; draft: StaffDraft } | null>(null);
  const [problem, setProblem] = useState('');
  const [saving, setSaving] = useState(false);
  if (!tenant || !withAgenda) return null;

  const settings = staff.data;
  const enabledCount = settings?.members.filter((member) => member.enabled).length ?? 0;

  const open = (member: OnlineBookingStaffMember) => {
    setEditing({ member, draft: draftOf(member, tenant.openingHours ?? [], enabledCount < (settings?.maxEnabled ?? 0)) });
    setProblem('');
  };

  const save = async () => {
    if (!editing || !settings) return;
    const { member, draft } = editing;
    const found = staffDraftProblem(draft, settings.services);
    if (found) {
      setProblem(found);
      return;
    }
    setSaving(true);
    setProblem('');
    try {
      const saved = await appointmentsApi.updateOnlineBookingStaff(member.userId, {
        enabled: draft.enabled,
        name: draft.name.trim(),
        allServices: draft.allServices,
        serviceIds: draft.allServices ? [] : chosenServices(draft.serviceIds, settings.services).map((service) => service.id),
        weeklyHours: draft.ownHours ? fromDraft(draft.week) : null,
      });
      queryClient.setQueryData<OnlineBookingStaffSettings>(STAFF_KEY, (current) => current && {
        ...current,
        members: current.members.map((item) => (item.userId === saved.userId ? saved : item)),
      });
      // «Citas en línea» says whether the times come from each person, and the booking page offers who attends.
      void queryClient.invalidateQueries({ queryKey: ['online-booking-settings'] });
      void queryClient.invalidateQueries({ queryKey: ['public-booking'] });
      setEditing(null);
      toast.success(saved.enabled && !member.enabled ? `Guardado: ${saved.name} ya agenda citas en línea.` : 'Guardado.');
    } catch (err: unknown) {
      setProblem(getApiErrorMessage(err, `No se pudo guardar a ${member.fullName}.`));
    } finally {
      setSaving(false);
    }
  };

  return (
    <section className="card" aria-labelledby={titleId}>
      <div className="card-body">
        <h4 id={titleId} className="header-title mt-0 mb-1">Quién atiende</h4>
        <p className="text-muted small mb-3">
          Elige quién de tu equipo agenda citas en línea. Tus clientes escogen a la persona o «Sin preferencia», y las horas
          libres salen del horario y las citas de cada quien. Mientras no elijas a nadie, se usan las citas a la vez.
        </p>

        {staff.isPending ? (
          <p className="text-muted small mb-0" aria-busy="true">Cargando…</p>
        ) : staff.isError || !settings ? (
          <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2 mb-0" role="alert">
            <span className="me-auto">{getApiErrorMessage(staff.error, 'No se pudo cargar tu equipo.')}</span>
            <button type="button" className="btn btn-sm btn-light" onClick={() => void staff.refetch()}>Reintentar</button>
          </div>
        ) : (
          <>
            {enabledCount >= settings.maxEnabled && (
              <p className="small text-muted">Ya agendan {settings.maxEnabled} personas, el máximo: apaga a alguien para agregar a otra.</p>
            )}
            <ul className="list-group list-group-flush">
              {settings.members.map((member) => {
                const verb = member.configured ? 'Editar' : 'Configurar';
                return (
                  <li key={member.userId} className="list-group-item px-0 d-flex flex-wrap align-items-start justify-content-between gap-2">
                    <div className="flex-grow-1 text-break">
                      <div className="d-flex flex-wrap align-items-center gap-2">
                        <span className="fw-semibold">{member.fullName}</span>
                        {!member.configured ? (
                          <span className="small text-muted">No agenda en línea</span>
                        ) : member.enabled ? (
                          <span className="ta-chip ta-chip-ready">Agenda en línea</span>
                        ) : (
                          <span className="ta-chip ta-chip-done">Apagada</span>
                        )}
                      </div>
                      {member.configured && (
                        <div className="small text-muted">
                          Tus clientes ven «{member.name}» · {describeStaff(member, settings.services)}
                        </div>
                      )}
                    </div>
                    <button
                      type="button"
                      className="btn btn-light btn-sm"
                      aria-label={`${verb} a ${member.fullName}`}
                      onClick={() => open(member)}
                    >
                      {verb}
                    </button>
                  </li>
                );
              })}
            </ul>
          </>
        )}
      </div>

      {editing && settings && (
        <StaffForm
          member={editing.member}
          settings={settings}
          draft={editing.draft}
          problem={problem}
          saving={saving}
          onChange={(draft) => setEditing({ ...editing, draft })}
          onCancel={() => setEditing(null)}
          onSave={() => void save()}
        />
      )}
    </section>
  );
}
