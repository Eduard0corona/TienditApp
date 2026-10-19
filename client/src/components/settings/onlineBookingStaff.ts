import type { OnlineBookingStaffMember, OnlineBookingStaffSettings } from '../../api/appointmentsApi';
import type { OpeningIntervalDto } from '../../types';
import { dayName, firstDayMissingTime, fromDraft, toDraft, type DayDraft } from '../../utils/openingHours';

/** Forty characters for customers, an emoji counting as one. */
export const MAX_STAFF_NAME = 40;

type ServiceOption = OnlineBookingStaffSettings['services'][number];

export interface StaffDraft {
  enabled: boolean;
  name: string;
  allServices: boolean;
  serviceIds: string[];
  /** Their own hours; otherwise the business's. */
  ownHours: boolean;
  week: DayDraft[];
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
});

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
  return null;
}

/** How a person reads in the list: their services and their hours. */
export function describeStaff(member: OnlineBookingStaffMember, services: readonly ServiceOption[]): string {
  const chosen = chosenServices(member.serviceIds, services).length;
  const described = member.allServices || (chosen > 0 && chosen === services.length)
    ? 'Todos los servicios'
    : chosen === 1 ? '1 servicio' : `${chosen} servicios`;
  return `${described} · ${member.weeklyHours ? 'Horario propio' : 'Horario del negocio'}`;
}
