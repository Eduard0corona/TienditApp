import type { AppointmentVisitDto } from '../../types';
import type { VisitFieldsState } from './AppointmentVisitFields';

/** The same address as typed (the server compares it trimmed): then the visit keeps the zone and the charge agreed. */
export function isSameAddress(visit: AppointmentVisitDto, value: VisitFieldsState): boolean {
  return visit.postalCode === value.postalCode.trim()
    && visit.addressLine === value.addressLine.trim()
    && (visit.references ?? '') === value.references.trim();
}
