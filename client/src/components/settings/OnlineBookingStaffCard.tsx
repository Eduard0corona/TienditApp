import { useId, useRef, useState, type ChangeEvent, type FormEvent } from 'react';
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
import { getBusinessToday } from '../../utils/dates';
import { fromDraft } from '../../utils/openingHours';
import { hasCapability } from '../../utils/tenantCapabilities';
import PhotoOrInitial from '../common/PhotoOrInitial';
import {
  MAX_ABSENCES,
  absenceDraft,
  absencesToSave,
  describeAbsence,
  chosenServices,
  describeStaff,
  draftOf,
  staffDraftProblem,
  type AbsenceDraft,
  type StaffDraft,
} from './onlineBookingStaff';
import WeeklyHoursFields from './WeeklyHoursFields';
import { PERSON_HOURS_WORDS } from './weeklyHoursWords';
import './OnlineBookingStaffCard.css';

const STAFF_KEY = ['online-booking-staff'];

interface StaffPhotoProps {
  member: OnlineBookingStaffMember;
  /** The name next to it, for the initial while it has no photo. */
  name: string;
  /** The form is saving or a photo is going up or away: nothing else starts. */
  busy: boolean;
  /** A photo is going up or away. */
  uploading: boolean;
  onUpload: (file: File) => void;
  onRemove: () => void;
}

/**
 * The photo customers see of the person (Fase 31I), instead of their initial. It goes up and away at once, apart from the
 * rest of the form; the server keeps it only for someone already saved.
 */
function StaffPhoto({ member, name, busy, uploading, onUpload, onRemove }: StaffPhotoProps) {
  const id = useId();
  const input = useRef<HTMLInputElement>(null);
  const pick = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) onUpload(file);
  };

  return (
    <div className="mb-3">
      <span id={`${id}-label`} className="form-label d-block">Foto que ven tus clientes</span>
      <div className="d-flex flex-wrap align-items-center gap-3">
        <PhotoOrInitial src={member.photoUrl} name={name || member.fullName} className="obs-photo" loading="eager" />
        {member.configured ? (
          <div className="d-flex flex-wrap gap-2">
            <input
              ref={input}
              type="file"
              className="d-none"
              accept="image/jpeg,image/png,image/webp"
              aria-labelledby={`${id}-label`}
              onChange={pick}
            />
            <button type="button" className="btn btn-light btn-sm" disabled={busy} onClick={() => input.current?.click()}>
              <i className="ri-image-add-line me-1" aria-hidden="true"></i>{member.photoUrl ? 'Cambiar foto' : 'Subir foto'}
            </button>
            {member.photoUrl && (
              <button type="button" className="btn btn-light btn-sm" disabled={busy} onClick={onRemove}>
                <i className="ri-delete-bin-line me-1" aria-hidden="true"></i>Quitar foto
              </button>
            )}
          </div>
        ) : (
          <span className="small text-muted">Guárdala primero; luego le pones su foto.</span>
        )}
      </div>
      <div className="form-text">
        {uploading ? 'Guardando la foto…' : 'Con su permiso: la ve quien abra tu página de citas. JPG, PNG o WebP de hasta 5 MB; se guarda al momento.'}
      </div>
    </div>
  );
}

interface AbsencesFieldsProps {
  absences: AbsenceDraft[];
  idPrefix: string;
  onChange: (absences: AbsenceDraft[]) => void;
}

/** «Días que no atiende» (Fase 31I): runs of whole days, both included, when customers do not book the person. */
function AbsencesFields({ absences, idPrefix, onChange }: AbsencesFieldsProps) {
  // One that already ended is dropped when saving: the picker starts «Hasta» at today.
  const today = getBusinessToday();
  const set = (key: string, patch: Partial<AbsenceDraft>) =>
    onChange(absences.map((absence) => (absence.key === key ? { ...absence, ...patch } : absence)));
  // Choosing the first day moves the last one along when it was empty or earlier: a single day is one choice.
  const setFrom = (absence: AbsenceDraft, from: string) =>
    set(absence.key, { from, to: !absence.to || (from && absence.to < from) ? from : absence.to });

  return (
    <fieldset className="mb-3">
      <legend className="form-label fw-semibold fs-6 mb-1">Días que no atiende</legend>
      <p className="small text-muted mb-2">
        Vacaciones, un curso o un permiso: esos días tus clientes no la pueden elegir. Sus citas de esos días no cambian;
        revísalas en tu agenda. Las que ya pasaron se quitan al guardar.
      </p>
      {absences.map((absence, index) => {
        // Each row says which absence it is, so «Desde» and «Hasta» are not the same for a screen reader.
        const name = absence.from && absence.to ? describeAbsence(absence) : String(index + 1);
        return (
          <div key={absence.key} className="d-flex flex-wrap align-items-end gap-2 mb-2" role="group" aria-label={`Ausencia ${name}`}>
            <div>
              <label className="form-label small mb-1" htmlFor={`${idPrefix}-${absence.key}-from`}>Desde</label>
              <input
                id={`${idPrefix}-${absence.key}-from`}
                type="date"
                className="form-control form-control-sm"
                value={absence.from}
                onChange={(event) => setFrom(absence, event.target.value)}
              />
            </div>
            <div>
              <label className="form-label small mb-1" htmlFor={`${idPrefix}-${absence.key}-to`}>Hasta</label>
              <input
                id={`${idPrefix}-${absence.key}-to`}
                type="date"
                className="form-control form-control-sm"
                min={absence.from && absence.from > today ? absence.from : today}
                value={absence.to}
                onChange={(event) => set(absence.key, { to: event.target.value })}
              />
            </div>
            <button
              type="button"
              className="btn btn-light btn-sm"
              aria-label={`Quitar la ausencia ${name}`}
              onClick={() => onChange(absences.filter((item) => item.key !== absence.key))}
            >
              <i className="ri-delete-bin-line" aria-hidden="true"></i>
            </button>
          </div>
        );
      })}
      {absences.length < MAX_ABSENCES ? (
        <button type="button" className="btn btn-light btn-sm" onClick={() => onChange([...absences, absenceDraft()])}>
          <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar días
        </button>
      ) : (
        <p className="small text-muted mb-0">Ya tiene {MAX_ABSENCES} ausencias, el máximo: junta o quita alguna.</p>
      )}
    </fieldset>
  );
}

interface StaffFormProps {
  member: OnlineBookingStaffMember;
  settings: OnlineBookingStaffSettings;
  draft: StaffDraft;
  problem: string;
  saving: boolean;
  photoBusy: boolean;
  onChange: (draft: StaffDraft) => void;
  onCancel: () => void;
  onSave: () => void;
  onUploadPhoto: (file: File) => void;
  onRemovePhoto: () => void;
}

function StaffForm({
  member, settings, draft, problem, saving, photoBusy, onChange, onCancel, onSave, onUploadPhoto, onRemovePhoto,
}: StaffFormProps) {
  const titleId = useId();
  const id = useId();
  const busy = saving || photoBusy;
  const change = (patch: Partial<StaffDraft>) => onChange({ ...draft, ...patch });
  const toggleService = (serviceId: string, checked: boolean) =>
    change({ serviceIds: checked ? [...draft.serviceIds, serviceId] : draft.serviceIds.filter((item) => item !== serviceId) });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSave();
  };

  return (
    <Modal show onHide={() => !busy && onCancel()} centered scrollable fullscreen="sm-down" aria-labelledby={titleId}>
      <form noValidate onSubmit={submit} className="d-flex flex-column overflow-hidden">
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">{member.fullName}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {problem && <div className="alert alert-danger py-2" role="alert">{problem}</div>}
          <fieldset disabled={busy}>
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

            <StaffPhoto
              member={member}
              name={draft.name.trim()}
              busy={busy}
              uploading={photoBusy}
              onUpload={onUploadPhoto}
              onRemove={onRemovePhoto}
            />

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

            <fieldset className="mb-3">
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

            <AbsencesFields absences={draft.absences} idPrefix={`${id}-absences`} onChange={(absences) => change({ absences })} />
          </fieldset>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" disabled={busy} onClick={onCancel}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={busy}>{saving ? 'Guardando…' : 'Guardar'}</button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}

/**
 * «Quién atiende» (Fase 24J, ADR 0032), below «Citas en línea» for owners and admins of a business with the agenda: the
 * active team, and for each person whether customers book them online, the name customers see, their services and their
 * own hours, and since Fase 31I their photo and the days they do not attend. While someone takes online appointments,
 * customers choose the person (or no preference) and the free times come from each one; the server decides them.
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
  const [photoBusy, setPhotoBusy] = useState(false);
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
        absences: absencesToSave(draft.absences),
      });
      replaceMember(saved);
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

  /** The person as the server answered, in the list. */
  function replaceMember(saved: OnlineBookingStaffMember) {
    queryClient.setQueryData<OnlineBookingStaffSettings>(STAFF_KEY, (current) => current && {
      ...current,
      members: current.members.map((item) => (item.userId === saved.userId ? saved : item)),
    });
  }

  /**
   * The photo went up or away: the list, the open form and the booking page show the new one. Only the photo changes, on
   * what each one has now (the list may have come again meanwhile).
   */
  const photoChanged = (member: OnlineBookingStaffMember, photoUrl: string | null) => {
    queryClient.setQueryData<OnlineBookingStaffSettings>(STAFF_KEY, (current) => current && {
      ...current,
      members: current.members.map((item) => (item.userId === member.userId ? { ...item, photoUrl } : item)),
    });
    setEditing((current) => current && current.member.userId === member.userId
      ? { ...current, member: { ...current.member, photoUrl } }
      : current);
    void queryClient.invalidateQueries({ queryKey: ['public-booking'] });
  };

  const uploadPhoto = async (file: File) => {
    if (!editing) return;
    const { member } = editing;
    setPhotoBusy(true);
    try {
      const saved = await appointmentsApi.uploadOnlineBookingStaffPhoto(member.userId, file);
      photoChanged(member, saved.photoUrl ?? null);
      toast.success(`Foto guardada: tus clientes ya la ven junto a «${member.name}».`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo subir la foto.'));
    } finally {
      setPhotoBusy(false);
    }
  };

  const removePhoto = async () => {
    if (!editing) return;
    const { member } = editing;
    setPhotoBusy(true);
    try {
      await appointmentsApi.deleteOnlineBookingStaffPhoto(member.userId);
      photoChanged(member, null);
      toast.success('Foto quitada: tus clientes ven su inicial.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo quitar la foto.'));
    } finally {
      setPhotoBusy(false);
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
                    <PhotoOrInitial src={member.photoUrl} name={member.fullName} className="obs-photo obs-photo-sm" />
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
          photoBusy={photoBusy}
          onChange={(draft) => setEditing({ ...editing, draft })}
          onCancel={() => setEditing(null)}
          onSave={() => void save()}
          onUploadPhoto={(file) => void uploadPhoto(file)}
          onRemovePhoto={() => void removePhoto()}
        />
      )}
    </section>
  );
}
