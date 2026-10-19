import type { OnlineBookingStaffMember } from '../../api/appointmentsApi';
import type { OpeningIntervalDto } from '../../types';
import { dayName, firstDayMissingTime, fromDraft, toDraft, type DayDraft } from '../../utils/openingHours';

/** Forty characters for customers, an emoji counting as one (the server counts the same way). */
export const MAX_STAFF_NAME = 40;

export interface StaffDraft {
  enabled: boolean;
  name: string;
  allServices: boolean;
  serviceIds: string[];
  /** Their own hours; otherwise the business's. */
  ownHours: boolean;
  week: DayDraft[];
}

export const draftOf = (member: OnlineBookingStaffMember, businessHours: OpeningIntervalDto[]): StaffDraft => ({
  // Setting someone up is to take their online appointments: the switch starts on.
  enabled: member.configured ? member.enabled : true,
  name: member.name,
  allServices: member.allServices,
  serviceIds: member.serviceIds,
  ownHours: !!member.weeklyHours,
  // Their own hours start from the business's: a person usually works part of them.
  week: toDraft(member.weeklyHours ?? businessHours),
});

/** What the draft lacks, in the owner's words; null when it can be saved. */
export function staffDraftProblem(draft: StaffDraft, serviceCount: number): string | null {
  const name = draft.name.trim();
  if (!name) return 'Escribe el nombre que verán tus clientes.';
  if (Array.from(name).length > MAX_STAFF_NAME) return `El nombre que ven tus clientes puede tener hasta ${MAX_STAFF_NAME} caracteres.`;
  if (!draft.allServices && draft.serviceIds.length === 0) {
    return serviceCount === 0 ? 'Aún no tienes servicios para agendar en línea: elige «Todos los servicios».' : 'Elige al menos un servicio, o todos.';
  }
  if (draft.ownHours) {
    const missing = firstDayMissingTime(draft.week);
    if (missing !== null) return `Escribe a qué hora entra y sale el ${dayName(missing)}.`;
    if (fromDraft(draft.week).length === 0) return 'Marca al menos un día que atiende, o usa el horario del negocio.';
  }
  return null;
}

/** How a person reads in the list: their services and their hours. */
export function describeStaff(member: OnlineBookingStaffMember, serviceCount: number): string {
  const services = member.allServices || member.serviceIds.length >= serviceCount
    ? 'Todos los servicios'
    : member.serviceIds.length === 1 ? '1 servicio' : `${member.serviceIds.length} servicios`;
  return `${services} · ${member.weeklyHours ? 'Horario propio' : 'Horario del negocio'}`;
}
