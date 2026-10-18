import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { uuid } from '../mocks/data.mjs';
import { demoDirectory } from '../mocks/routes-p9.mjs';
import { publicRoutes } from './publico-cuenta-marco.mocks';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

// AppointmentStatus and AppointmentChannel as the contract numbers them (Fase 24D).
const SCHEDULED = 0;
const COMPLETED = 3;
const REQUESTED = 5;
const CANCELLED = 99;
const ONLINE = 1;

/** The link of an appointment booked online: 43 characters, as the API gives it. */
export const APPOINTMENT_LINK = 'Zc8Lm2Qv7RtW4yXk9PbN1sDf5GhJ3aEu6Ki0Oo-_CtA';

const DAYS = ['domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado'];
const MONTHS = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];
// The scenes' now: Sunday 2026-10-04 13:25 in Mexico City (UTC-6 all year).
const NOW_MINUTES = 13 * 60 + 25;
const TODAY = '2026-10-04';

/** «lunes 5 de octubre», as MessageValues.FormatDate writes it. */
function dateLabel(date: string): string {
  const [year, month, day] = date.split('-').map(Number);
  const weekday = new Date(Date.UTC(year, month - 1, day)).getUTCDay();
  return `${DAYS[weekday]} ${day} de ${MONTHS[month - 1]}`;
}

/** «4:30 p. m.», as MessageValues.FormatTime writes it. */
function timeLabel(minutes: number): string {
  const hour = Math.floor(minutes / 60);
  return `${hour % 12 === 0 ? 12 : hour % 12}:${String(minutes % 60).padStart(2, '0')} ${hour < 12 ? 'a. m.' : 'p. m.'}`;
}

const pad = (n: number) => String(n).padStart(2, '0');
/** The instant of a time of the business's day, with its offset. */
const instant = (date: string, minutes: number) => `${date}T${pad(Math.floor(minutes / 60))}:${pad(minutes % 60)}:00-06:00`;
const utc = (date: string, minutes: number) => new Date(instant(date, minutes)).toISOString();
const addDays = (date: string, days: number) => {
  const [year, month, day] = date.split('-').map(Number);
  return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
};

/** The hours of Café La Esquina by weekday (Sunday 9 to 15, weekdays 8 to 20, Saturday with a break). */
const OPENINGS: Record<number, [number, number][]> = {
  0: [[9 * 60, 15 * 60]],
  1: [[8 * 60, 20 * 60]], 2: [[8 * 60, 20 * 60]], 3: [[8 * 60, 20 * 60]], 4: [[8 * 60, 20 * 60]], 5: [[8 * 60, 20 * 60]],
  6: [[8 * 60, 14 * 60], [16 * 60, 21 * 60]],
};

/** The appointments already taken that a time must not overlap: the cata of Tuesday and the taller of Saturday. */
const TAKEN: Record<string, [number, number][]> = {
  '2026-10-05': [[10 * 60, 11 * 60 + 30]],
  '2026-10-06': [[17 * 60, 18 * 60 + 30]],
  '2026-10-10': [[10 * 60, 13 * 60]],
};

interface BookingService {
  id: string;
  name: string;
  description: string | null;
  price: number;
  durationMinutes: number;
  imageUrl: null;
}

/** The services Café La Esquina books online (PublicBookingServiceDto): its catas, talleres and the molino. */
function services(data: DemoData): BookingService[] {
  const service = (name: string, durationMinutes: number) => {
    const product = data.products.find((candidate: { name: string }) => candidate.name === name);
    return { id: product.id, name, description: product.description ?? null, price: product.priceAmount, durationMinutes, imageUrl: null };
  };
  return [service('Cata de café de especialidad', 90), service('Taller de barismo básico', 180), service('Calibración de molino', 45)];
}

/**
 * A week of free times of a service (PublicBookingSlotsDto) as BookingSlots.FreeStarts gives them: every half hour from
 * each opening, an hour from now at the earliest, within the opening and not over the appointments taken.
 */
function week(data: DemoData, serviceId: string, from: string | null) {
  const service = services(data).find((candidate) => candidate.id === serviceId);
  if (!service) return null;
  const first = from && from > TODAY ? from : TODAY;
  const days = Array.from({ length: 7 }, (_, index) => {
    const date = addDays(first, index);
    const weekday = new Date(`${date}T12:00:00Z`).getUTCDay();
    const slots = (OPENINGS[weekday] ?? []).flatMap(([opens, closes]) => {
      const starts: number[] = [];
      for (let start = opens; start + service.durationMinutes <= closes; start += 30) starts.push(start);
      return starts;
    }).filter((start) => (date !== TODAY || start >= NOW_MINUTES + 60)
      && !(TAKEN[date] ?? []).some(([takenFrom, takenTo]) => start < takenTo && takenFrom < start + service.durationMinutes));
    return {
      date,
      label: dateLabel(date),
      // Café La Esquina closes before midnight: every time starts on its day.
      slots: slots.map((start) => ({ startAt: instant(date, start), label: timeLabel(start), date, dateLabel: dateLabel(date) })),
    };
  });
  return { days, nextFrom: addDays(first, 7) };
}

/** The appointments of María Fernanda's account and of the guest's link (CustomerAppointmentDto). */
function customerAppointment(id: string, name: string, date: string, minutes: number, duration: number, status: number, statusLabel: string, canCancel: boolean) {
  return {
    id, serviceName: name, startAt: instant(date, minutes), endAt: instant(date, minutes + duration), dateLabel: dateLabel(date),
    timeLabel: timeLabel(minutes), status, statusLabel, canCancel, businessName: 'Café La Esquina', businessSlug: 'cafe-la-esquina',
    businessContactPhone: '5512345678', address: 'Av. Álvaro Obregón 120, Roma Norte, 06700 Ciudad de México, CDMX',
  };
}

function myAppointments(scope: string | null) {
  return scope === '1'
    ? [
      customerAppointment('mc-3', 'Calibración de molino', '2026-09-28', 11 * 60, 45, COMPLETED, 'Terminada', false),
      customerAppointment('mc-4', 'Cata de café de especialidad', '2026-09-19', 17 * 60, 90, CANCELLED, 'La cancelaste', false),
    ]
    : [
      customerAppointment('mc-1', 'Cata de café de especialidad', '2026-10-06', 17 * 60, 90, REQUESTED, 'Por confirmar', true),
      customerAppointment('mc-2', 'Taller de barismo básico', '2026-10-10', 10 * 60, 180, SCHEDULED, 'Confirmada', true),
    ];
}

/** An error as the API answers it (ApiProblem). */
function problem(status: number, detail: string) {
  return { status, json: { status, title: status === 404 ? 'Not Found' : 'Bad Request', detail, code: status === 404 ? 'not_found' : 'business_rule', error: detail, traceId: '00-visual-00' } };
}

const bookingRoutes: ExtraRoutes = (data, helpers) => [
  ['GET', /^\/public\/menu\/([^/]+)\/booking$/, (match) => (decodeURIComponent(match[1]) === data.tenant.slug
    ? helpers.ok({
      businessName: data.tenant.name, slug: data.tenant.slug, logoUrl: null, contactPhone: data.tenant.contactPhone,
      theme: data.tenant.theme, address: 'Av. Álvaro Obregón 120, Roma Norte, 06700 Ciudad de México, CDMX',
      pricesIncludeTax: true, daysAhead: 30, services: services(data),
    })
    : problem(404, 'Este negocio no agenda citas en línea.'))],
  ['GET', /^\/public\/menu\/([^/]+)\/booking\/slots$/, (_match, query) => {
    const slots = week(data, query.get('serviceId') ?? '', query.get('from'));
    return slots ? helpers.ok(slots) : problem(404, 'Ese servicio no se agenda en línea.');
  }],
  ['POST', /^\/public\/menu\/([^/]+)\/appointments$/, (_match, _query, body: { serviceProductId?: string; startAt?: string }) => {
    const service = services(data).find((candidate) => candidate.id === body?.serviceProductId);
    const [date, time] = (body?.startAt ?? '').split('T');
    const minutes = Number(time?.slice(0, 2)) * 60 + Number(time?.slice(3, 5));
    return helpers.ok({
      serviceName: service?.name ?? '', startAt: body?.startAt, endAt: instant(date, minutes + (service?.durationMinutes ?? 60)),
      dateLabel: dateLabel(date), timeLabel: timeLabel(minutes), businessName: data.tenant.name, contactPhone: '5512345678',
      address: 'Av. Álvaro Obregón 120, Roma Norte, 06700 Ciudad de México, CDMX',
      linkedToAccount: data.profile.role === 'Customer', trackingToken: APPOINTMENT_LINK,
    });
  }],
  ['POST', /^\/public\/appointments\/tracking$/, (_match, _query, body: { token?: string }) => (body?.token === APPOINTMENT_LINK
    ? helpers.ok(customerAppointment('mc-1', 'Cata de café de especialidad', '2026-10-06', 17 * 60, 90, REQUESTED, 'Por confirmar', true))
    : problem(404, 'No encontramos esa cita. El enlace pudo haber vencido.'))],
  ['POST', /^\/public\/appointments\/tracking\/cancel$/, () => helpers.ok(
    customerAppointment('mc-1', 'Cata de café de especialidad', '2026-10-06', 17 * 60, 90, CANCELLED, 'La cancelaste', false))],
  ['GET', /^\/me\/appointments$/, (_match, query) => {
    const items = myAppointments(query.get('scope'));
    return helpers.ok({ items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false });
  }],
  ['POST', /^\/me\/appointments\/([^/]+)\/cancel$/, (match) => helpers.ok({
    ...myAppointments('0').find((appointment) => appointment.id === match[1]), status: CANCELLED, statusLabel: 'La cancelaste', canCancel: false,
  })],
  ...publicRoutes(data, helpers),
];

/** The menu of Café La Esquina leads to its booking page. */
function menuBooks(data: DemoData) {
  data.menuBooksOnline = true;
}

/** Estética Bella, in Explorar, books its appointments online. */
function directoryBooks(data: DemoData) {
  data.directory = demoDirectory().map((business: { slug: string }) => (business.slug === 'estetica-bella'
    ? { ...business, booksOnline: true }
    : business));
}

/**
 * Two customers booked online and wait for the business to accept them (Fase 24D): María Fernanda's cata on Tuesday and
 * Jorge's taller on Saturday; and Rodrigo cancelled his cata of Thursday from his link.
 */
function withRequests(data: DemoData) {
  const serviceProductId = (name: string) => data.products.find((product: { name: string }) => product.name === name)?.id ?? null;
  const request = (key: string, customerName: string, customerPhone: string, serviceName: string, date: string, minutes: number, duration: number, status = REQUESTED) => ({
    id: uuid(`appt:${key}`), customerName, customerPhone, serviceName, serviceProductId: serviceProductId(serviceName), staffName: null,
    // In UTC like the rest of the demo agenda, which the list compares and sorts as text.
    staffUserId: null, startAt: utc(date, minutes), endAt: utc(date, minutes + duration), status,
    notes: null, visit: null, saleOrderId: null, saleCancelled: false, channel: ONLINE, cancelledByCustomer: status === CANCELLED,
  });
  data.appointments.push(
    request('en-linea-mafer', 'María Fernanda López', '55 2210 7765', 'Cata de café de especialidad', '2026-10-06', 17 * 60, 90),
    request('en-linea-jorge', 'Jorge Ibarra', '55 4431 0098', 'Taller de barismo básico', '2026-10-10', 10 * 60, 180),
    request('en-linea-rodrigo', 'Rodrigo Salas', '55 7781 2045', 'Cata de café de especialidad', '2026-10-08', 18 * 60, 90, CANCELLED),
  );
  data.appointments.sort((a: { startAt: string }, b: { startAt: string }) => b.startAt.localeCompare(a.startAt));
}

/** Online booking is on and customers book: the owner's card links the booking page. */
function bookingOn(data: DemoData) {
  data.onlineBooking = { enabled: true, capacity: 2, maxCapacity: 20, booksOnline: true, missing: [], slug: data.tenant.slug };
}

export const CITAS_ROUTES = asOption(bookingRoutes);
export const MENU_AGENDA = asOption(menuBooks);
export const EXPLORAR_AGENDA = asOption(directoryBooks);
export const AGENDA_SOLICITUDES = asOption(withRequests);
export const CITAS_PRENDIDAS = asOption(bookingOn);
