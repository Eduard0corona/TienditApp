// Phase 22E endpoints for the fake backend: appointments at the customer's address. The visit charges what the delivery
// zone that reaches its postal code charges: the zones are `data.deliveryZones` (none in the demo, as GET /delivery-zones
// answers), and a scene file with zones sets them in its `prepareData`.

const CDMX_OFFSET_MS = 6 * 3600_000;
const POSTAL_CODE = /^\d{5}$/;

/** The day of an instant in Mexico City (yyyy-MM-dd). */
const businessDate = (iso) => new Date(new Date(iso).getTime() - CDMX_OFFSET_MS).toISOString().slice(0, 10);

/** A business rule the API refuses, as its ProblemDetails says it (`error` repeats `detail` for older readers). */
const refused = (detail, code = 'business_rule') => ({ status: 400, json: { title: 'Solicitud inválida', status: 400, code, detail, error: detail } });

// HomeVisitRules and HomeVisitPlacement (Domain and Application), word for word.
const NO_ZONES = 'Para citas a domicilio, primero da de alta tus zonas en Configuración › Zonas de entrega.';
const POSTAL_CODE_MESSAGE = 'El código postal lleva 5 dígitos.';
const notCovered = (postalCode) => `Por ahora no hacemos visitas en el ${postalCode}.`;

/** The visits of a day by team member as GET /appointments/route answers: by time, unassigned last. */
function visitRoute(data, date, staffUserId) {
  const visits = data.appointments
    .filter((appointment) => appointment.visit && appointment.status !== 99 && appointment.status !== 4)
    .filter((appointment) => businessDate(appointment.startAt) === date)
    .filter((appointment) => !staffUserId || appointment.staffUserId === staffUserId)
    .sort((a, b) => a.startAt.localeCompare(b.startAt));
  const groups = new Map();
  for (const appointment of visits) {
    const key = appointment.staffUserId ?? (appointment.staffName ? `name:${appointment.staffName}` : '');
    const group = groups.get(key) ?? { staffUserId: appointment.staffUserId ?? null, staffName: appointment.staffName ?? null, stops: [], feeTotal: 0 };
    group.stops.push({
      appointmentId: appointment.id, customerName: appointment.customerName, customerPhone: appointment.customerPhone,
      serviceName: appointment.serviceName, startAt: appointment.startAt, endAt: appointment.endAt, status: appointment.status,
      postalCode: appointment.visit.postalCode, addressLine: appointment.visit.addressLine, references: appointment.visit.references ?? null,
      zoneName: appointment.visit.zoneName, fee: appointment.visit.fee, saleOrderId: appointment.saleOrderId ?? null,
    });
    group.feeTotal = Math.round((group.feeTotal + appointment.visit.fee) * 100) / 100;
    groups.set(key, group);
  }
  const staff = [...groups.values()].sort((a, b) => (a.staffName === null) - (b.staffName === null)
    || (a.staffName ?? '').localeCompare(b.staffName ?? '', 'es', { sensitivity: 'base' }));
  return { date, staff };
}

export function phase22eRoutes(data, { ok, notFound }) {
  return [
    // The travel time between a visit and another appointment of the same person (Configuración › Zonas de entrega).
    ['GET', /^\/delivery-zones\/home-visits$/, () => ok({ travelMinutes: data.homeVisitTravelMinutes ?? 30 })],
    ['PUT', /^\/delivery-zones\/home-visits$/, (m, q, body) => {
      if (!Number.isInteger(body?.travelMinutes) || body.travelMinutes < 0 || body.travelMinutes > 240) {
        return refused('El traslado entre visitas va de 0 a 240 minutos.', 'validation_failed');
      }
      data.homeVisitTravelMinutes = body.travelMinutes;
      return ok({ travelMinutes: body.travelMinutes });
    }],
    ['GET', /^\/appointments\/visit-quote$/, (m, q) => {
      const postalCode = (q.get('postalCode') ?? '').trim();
      if (!POSTAL_CODE.test(postalCode)) return refused(POSTAL_CODE_MESSAGE, 'validation_failed');
      const zones = (data.deliveryZones ?? []).filter((zone) => zone.isActive);
      if (zones.length === 0) return refused(NO_ZONES);
      const zone = zones.find((candidate) => candidate.postalCodes.includes(postalCode));
      return zone ? ok({ postalCode, zoneName: zone.name, fee: zone.fee }) : refused(notCovered(postalCode));
    }],
    ['GET', /^\/appointments\/route$/, (m, q) => ok(visitRoute(data, q.get('date') ?? '', q.get('staffUserId') || null))],
    ['GET', /^\/appointments\/([^/]+)$/, (m) => {
      const appointment = data.appointments.find((candidate) => candidate.id === m[1]);
      return appointment ? ok(appointment) : notFound('Cita');
    }],
  ];
}
