// Fase 29A endpoints for the fake backend (ADR 0053): visits to quote of a business that goes to its customers' homes
// (GET and PUT /appointments/home-visit-requests, from `data.homeVisitRequests`). Café La Esquina is not a trade: it
// has them off, and without delivery zones its visits would also miss where it goes. A scene of a trade sets the whole
// setting in its `prepareData`; dismissing the notice of its published address answers 204, as the API does.

// HomeVisitChargePolicy as the contract numbers it.
const CHARGED = 0;
const FREE = 1;

/** What the server says is missing without delivery zones (HomeVisitRequestAvailability.Describe). */
export const NO_ZONES = 'Da de alta tus zonas en Configuración › Zonas de entrega: a qué códigos postales vas y cuánto cuesta ir.';

/** The services Café La Esquina could pick as a visit: the ones it books online. */
const BOOKABLE = ['Cata de café de especialidad', 'Taller de barismo básico', 'Calibración de molino'];

/** Café La Esquina's setting: off, no visit service, charged, and its missing zones. */
export function demoHomeVisitRequests(data) {
  return {
    enabled: false,
    serviceId: null,
    chargePolicy: CHARGED,
    price: null,
    pricesIncludeTax: true,
    receivesVisits: false,
    missing: [NO_ZONES],
    services: BOOKABLE
      .map((name) => data.products.find((product) => product.name === name))
      .filter(Boolean)
      .map((product) => ({ id: product.id, name: product.name, price: product.priceAmount, durationMinutes: product.preparationTimeMinutes ?? null })),
    addressNotice: false,
    slug: data.tenant.slug,
  };
}

/** The setting as the API answers it: the visit service while it is bookable, and whether it receives visits now. */
function answer(settings) {
  const service = settings.services.find((candidate) => candidate.id === settings.serviceId) ?? null;
  return { ...settings, service, receivesVisits: settings.enabled && settings.missing.length === 0 };
}

export function phase29aRoutes(data, { ok }) {
  data.homeVisitRequests ??= demoHomeVisitRequests(data);

  return [
    ['GET', /^\/appointments\/home-visit-requests$/, () => ok(answer(data.homeVisitRequests))],
    ['PUT', /^\/appointments\/home-visit-requests$/, (m, q, body) => {
      const free = body?.chargePolicy === FREE;
      data.homeVisitRequests = {
        ...data.homeVisitRequests,
        enabled: !!body?.enabled,
        serviceId: body?.serviceId ?? null,
        chargePolicy: body?.chargePolicy ?? CHARGED,
        price: free && data.homeVisitRequests.price ? { amount: 0, isStarting: false } : data.homeVisitRequests.price,
      };
      return ok(answer(data.homeVisitRequests));
    }],
    ['POST', /^\/auth\/my-tenant\/directory\/home-visit-address-notice\/dismiss$/, () => {
      if (data.directoryProfile) data.directoryProfile.homeVisitAddressNotice = false;
      data.homeVisitRequests.addressNotice = false;
      return { status: 204, json: null };
    }],
  ];
}
