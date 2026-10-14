import type { AppointmentVisitDto } from '../../types';
import type { VisitFieldsState } from './AppointmentVisitFields';

/**
 * The same place as typed (the server compares it trimmed): the postal code and the address. Then the visit keeps the zone
 * and the charge agreed; the references only say how to find it.
 */
export function isSameAddress(visit: AppointmentVisitDto, value: VisitFieldsState): boolean {
  return visit.postalCode === value.postalCode.trim() && visit.addressLine === value.addressLine.trim();
}
