import type { OnlineBookingStaffMember, OnlineBookingStaffSettings } from '../../api/appointmentsApi';
import type { OpeningIntervalDto } from '../../types';
import { calendarDaysBetween, formatShortCalendarDay } from '../../utils/dates';
import { dayName, firstDayMissingTime, fromDraft, toDraft, type DayDraft } from '../../utils/openingHours';

/** Forty characters for customers, an emoji counting as one. */
export const MAX_STAFF_NAME = 40;
/** Absences still to come a person may have, and the days each may last (Fase 31I; the server says the same). */
export const MAX_ABSENCES = 20;
export const MAX_ABSENCE_DAYS = 366;

/** A run of whole days a person does not attend, as the date inputs hold it ("2026-11-16"); empty while not chosen. */
export interface AbsenceDraft {
  /** Only for the list on screen: which inputs are which while rows come and go. */
  key: string;
  from: string;
  to: string;
}

let nextAbsenceKey = 0;

/** A new row of the absences, with the days given or empty. */
export const absenceDraft = (from = '', to = from): AbsenceDraft => ({ key: `absence-${nextAbsenceKey++}`, from, to });

type ServiceOption = OnlineBookingStaffSettings['services'][number];

export interface StaffDraft {
  enabled: boolean;
  name: string;
  allServices: boolean;
  serviceIds: string[];
  /** Their own hours; otherwise the business's. */
  ownHours: boolean;
  week: DayDraft[];
  /** The days they do not attend (Fase 31I). */
  absences: AbsenceDraft[];
}

/** Characters as a person reads them (the server counts the same way): an emoji with its skin tone or a flag is one. */
export function nameLength(name: string): number {
  if (typeof Intl.Segmenter !== 'function') return Array.from(name).length;
  return Array.from(new Intl.Segmenter('es', { granularity: 'grapheme' }).segment(name)).length;
}

/**
 * The draft of a person. Setting someone up is to take their online appointments, so the switch starts on, unless the
 * business already has as many as it may.
 */
export const draftOf = (
  member: OnlineBookingStaffMember,
  businessHours: OpeningIntervalDto[],
  roomForOneMore = true,
): StaffDraft => ({
  enabled: member.configured ? member.enabled : roomForOneMore,
  name: member.name,
  allServices: member.allServices,
  serviceIds: member.serviceIds,
  ownHours: !!member.weeklyHours,
  // Their own hours start from the business's: a person usually works part of them.
  week: toDraft(member.weeklyHours ?? businessHours),
  absences: (member.absences ?? []).map((absence) => absenceDraft(absence.from, absence.to)),
});

/** What the absences lack or cannot be, in the owner's words (the server's messages); null when they can be saved. */
export function absencesProblem(absences: readonly AbsenceDraft[]): string | null {
  if (absences.some((absence) => !absence.from || !absence.to)) return 'Escribe desde y hasta qué día de cada ausencia.';
  if (absences.some((absence) => absence.to < absence.from)) return 'Una ausencia no puede terminar antes de empezar.';
  if (absences.some((absence) => calendarDaysBetween(absence.from, absence.to) + 1 > MAX_ABSENCE_DAYS)) {
    return 'Una ausencia puede durar hasta un año.';
  }
  if (absences.length > MAX_ABSENCES) return `Una persona puede tener hasta ${MAX_ABSENCES} ausencias por venir.`;
  const ordered = [...absences].sort((a, b) => a.from.localeCompare(b.from));
  if (ordered.some((absence, index) => index > 0 && absence.from <= ordered[index - 1].to)) {
    return 'Dos ausencias se cruzan: júntalas en una.';
  }
  return null;
}

/** The absences to save, in order and without the rows of the screen. */
export const absencesToSave = (absences: readonly AbsenceDraft[]) =>
  [...absences].sort((a, b) => a.from.localeCompare(b.from)).map(({ from, to }) => ({ from, to }));

/** "Vie 25 dic" or "Vie 25 dic – Lun 4 ene": a run of days in a short list. */
export function describeAbsence(absence: { from: string; to: string }): string {
  return absence.from === absence.to
    ? formatShortCalendarDay(absence.from)
    : `${formatShortCalendarDay(absence.from)} – ${formatShortCalendarDay(absence.to)}`;
}

/** The services of the list the person does: a paused one stays chosen and counts. */
export const chosenServices = (serviceIds: readonly string[], services: readonly ServiceOption[]) =>
  services.filter((service) => serviceIds.includes(service.id));

/** What the draft lacks, in the owner's words; null when it can be saved. */
export function staffDraftProblem(draft: StaffDraft, services: readonly ServiceOption[]): string | null {
  const name = draft.name.trim();
  if (!name) return 'Escribe el nombre que verán tus clientes.';
  if (nameLength(name) > MAX_STAFF_NAME) return `El nombre que ven tus clientes puede tener hasta ${MAX_STAFF_NAME} caracteres.`;
  if (!draft.allServices && chosenServices(draft.serviceIds, services).length === 0) {
    return services.length === 0 ? 'Aún no tienes servicios para agendar en línea: elige «Todos los servicios».' : 'Elige al menos un servicio, o todos.';
  }
  if (draft.ownHours) {
    const missing = firstDayMissingTime(draft.week);
    if (missing !== null) return `Escribe a qué hora entra y sale el ${dayName(missing)}.`;
    if (fromDraft(draft.week).length === 0) return 'Marca al menos un día que atiende, o usa el horario del negocio.';
  }
  return absencesProblem(draft.absences);
}

/** How a person reads in the list: their services, their hours and the next days they are away. */
export function describeStaff(member: OnlineBookingStaffMember, services: readonly ServiceOption[]): string {
  const chosen = chosenServices(member.serviceIds, services).length;
  const described = member.allServices || (chosen > 0 && chosen === services.length)
    ? 'Todos los servicios'
    : chosen === 1 ? '1 servicio' : `${chosen} servicios`;
  const hours = `${described} · ${member.weeklyHours ? 'Horario propio' : 'Horario del negocio'}`;
  const [next, ...more] = member.absences ?? [];
  if (!next) return hours;
  return `${hours} · No atiende: ${describeAbsence(next)}${more.length > 0 ? ` y ${more.length} más` : ''}`;
}
