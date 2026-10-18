// Appointments booked online (Fase 24D, ADR 0025): what the booking page, the appointment's link and Mis citas show. The
// server decides the free times, the status and whether the customer may still cancel; these only put them in words.
import { AppointmentStatus } from '../types';

/** «45 min», «1 h», «1 h 30 min»: how long a service takes. */
export function describeDuration(minutes: number): string {
  if (minutes < 60) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
}

const WEEKDAYS = ['Dom', 'Lun', 'Mar', 'Mié', 'Jue', 'Vie', 'Sáb'];

/** «Lun 5» for a day of the booking page (a date «2026-10-05» of the business's calendar). */
export function shortDayLabel(date: string): string {
  const [year, month, day] = date.split('-').map(Number);
  // The date has no time zone: read it as a calendar day, not as midnight somewhere.
  const weekday = new Date(Date.UTC(year, month - 1, day)).getUTCDay();
  return `${WEEKDAYS[weekday]} ${day}`;
}

/** The chip of an appointment's status for its customer, with the colors of the states (never the accent). */
export function customerAppointmentChip(status: AppointmentStatus): string {
  switch (status) {
    case AppointmentStatus.Requested: return 'ta-chip-new';
    case AppointmentStatus.Scheduled:
    case AppointmentStatus.Confirmed: return 'ta-chip-confirmed';
    case AppointmentStatus.InService: return 'ta-chip-preparing';
    case AppointmentStatus.Completed: return 'ta-chip-done';
    default: return 'ta-chip-cancelled';
  }
}

/** The booking page of a business: services, days and free times. */
export const bookingPath = (slug: string) => `/m/${slug}/agendar`;

/** A map search of the business's address: opens the maps app on a phone. */
export function addressMapUrl(address: string): string {
  return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
}

/** «Lunes 5 de octubre» where the server's «lunes 5 de octubre» starts a line. */
export const upperFirst = (text: string) => text.charAt(0).toLocaleUpperCase('es-MX') + text.slice(1);

/** What the customer confirms before cancelling: the time is freed and the business sees it cancelled. */
export function cancelAppointmentMessage(appointment: { serviceName: string; dateLabel: string; timeLabel: string }): string {
  return `Tu cita de ${appointment.serviceName} del ${appointment.dateLabel} a las ${appointment.timeLabel} se cancela y `
    + 'el horario queda libre para alguien más.';
}
