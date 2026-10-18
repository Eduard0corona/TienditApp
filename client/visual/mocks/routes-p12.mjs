// Phase 24D endpoints for the fake backend: the business's online booking settings (GET and PUT
// /appointments/online-booking, from `data.onlineBooking`) and the decision on a request booked online (POST
// /appointments/{id}/accept and /decline, on `data.appointments`). The booking page, the appointment's link and Mis citas
// answer in their scene file (scenes/citas-en-linea.mocks.ts).

// AppointmentStatus as the contract numbers it.
const SCHEDULED = 0;
const REQUESTED = 5;
const CANCELLED = 99;

/** Café La Esquina has the agenda, its menu published and its hours: online booking only waits for its owner. */
export function demoOnlineBooking(data) {
  return { enabled: false, capacity: 1, maxCapacity: 20, booksOnline: false, missing: [], slug: data.tenant.slug };
}

export function phase24dRoutes(data, { ok, notFound }) {
  data.onlineBooking ??= demoOnlineBooking(data);
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
    ['POST', /^\/appointments\/([^/]+)\/accept$/, (m) => decide(m[1], SCHEDULED)],
    ['POST', /^\/appointments\/([^/]+)\/decline$/, (m) => decide(m[1], CANCELLED)],
  ];
}
