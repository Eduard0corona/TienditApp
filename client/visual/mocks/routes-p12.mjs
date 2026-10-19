// Phase 24D endpoints for the fake backend: the business's online booking settings (GET and PUT
// /appointments/online-booking, from `data.onlineBooking`) and the decision on a request booked online (POST
// /appointments/{id}/accept and /decline, on `data.appointments`). The booking page, the appointment's link and Mis citas
// answer in their scene file (scenes/citas-en-linea.mocks.ts). Who attends online (Fase 24J): GET and PUT
// /appointments/online-booking/staff, from `data.onlineBookingStaff` (the active team of the demo, nobody on).

// AppointmentStatus as the contract numbers it.
const SCHEDULED = 0;
const REQUESTED = 5;
const CANCELLED = 99;

/** Café La Esquina has the agenda, its menu published and its hours: online booking only waits for its owner. */
export function demoOnlineBooking(data) {
  return { enabled: false, capacity: 1, maxCapacity: 20, booksOnline: false, missing: [], slug: data.tenant.slug, staffCount: 0 };
}

/** The services Café La Esquina books online, as the owner gives them to each person. */
const BOOKABLE = ['Cata de café de especialidad', 'Taller de barismo básico', 'Calibración de molino'];

/** «Ana L.»: the name the server suggests for customers. */
const suggestedName = (first, last) => (last ? `${first} ${last[0].toUpperCase()}.` : first);

/** The active team of the demo, nobody taking online appointments yet (OnlineBookingStaffSettingsDto). */
export function demoOnlineBookingStaff(data) {
  return {
    members: data.employees
      .filter((employee) => employee.status === 'Active' && employee.isActive)
      .map((employee) => ({
        userId: employee.userId,
        fullName: `${employee.firstName} ${employee.lastName}`,
        name: suggestedName(employee.firstName, employee.lastName),
        configured: false,
        enabled: false,
        allServices: true,
        serviceIds: [],
        weeklyHours: null,
      }))
      .sort((a, b) => a.fullName.localeCompare(b.fullName, 'es')),
    services: BOOKABLE
      .map((name) => data.products.find((product) => product.name === name))
      .filter(Boolean)
      .map((product) => ({ id: product.id, name: product.name, bookableNow: true })),
    maxEnabled: 30,
  };
}

export function phase24dRoutes(data, { ok, notFound }) {
  data.onlineBooking ??= demoOnlineBooking(data);
  data.onlineBookingStaff ??= demoOnlineBookingStaff(data);
  const decide = (id, status) => {
    const appointment = data.appointments.find((candidate) => candidate.id === id);
    if (!appointment) return notFound('Cita');
    if (appointment.status !== REQUESTED) {
      return { status: 400, json: { status: 400, title: 'Bad Request', detail: 'Esta cita no está por confirmar.', code: 'business_rule' } };
    }
    appointment.status = status;
    return ok(appointment);
  };

  return [
    ['GET', /^\/appointments\/online-booking$/, () => ok(data.onlineBooking)],
    ['PUT', /^\/appointments\/online-booking$/, (m, q, body) => {
      const enabled = !!body?.enabled;
      data.onlineBooking = {
        ...data.onlineBooking,
        enabled,
        capacity: body?.capacity ?? data.onlineBooking.capacity,
        booksOnline: enabled && data.onlineBooking.missing.length === 0,
      };
      return ok(data.onlineBooking);
    }],
    ['GET', /^\/appointments\/online-booking\/staff$/, () => ok(data.onlineBookingStaff)],
    ['PUT', /^\/appointments\/online-booking\/staff\/([^/]+)$/, (m, q, body) => {
      const member = data.onlineBookingStaff.members.find((candidate) => candidate.userId === m[1]);
      if (!member) return notFound('Persona');
      Object.assign(member, {
        configured: true,
        enabled: !!body?.enabled,
        name: body?.name ?? member.name,
        allServices: body?.allServices ?? true,
        serviceIds: body?.allServices ? [] : body?.serviceIds ?? [],
        weeklyHours: body?.weeklyHours ?? null,
      });
      data.onlineBooking = { ...data.onlineBooking, staffCount: data.onlineBookingStaff.members.filter((item) => item.enabled).length };
      return ok(member);
    }],
    ['POST', /^\/appointments\/([^/]+)\/accept$/, (m) => decide(m[1], SCHEDULED)],
    ['POST', /^\/appointments\/([^/]+)\/decline$/, (m) => decide(m[1], CANCELLED)],
  ];
}
