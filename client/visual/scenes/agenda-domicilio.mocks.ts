import { asOption, type DemoData } from '../fixtures';
import { at, uuid } from '../mocks/data.mjs';

// AppointmentStatus as the contract numbers it.
const SCHEDULED = 0;
const CONFIRMED = 1;
const COMPLETED = 3;

/** A delivery zone of the business as GET /delivery-zones answers it: where it goes, and what a visit there charges. */
interface DeliveryZone {
  id: string;
  name: string;
  fee: number;
  minimumOrder: number;
  freeDeliveryFrom: number | null;
  isActive: boolean;
  revision: number;
  postalCodes: string[];
}

/** The zones of Café La Esquina (the same as Vender's and Configuración's) and Del Valle, farther away. */
const ZONES: DeliveryZone[] = [
  { id: 'zona-roma', name: 'Roma', fee: 35, minimumOrder: 150, freeDeliveryFrom: 400, isActive: true, revision: 1, postalCodes: ['06700', '06760'] },
  { id: 'zona-condesa', name: 'Condesa', fee: 45, minimumOrder: 0, freeDeliveryFrom: null, isActive: true, revision: 1, postalCodes: ['06140'] },
  { id: 'zona-del-valle', name: 'Del Valle', fee: 60, minimumOrder: 0, freeDeliveryFrom: null, isActive: true, revision: 1, postalCodes: ['03100'] },
];

/** Sofía's maintenance tomorrow at 10:00, at her address in Roma (Carlos). */
export const SOFIA_VISIT_ID = uuid('appt:a2');
/** Ricardo's descaling today at 14:30, in Condesa (Diego): confirmed and not charged yet. */
export const RICARDO_VISIT_ID = uuid('appt:visita-ricardo');

/**
 * Café La Esquina also goes to its customers' addresses (Fase 22E): its delivery zones are where it goes, and the fee of a
 * zone is the charge for the visit. Each appointment knows its team member and its service of the catalog; Sofía's and
 * the offices' are at their addresses, and today Diego, Mariana and someone not assigned yet have visits around the
 * neighborhoods: one done and charged, the others ahead.
 */
function withHomeVisits(data: DemoData) {
  data.deliveryZones = ZONES;
  const staffUserId = (name: string | null) =>
    data.employees.find((member: { firstName: string; lastName: string }) => `${member.firstName} ${member.lastName}` === name)?.userId ?? null;
  const serviceProductId = (name: string) => data.products.find((product: { name: string }) => product.name === name)?.id ?? null;
  const visit = (zoneName: string, postalCode: string, addressLine: string, references: string | null = null) => ({
    postalCode, addressLine, references, zoneName, fee: ZONES.find((zone) => zone.name === zoneName)!.fee,
  });

  for (const appointment of data.appointments) {
    Object.assign(appointment, {
      staffUserId: staffUserId(appointment.staffName),
      serviceProductId: serviceProductId(appointment.serviceName),
      visit: null,
      saleOrderId: null,
    });
  }
  const byId = (id: string) => data.appointments.find((appointment: { id: string }) => appointment.id === id);
  Object.assign(byId(SOFIA_VISIT_ID), {
    serviceProductId: serviceProductId('Mantenimiento de cafetera espresso'),
    visit: visit('Roma', '06700', 'Colima 145, depto. 3, Roma Norte', 'Portón negro, timbre 3'),
  });
  Object.assign(byId(uuid('appt:a8')), { visit: visit('Condesa', '06140', 'Ámsterdam 210, piso 3, Condesa', 'Recepción del piso 3') });

  const today = (key: string, customerName: string, customerPhone: string, serviceName: string, staffName: string | null,
    from: [number, number], to: [number, number], status: number, place: ReturnType<typeof visit>, saleOrderId: string | null = null) => ({
    id: uuid(`appt:${key}`), customerName, customerPhone, serviceName, staffName, staffUserId: staffUserId(staffName),
    serviceProductId: serviceProductId(serviceName), startAt: at(from[0], from[1]), endAt: at(to[0], to[1]), status, notes: null,
    visit: place, saleOrderId,
  });
  data.appointments.push(
    today('visita-gabriela', 'Gabriela Soto', '55 4410 2387', 'Mantenimiento de cafetera espresso', 'Diego Herrera', [10, 0], [12, 0],
      COMPLETED, visit('Roma', '06760', 'Medellín 81, Roma Sur'), uuid('venta-cita-gabriela')),
    today('visita-ricardo', 'Ricardo Peña', '55 7781 0924', 'Descalcificación de cafetera', 'Diego Herrera', [14, 30], [15, 30],
      CONFIRMED, visit('Condesa', '06140', 'Tamaulipas 66, int. 2, Condesa', 'Edificio azul, interfón 2')),
    today('visita-tlacuache', 'Café Tlacuache', '55 2290 6613', 'Calibración de molino', 'Diego Herrera', [17, 0], [17, 45],
      SCHEDULED, visit('Roma', '06700', 'Orizaba 87, Roma Norte')),
    today('visita-ortega', 'Familia Ortega', '55 3176 4405', 'Cata de café de especialidad', 'Mariana Ortiz', [19, 0], [20, 30],
      CONFIRMED, visit('Condesa', '06140', 'Ámsterdam 45, Condesa')),
    today('visita-elena', 'Elena Vargas', '55 6023 1178', 'Mantenimiento de cafetera espresso', null, [18, 30], [20, 30],
      SCHEDULED, visit('Del Valle', '03100', 'Pilares 410, Del Valle', 'Casa con zaguán verde')),
  );
  data.appointments.sort((a: { startAt: string }, b: { startAt: string }) => b.startAt.localeCompare(a.startAt));
}

export const HOME_VISITS_DATA = asOption(withHomeVisits);
