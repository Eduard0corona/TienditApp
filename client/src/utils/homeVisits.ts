// Appointments at the customer's address (Fase 22E). Mirrors the API (Appointment.ArrangeVisit, Order.ChargeVisit): the
// visit charges what its zone charged to go there when it was arranged, one more concept of the sale at the business's
// IVA rate and with its tax policy. It is an estimate for the cashier: the server charges what the appointment says and
// rejects the sale if the total it computes differs from the one shown.
import { AppointmentStatus } from '../types';
import { formatMoney } from './money';
import { estimateOrderTotals, type OrderTotalsEstimate, type TotalsLine } from './orderTotals';

/** The server's words (HomeVisitRules). */
export const CANCELLED_SALE_MESSAGE = 'La cita se canceló: ya no se cobra.';
export const NO_SHOW_SALE_MESSAGE = 'El cliente no se presentó a la cita: no se cobra.';
export const ALREADY_SOLD_MESSAGE = 'La cita ya tiene su venta.';
/** The server's words (OnlineBookingRules): a request booked online is accepted before it is charged. */
export const ACCEPT_BEFORE_CHARGING_MESSAGE = 'Acepta la cita antes de cobrarla.';
export const VISIT_POSTAL_CODE_REQUIRED_MESSAGE = 'Escribe el código postal de la visita.';
export const VISIT_ADDRESS_LINE_REQUIRED_MESSAGE = 'Escribe a dónde es la visita: calle, número y colonia.';
export const TRAVEL_MINUTES_MESSAGE = 'El traslado entre visitas va de 0 a 240 minutos.';
export const MAX_TRAVEL_MINUTES = 240;

/** Google Maps takes the destination and up to nine stops on the way in one link. */
export const MAX_ROUTE_STOPS = 10;

export interface VisitEstimate extends OrderTotalsEstimate {
  /** What the services and products add up to, without the visit. */
  productsTotal: number;
  /** The charge for the visit, as agreed. */
  fee: number;
}

/** The totals of the sale of a visit: its lines and the charge as one more line at the business's IVA rate. */
export function estimateVisitTotals(
  lines: TotalsLine[],
  pricesIncludeTax: boolean,
  fee: number,
  defaultTaxRate: number,
): VisitEstimate {
  const productsTotal = estimateOrderTotals(lines, pricesIncludeTax).total;
  const withFee = fee > 0
    ? [...lines, { unitPrice: fee, quantity: 1, ratePercent: defaultTaxRate, isExempt: false }]
    : lines;
  return { ...estimateOrderTotals(withFee, pricesIncludeTax), productsTotal, fee };
}

/** «Cargo por visita $80.00», «… más IVA» when the business's prices do not include it, or «Visita sin cargo». */
export function describeVisitFee(fee: number, pricesIncludeTax = true): string {
  return fee > 0 ? `Cargo por visita ${formatMoney(fee)}${pricesIncludeTax ? '' : ' más IVA'}` : 'Visita sin cargo';
}

/** «A domicilio · CP 72000 · Centro», for the agenda (the whole address is in the appointment). */
export function describeVisitPlace(visit: { postalCode: string; zoneName: string }): string {
  return `A domicilio · CP ${visit.postalCode} · ${visit.zoneName}`;
}

/** An appointment that has its sale: one that was cancelled no longer counts, and the appointment is charged again. */
export function hasLiveSale(appointment: { saleOrderId?: string | null; saleCancelled?: boolean }): boolean {
  return Boolean(appointment.saleOrderId) && !appointment.saleCancelled;
}

/** An appointment that cannot be charged (the server says so too); null when it can. */
export function appointmentSaleProblem(
  appointment: { status: AppointmentStatus; saleOrderId?: string | null; saleCancelled?: boolean },
): string | null {
  if (appointment.status === AppointmentStatus.Cancelled) return CANCELLED_SALE_MESSAGE;
  if (appointment.status === AppointmentStatus.NoShow) return NO_SHOW_SALE_MESSAGE;
  if (appointment.status === AppointmentStatus.Requested) return ACCEPT_BEFORE_CHARGING_MESSAGE;
  if (hasLiveSale(appointment)) return ALREADY_SOLD_MESSAGE;
  return null;
}

interface VisitAddress {
  addressLine: string;
  postalCode: string;
}

/** «Av. Juárez 123, Col. Centro, CP 72000, México», what a map searches. */
function mapQuery(stop: VisitAddress): string {
  return `${stop.addressLine.trim()}, CP ${stop.postalCode}, México`;
}

/** A link that opens the address in Google Maps: the address leaves the device only when the person opens it. */
export function mapsPlaceUrl(stop: VisitAddress): string {
  return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(mapQuery(stop))}`;
}

/**
 * A link with the route of the visits in the order given, from where the person is: the last one is the destination and
 * the others stops on the way. Null without visits; only the first ten fit in one link.
 */
export function mapsRouteUrl(stops: readonly VisitAddress[]): string | null {
  const route = stops.slice(0, MAX_ROUTE_STOPS);
  if (route.length === 0) return null;
  const destination = route[route.length - 1];
  const waypoints = route.slice(0, -1).map(mapQuery).join('|');
  return `https://www.google.com/maps/dir/?api=1&travelmode=driving&destination=${encodeURIComponent(mapQuery(destination))}`
    + (waypoints ? `&waypoints=${encodeURIComponent(waypoints)}` : '');
}

/** A link that calls the customer: the phone as typed, only with its digits and a leading «+». */
export function telUrl(phone: string): string {
  return `tel:${phone.trim().replace(/(?!^\+)[^\d]/g, '')}`;
}
