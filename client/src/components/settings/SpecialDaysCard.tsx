import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorCode, getApiErrorMessage } from '../../api/errors';
import { useRemoveSpecialDay, useSaveSpecialDay, useSpecialDays, useUpdateSpecialDay } from '../../api/queries';
import type { Schemas } from '../../api/typedClient';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OnlineMenuMode, SpecialDayKind, TenantCapability, type OpeningIntervalDto } from '../../types';
import {
  BUSINESS_TIME_ZONES,
  DEFAULT_TIME_ZONE,
  addCalendarDays,
  calendarDaysBetween,
  formatCalendarDate,
  formatLongCalendarDay,
  formatShortCalendarDay,
} from '../../utils/dates';
import {
  DEFAULT_RANGE,
  MAX_RANGES_PER_DAY,
  dayName,
  dayOfWeekOf,
  describeOpenings,
  describeSpecialDay,
  endsAfterMidnight,
  openingsOn,
  rangeHint,
  specialDayDates,
  specialDayDatesInWords,
  specialDayRule,
  type TimeRange,
} from '../../utils/openingHours';
import { hasCapability } from '../../utils/tenantCapabilities';
import './OpeningHoursCard.css';
import './SpecialDaysCard.css';

type SpecialDay = Schemas['SpecialDayDto'];
type HolidaySuggestion = Schemas['HolidaySuggestionDto'];
type SpecialDays = Schemas['SpecialDaysDto'];

// Holidays shown before "Ver más".
const SUGGESTIONS_SHOWN = 3;
// "Varios días" starts a week long.
const DEFAULT_EXTRA_DAYS = 6;
// Same limits as the public menu (PublicOpeningHoursDto): today's special day in its "Hoy", and at most 5 of the next 31
// days in its list.
const MENU_SPECIAL_DAYS_AHEAD = 31;
const MENU_MAX_SPECIAL_DAYS = 5;
// The API refused because the special days changed meanwhile (changed, removed, ended or a date taken by someone else):
// the list is read again to show them as they are now.
const LIST_CHANGED_CODES: readonly string[] = ['concurrency_conflict', 'not_found', 'domain_rule', 'business_rule'];

/** The dialog's form. Dates stay as a date input writes them ("2026-12-25"). */
interface SpecialDayDraft {
  /** The special day being changed, with the revision read; none for a new one. */
  editing?: { id: string; revision: number; inProgress: boolean };
  from: string;
  severalDays: boolean;
  to: string;
  kind: SpecialDayKind;
  ranges: TimeRange[];
  reason: string;
}

const NEW_DRAFT: SpecialDayDraft = { from: '', severalDays: false, to: '', kind: SpecialDayKind.Closed, ranges: [], reason: '' };

function draftOf(day: SpecialDay): SpecialDayDraft {
  return {
    editing: { id: day.id, revision: day.revision, inProgress: day.inProgress },
    from: day.from,
    severalDays: day.to !== day.from,
    to: day.to,
    kind: day.kind,
    ranges: day.openings.map(({ opens, closes }) => ({ opens, closes })),
    reason: day.reason ?? '',
  };
}

/** A holiday opens the dialog filled in; nothing is saved until the owner says so. */
function draftOfSuggestion(suggestion: HolidaySuggestion): SpecialDayDraft {
  return {
    from: suggestion.date,
    severalDays: false,
    to: suggestion.date,
    kind: suggestion.kind,
    ranges: suggestion.openings.map(({ opens, closes }) => ({ opens, closes })),
    reason: suggestion.name,
  };
}

/** Other hours with a time left blank, or none yet. */
function missingTime(draft: SpecialDayDraft): boolean {
  return draft.ranges.length === 0 || draft.ranges.some((range) => !range.opens || !range.closes);
}

/** What the browser can check before asking the API: blanks and order. The API decides the rest. */
function draftError(draft: SpecialDayDraft): string | null {
  if (!draft.from || (draft.severalDays && !draft.to)) return 'Elige la fecha.';
  if (draft.severalDays && draft.to < draft.from) return 'La fecha final no puede ser antes de la inicial.';
  if (draft.kind === SpecialDayKind.SpecialHours && missingTime(draft)) return 'Escribe a qué hora abres y cierras ese día.';
  return null;
}

function bodyOf(draft: SpecialDayDraft): Schemas['SaveSpecialDayRequest'] {
  return {
    from: draft.from,
    // One day goes without an end.
    to: draft.severalDays && draft.to !== draft.from ? draft.to : null,
    kind: draft.kind,
    openings: draft.kind === SpecialDayKind.Closed ? [] : draft.ranges,
    reason: draft.reason.trim() || null,
  };
}

/** "7 días: del lunes 21 al domingo 27 de diciembre." for several days. */
function lengthSummary(draft: SpecialDayDraft): string | null {
  if (!draft.severalDays || !draft.from || !draft.to) return null;
  const days = calendarDaysBetween(draft.from, draft.to) + 1;
  if (days < 2) return null;
  const sameMonth = draft.from.slice(0, 7) === draft.to.slice(0, 7);
  return `${days} días: del ${formatLongCalendarDay(draft.from, !sameMonth)} al ${formatLongCalendarDay(draft.to)}.`;
}

/** How the public menu will put it: "Cerrado por Navidad", "9:00 a 18:00 · Nochebuena". */
function customerPreview(draft: SpecialDayDraft): string | null {
  const reason = draft.reason.trim();
  if (draft.kind === SpecialDayKind.Closed) return reason ? `Cerrado por ${reason}` : 'Cerrado';
  if (missingTime(draft)) return null;
  return `${describeOpenings(draft.ranges)} · ${reason || 'horario especial'}`;
}

/** Whether the public menu already shows the special day that starts on that date, in the list the API answered. */
function menuShowsNow(specialDays: SpecialDays, from: string): boolean {
  const { today } = specialDays;
  if (from <= today) return true;
  const lastDate = addCalendarDays(today, MENU_SPECIAL_DAYS_AHEAD);
  return specialDays.upcoming
    .filter((day) => day.to > today && day.from <= lastDate)
    .slice(0, MENU_MAX_SPECIAL_DAYS)
    .some((day) => day.from === from);
}

function confirmMessage(day: SpecialDay): string {
  if (day.inProgress) return 'Desde hoy vuelve tu horario de siempre; los días que ya pasaron se quedan en Pasados.';
  if (day.from === day.to) return `El ${formatLongCalendarDay(day.from)} vuelve tu horario de siempre.`;
  const dayAndMonth = (date: string) => formatCalendarDate(date, { day: 'numeric', month: 'long' });
  return `Del ${dayAndMonth(day.from)} al ${dayAndMonth(day.to)} vuelve tu horario de siempre.`;
}

/**
 * Días especiales in Mi negocio (owners and admins): the dates the business closes or opens with other hours, in its
 * time zone. The public menu and the WhatsApp automatic replies use them; the API decides what each date means and
 * keeps the past ones as history. The holidays it suggests open the dialog filled in, never save by themselves.
 */
export default function SpecialDaysCard() {
  const { tenant, capabilities } = useTenant();
  const specialDays = useSpecialDays(!!tenant);
  const saveDay = useSaveSpecialDay();
  const updateDay = useUpdateSpecialDay();
  const removeDay = useRemoveSpecialDay();
  const { confirm } = useConfirm();
  const toast = useToast();
  const suggestionsTitleId = useId();
  const [draft, setDraft] = useState<SpecialDayDraft | null>(null);
  const [error, setError] = useState('');
  const [allSuggestions, setAllSuggestions] = useState(false);
  if (!tenant) return null;

  const data = specialDays.data;
  const zone = BUSINESS_TIME_ZONES.find((item) => item.id === (tenant.timeZone || DEFAULT_TIME_ZONE))?.label;
  const menuOn = (tenant.onlineMenuMode ?? OnlineMenuMode.Off) !== OnlineMenuMode.Off;
  const takesOrders = tenant.onlineMenuMode === OnlineMenuMode.Orders;
  const hasTables = hasCapability(capabilities, TenantCapability.Food);
  const saving = saveDay.isPending || updateDay.isPending;

  const openDialog = (next: SpecialDayDraft) => {
    setDraft(next);
    setError('');
  };

  const save = async () => {
    if (!draft) return;
    const invalid = draftError(draft);
    if (invalid) {
      setError(invalid);
      return;
    }
    setError('');
    try {
      const saved = draft.editing
        ? await updateDay.mutateAsync({ id: draft.editing.id, body: { revision: draft.editing.revision, ...bodyOf(draft) } })
        : await saveDay.mutateAsync(bodyOf(draft));
      setDraft(null);
      if (!menuOn) toast.success('Día especial guardado.');
      else if (menuShowsNow(saved, draft.from)) toast.success('Día especial guardado: tu menú en línea ya lo muestra.');
      else toast.success('Día especial guardado: tu menú en línea lo mostrará cuando se acerque la fecha.');
    } catch (err: unknown) {
      const message = getApiErrorMessage(err, 'No se pudo guardar el día especial.');
      const code = getApiErrorCode(err);
      if (code && LIST_CHANGED_CODES.includes(code)) void specialDays.refetch();
      // Changed or removed by someone else: saving it again would fail the same way. It opens again from the list.
      if (code === 'concurrency_conflict' || code === 'not_found') {
        setDraft(null);
        toast.error(message);
      } else {
        setError(message);
      }
    }
  };

  const remove = async (day: SpecialDay) => {
    const accepted = await confirm({
      title: 'Quitar el día especial',
      message: confirmMessage(day),
      confirmLabel: 'Quitar día especial',
      danger: true,
    });
    if (!accepted) return;
    try {
      await removeDay.mutateAsync({ id: day.id, revision: day.revision });
      toast.success('Día especial quitado.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo quitar el día especial.'));
      const code = getApiErrorCode(err);
      if (code && LIST_CHANGED_CODES.includes(code)) void specialDays.refetch();
    }
  };

  const suggestions = data?.suggestions ?? [];
  const shownSuggestions = allSuggestions ? suggestions : suggestions.slice(0, SUGGESTIONS_SHOWN);

  return (
    <div className="card">
      <div className="card-body">
        <h4 className="header-title mt-0 mb-1">Días especiales</h4>
        <p className="text-muted small mb-3">
          Los días que cierras o abres con otro horario, en la hora de tu negocio{zone ? `: ${zone}` : ''}. Tu menú en línea
          y tus respuestas automáticas de WhatsApp los toman en cuenta.
        </p>
        {data && !data.hasWeeklyHours && (
          <p className="small mb-3">Sin horario de la semana, tu menú solo se ve cerrado en estos días.</p>
        )}
        {takesOrders && (
          <p className="small text-muted mb-3">
            En los días que cierras, tu menú no recibe pedidos{hasTables ? ', ni siquiera desde el QR de una mesa' : ''}.
          </p>
        )}

        {specialDays.isPending ? (
          <p className="text-muted small mb-0" aria-busy="true">Cargando…</p>
        ) : specialDays.isError || !data ? (
          <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2 mb-0" role="alert">
            <span className="me-auto">No se pudieron cargar tus días especiales.</span>
            <button type="button" className="btn btn-sm btn-light" onClick={() => void specialDays.refetch()}>Reintentar</button>
          </div>
        ) : (
          <>
            {data.upcoming.length === 0 ? (
              <p className="small mb-0">
                {data.past.length > 0 ? 'No tienes días especiales próximos.' : 'Aún no tienes días especiales.'} Marca los
                días que cierras (el Día de la Revolución, Navidad, vacaciones) o abres con otro horario.
              </p>
            ) : (
              // A short, bounded list (at most 120 dates in periods) without search: a list in the card, not DataTable.
              <ul className="list-group list-group-flush special-days-list" aria-label="Próximos días especiales">
                {data.upcoming.map((day) => {
                  const dates = specialDayDatesInWords(day.from, day.to);
                  return (
                    <li key={day.id} className="list-group-item special-days-item">
                      <div className="special-days-text">
                        <div className="d-flex flex-wrap align-items-center gap-2">
                          <span className="fw-semibold">{specialDayDates(day.from, day.to)}</span>
                          {day.inProgress && <span className="ta-chip ta-chip-preparing">En curso</span>}
                        </div>
                        <div className="small">
                          {specialDayRule(day)}
                          {day.reason && <span className="text-muted"> · {day.reason}</span>}
                        </div>
                      </div>
                      <span className="text-nowrap">
                        <button
                          type="button"
                          className="btn btn-link btn-sm p-0 me-3"
                          aria-label={`Editar ${dates}`}
                          onClick={() => openDialog(draftOf(day))}
                        >
                          Editar
                        </button>
                        <button
                          type="button"
                          className="btn btn-link btn-sm p-0 text-danger"
                          aria-label={`Quitar ${dates}`}
                          onClick={() => void remove(day)}
                          disabled={removeDay.isPending}
                        >
                          Quitar
                        </button>
                      </span>
                    </li>
                  );
                })}
              </ul>
            )}

            <div className="d-flex justify-content-end mt-3">
              <button type="button" className="btn btn-light" onClick={() => openDialog(NEW_DRAFT)}>
                <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar día especial
              </button>
            </div>

            {suggestions.length > 0 && (
              <section className="special-days-suggestions mt-3" aria-labelledby={suggestionsTitleId}>
                <h5 id={suggestionsTitleId} className="fs-6 mb-1">Próximos festivos</h5>
                <ul className="list-unstyled mb-1">
                  {shownSuggestions.map((suggestion) => (
                    <li key={suggestion.date} className="special-days-suggestion">
                      <div className="special-days-text">
                        <div>{formatShortCalendarDay(suggestion.date)} · {suggestion.name}</div>
                        <div className="small text-muted">
                          {suggestion.kind === SpecialDayKind.Closed ? 'Cerrado todo el día' : describeOpenings(suggestion.openings)}
                          {suggestion.statutory ? ' · descanso obligatorio' : ''}
                        </div>
                      </div>
                      <button
                        type="button"
                        className="btn btn-light btn-sm"
                        aria-label={`Agregar ${suggestion.name}, ${formatLongCalendarDay(suggestion.date)}`}
                        onClick={() => openDialog(draftOfSuggestion(suggestion))}
                      >
                        Agregar
                      </button>
                    </li>
                  ))}
                </ul>
                {suggestions.length > SUGGESTIONS_SHOWN && (
                  <button type="button" className="btn btn-link btn-sm p-0" onClick={() => setAllSuggestions(!allSuggestions)}>
                    {allSuggestions ? 'Ver menos' : `Ver ${suggestions.length - SUGGESTIONS_SHOWN} más`}
                  </button>
                )}
                <p className="small text-muted mt-1 mb-0">Son sugerencias: nada cambia hasta que guardes.</p>
              </section>
            )}

            {data.past.length > 0 && (
              <details className="special-days-past mt-3">
                <summary className="small">Pasados (últimos 90 días)</summary>
                <ul className="list-unstyled small text-muted mt-2 mb-0" aria-label="Días especiales pasados">
                  {data.past.map((day) => <li key={day.id}>{describeSpecialDay(day)}</li>)}
                </ul>
              </details>
            )}
          </>
        )}
      </div>

      {draft && data && (
        <SpecialDayDialog
          draft={draft}
          data={data}
          weekly={tenant.openingHours ?? []}
          error={error}
          saving={saving}
          onChange={(next) => setDraft(next)}
          onCancel={() => setDraft(null)}
          onSave={() => void save()}
        />
      )}
    </div>
  );
}

interface SpecialDayDialogProps {
  draft: SpecialDayDraft;
  data: SpecialDays;
  /** The weekly hours: special hours start from that weekday's, and the hints read them with the special days. */
  weekly: readonly OpeningIntervalDto[];
  error: string;
  saving: boolean;
  onChange: (draft: SpecialDayDraft) => void;
  onCancel: () => void;
  onSave: () => void;
}

/** Nuevo or Editar día especial: the date or dates, closed or other hours, and the reason customers read. */
function SpecialDayDialog({ draft, data, weekly, error, saving, onChange, onCancel, onSave }: SpecialDayDialogProps) {
  const titleId = useId();
  const id = useId();
  const inProgress = draft.editing?.inProgress ?? false;
  // "Today" is the business's, as the API counts it, never the device's.
  const lastDate = addCalendarDays(data.today, data.maxDaysAhead);
  const longestTo = draft.from ? addCalendarDays(draft.from, data.maxDaysPerPeriod - 1) : lastDate;
  const lastTo = longestTo < lastDate ? longestTo : lastDate;
  const hasWeeklyHours = weekly.length > 0;
  const weekday = draft.from ? dayOfWeekOf(draft.from) : null;
  const closedByWeek = hasWeeklyHours && weekday !== null && !draft.severalDays && draft.kind === SpecialDayKind.Closed
    && openingsOn(weekly, weekday).length === 0;
  // The night before keeps its hours when they run past midnight into this date: a special day marked on that date
  // (other than the one being changed, which leaves it) decides them, or else its weekday's hours.
  const dateBefore = draft.from && !inProgress ? addCalendarDays(draft.from, -1) : null;
  const markedBefore = dateBefore === null ? undefined : [...data.upcoming, ...data.past]
    .find((day) => day.id !== draft.editing?.id && day.from <= dateBefore && dateBefore <= day.to);
  const openingsBefore = dateBefore === null ? []
    : markedBefore ? (markedBefore.kind === SpecialDayKind.Closed ? [] : markedBefore.openings)
    : openingsOn(weekly, dayOfWeekOf(dateBefore));
  const spills = openingsBefore.some(endsAfterMidnight);
  // Before today it can no longer be marked or changed.
  const changeBefore = dateBefore !== null && dateBefore >= data.today;
  const summary = lengthSummary(draft);
  const preview = customerPreview(draft);

  const change = (patch: Partial<SpecialDayDraft>) => onChange({ ...draft, ...patch });
  const changeRange = (index: number, patch: Partial<TimeRange>) =>
    change({ ranges: draft.ranges.map((range, position) => (position === index ? { ...range, ...patch } : range)) });

  const chooseHours = () => {
    // Other hours start from that weekday's, which are usually close to what the owner wants; or 9:00 to 20:00.
    const usual = weekday === null ? [] : openingsOn(weekly, weekday);
    const ranges = draft.ranges.length > 0 ? draft.ranges : usual.length > 0 ? usual : [{ ...DEFAULT_RANGE }];
    change({ kind: SpecialDayKind.SpecialHours, ranges });
  };

  const toggleSeveralDays = (on: boolean) => {
    if (!on) {
      change({ severalDays: false, to: draft.from });
      return;
    }
    const weekLater = draft.from ? addCalendarDays(draft.from, DEFAULT_EXTRA_DAYS) : '';
    change({ severalDays: true, to: weekLater && weekLater > lastTo ? lastTo : weekLater });
  };

  return (
    <Modal show onHide={() => !saving && onCancel()} centered scrollable fullscreen="sm-down" aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">{draft.editing ? 'Editar día especial' : 'Nuevo día especial'}</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger py-2" role="alert">{error}</div>}
        <fieldset disabled={saving}>
          <div className="row g-2">
            <div className="col-sm-6">
              <label className="form-label" htmlFor={`${id}-from`}>Fecha</label>
              <input
                id={`${id}-from`}
                type="date"
                className="form-control"
                min={data.today}
                max={lastDate}
                value={draft.from}
                disabled={inProgress}
                onChange={(event) => change({ from: event.target.value, ...(draft.severalDays ? {} : { to: event.target.value }) })}
              />
            </div>
            {draft.severalDays && (
              <div className="col-sm-6">
                <label className="form-label" htmlFor={`${id}-to`}>Hasta</label>
                <input
                  id={`${id}-to`}
                  type="date"
                  className="form-control"
                  min={draft.from || data.today}
                  max={lastTo}
                  value={draft.to}
                  onChange={(event) => change({ to: event.target.value })}
                />
              </div>
            )}
          </div>
          {inProgress && <div className="form-text">Ya empezó: puedes cambiar hasta cuándo dura, el horario y el motivo.</div>}
          <div className="form-check form-switch mt-2">
            <input
              id={`${id}-several`}
              className="form-check-input"
              type="checkbox"
              role="switch"
              checked={draft.severalDays}
              disabled={inProgress}
              onChange={(event) => toggleSeveralDays(event.target.checked)}
            />
            <label className="form-check-label" htmlFor={`${id}-several`}>Varios días</label>
          </div>
          {summary && <p className="small text-muted mb-0">{summary}</p>}

          <fieldset className="mt-3">
            <legend className="visually-hidden">Ese día</legend>
            <div className="form-check">
              <input
                id={`${id}-closed`}
                className="form-check-input"
                type="radio"
                name={`${id}-kind`}
                checked={draft.kind === SpecialDayKind.Closed}
                onChange={() => change({ kind: SpecialDayKind.Closed })}
              />
              <label className="form-check-label" htmlFor={`${id}-closed`}>Cierro todo el día</label>
            </div>
            <div className="form-check">
              <input
                id={`${id}-hours`}
                className="form-check-input"
                type="radio"
                name={`${id}-kind`}
                checked={draft.kind === SpecialDayKind.SpecialHours}
                onChange={chooseHours}
              />
              <label className="form-check-label" htmlFor={`${id}-hours`}>Abro con otro horario</label>
            </div>
          </fieldset>

          {draft.kind === SpecialDayKind.SpecialHours && (
            <div className="opening-hours-ranges mt-2">
              {draft.ranges.map((range, index) => {
                const which = index === 0 ? '' : ', segundo horario';
                const hint = rangeHint(range);
                return (
                  <div key={index} className="opening-hours-range">
                    <input
                      type="time"
                      className="form-control form-control-sm"
                      aria-label={`Abre ese día${which}`}
                      value={range.opens}
                      onChange={(event) => changeRange(index, { opens: event.target.value })}
                    />
                    <span className="text-muted small" aria-hidden="true">a</span>
                    <input
                      type="time"
                      className="form-control form-control-sm"
                      aria-label={`Cierra ese día${which}`}
                      value={range.closes}
                      onChange={(event) => changeRange(index, { closes: event.target.value })}
                    />
                    {index > 0 && (
                      <button
                        type="button"
                        className="btn btn-sm btn-light"
                        aria-label="Quitar el segundo horario"
                        title="Quitar este horario"
                        onClick={() => change({ ranges: draft.ranges.filter((_, position) => position !== index) })}
                      >
                        <i className="ri-close-line" aria-hidden="true"></i>
                      </button>
                    )}
                    {hint && <span className="small text-muted opening-hours-hint">{hint}</span>}
                  </div>
                );
              })}
              {draft.ranges.length < MAX_RANGES_PER_DAY && (
                <button
                  type="button"
                  className="btn btn-link btn-sm opening-hours-add"
                  onClick={() => change({ ranges: [...draft.ranges, { opens: '', closes: '' }] })}
                >
                  <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar otro horario
                </button>
              )}
            </div>
          )}
          {closedByWeek && <div className="form-text">Ese día ya cierras por tu horario de la semana.</div>}
          {spills && dateBefore !== null && (
            <div className="form-text">
              {markedBefore
                ? `El ${formatLongCalendarDay(dateBefore)} tu horario especial termina después de medianoche y se respeta.`
                : `El ${dayName(dayOfWeekOf(dateBefore))} tu horario termina después de medianoche y se respeta.`}
              {changeBefore && (markedBefore ? ' Para cambiarlo, edita ese día especial.' : ' Para cambiarlo, marca también ese día.')}
            </div>
          )}

          <div className="mt-3">
            <label className="form-label" htmlFor={`${id}-reason`}>Motivo (opcional)</label>
            <input
              id={`${id}-reason`}
              className="form-control"
              maxLength={data.maxReasonLength}
              placeholder="Por ejemplo: Navidad, vacaciones, inventario"
              aria-describedby={`${id}-reason-help`}
              value={draft.reason}
              onChange={(event) => change({ reason: event.target.value })}
            />
            <div id={`${id}-reason-help`} className="form-text">
              Lo ve cualquiera que abra tu menú. Máximo {data.maxReasonLength} caracteres.
            </div>
          </div>
          {preview && <p className="small mt-3 mb-0">Tus clientes verán: {preview}</p>}
        </fieldset>
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onCancel} disabled={saving}>Cancelar</button>
        <button type="button" className="btn btn-primary" onClick={onSave} disabled={saving}>
          {saving ? 'Guardando…' : 'Guardar'}
        </button>
      </Modal.Footer>
    </Modal>
  );
}
