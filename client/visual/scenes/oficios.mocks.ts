import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { demoDirectory } from '../mocks/routes-p9.mjs';

// BusinessCategory, BusinessTheme, OnlineMenuMode and HomeVisitChargePolicy as the contract numbers them.
const HOME_REPAIRS = 11;
const MENU_ONLY = 1;
const TALAVERA = 1;
const CHARGED = 0;
const FREE = 1;
const CREDITED_IF_HIRED = 2;

const card = (name: string, slug: string, overrides: Record<string, unknown>) => ({
  name, slug, logoUrl: null, coverImageUrl: null, category: HOME_REPAIRS, description: null, theme: 0, city: 'Ciudad de México',
  deliveryOnly: false, deliveryAvailable: false, acceptsOrders: false, ordersPaused: false, statusLabel: null,
  statusDetail: null, closed: false, pricesIncludeTax: true, booksOnline: false, receivesVisits: true,
  ...overrides,
});

/**
 * Three trades of the demo city receive visits to quote (Fase 29A): a plumber who only goes to homes and takes the visit
 * off the quote, a carpenter with a workshop who charges it, and a locksmith who visits for free, closed now. With the
 * postal code 06700 (Roma Norte) the plumber and the carpenter go there; the locksmith does not.
 */
function tradesInExplore(data: DemoData) {
  data.directory ??= demoDirectory();
  data.directory = [
    card('Plomería Hernández', 'plomeria-hernandez', {
      description: 'Fugas, drenajes, tinacos y calentadores.', city: null, deliveryOnly: true, theme: TALAVERA,
      visitChargePolicy: CREDITED_IF_HIRED, visitPrice: { amount: 350, isStarting: true },
      statusLabel: 'Abierto', statusDetail: 'cierra a las 19:00',
    }),
    card('Carpintería Don José', 'carpinteria-don-jose', {
      description: 'Muebles a la medida, puertas y closets.', pricesIncludeTax: false,
      visitChargePolicy: CHARGED, visitPrice: { amount: 250, isStarting: false },
      statusLabel: 'Abierto', statusDetail: 'cierra a las 18:00',
    }),
    card('Cerrajería Pérez', 'cerrajeria-perez', {
      description: 'Aperturas, cambio de chapas y duplicados.', city: null, deliveryOnly: true,
      visitChargePolicy: FREE, visitPrice: { amount: 0, isStarting: false },
      closed: true, statusLabel: 'Cerrado', statusDetail: 'abre mañana a las 8:00',
    }),
    ...data.directory,
  ];
  data.directoryVisits = {
    'plomeria-hernandez': { '06700': { amount: 400, isStarting: false } },
    'carpinteria-don-jose': { '06700': { amount: 250, isStarting: false } },
  };
}

/** The services of the plumber: its visit and the work it quotes, with IVA on top. */
const PLUMBER_SERVICES = [
  { id: 'svc-visita', name: 'Visita y diagnóstico', price: 200, durationMinutes: 60 },
  { id: 'svc-calentador', name: 'Instalar calentador', price: 900, durationMinutes: 120 },
  { id: 'svc-drenaje', name: 'Destapar drenaje', price: 600, durationMinutes: 90 },
  { id: 'svc-fuga', name: 'Reparar fuga de agua', price: 450, durationMinutes: 60 },
];

/**
 * The owner of a plumbing business in Configuración: it receives visits, its visit service is «Visita y diagnóstico»,
 * taken off the quote when the customer hires the work, from $350 depending on the zone. Its store still publishes the
 * address of its main branch with premises: «Cómo te ven» tells it so.
 */
function plumberSettings(data: DemoData) {
  data.tenant.name = 'Plomería Hernández';
  data.tenant.industry = 'Plomería';
  // Receiving visits needs the menu published (HomeVisitRequestAvailability): without products, it is still in Explorar.
  data.tenant.onlineMenuMode = MENU_ONLY;
  data.homeVisitRequests = {
    enabled: true,
    serviceId: 'svc-visita',
    chargePolicy: CREDITED_IF_HIRED,
    price: { amount: 350, isStarting: true },
    pricesIncludeTax: false,
    receivesVisits: true,
    missing: [],
    services: PLUMBER_SERVICES,
    addressNotice: true,
    slug: data.tenant.slug,
  };
  data.directoryProfile = {
    category: HOME_REPAIRS, suggestedCategory: HOME_REPAIRS, description: 'Fugas, drenajes, tinacos y calentadores.', listed: true,
    homeVisitAddressNotice: true,
  };
}

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

/** The store of the plumber: its visits instead of a menu, its services as reference prices with IVA on top. */
const plumberStore: ExtraRoutes = (_data, { ok }) => [
  ['GET', /^\/public\/menu\/plomeria-hernandez$/, () => ok({
    businessName: 'Plomería Hernández',
    slug: 'plomeria-hernandez',
    logoUrl: null,
    description: 'Fugas, drenajes, tinacos y calentadores.',
    category: HOME_REPAIRS,
    contactPhone: '5512345678',
    currency: 'MXN',
    pricesIncludeTax: false,
    defaultTaxRate: 16,
    acceptsOrders: false,
    ordersPaused: false,
    deliveryOnly: true,
    theme: TALAVERA,
    categories: [],
    homeVisits: {
      chargePolicy: CREDITED_IF_HIRED,
      price: { amount: 350, isStarting: true },
      visitServiceName: 'Visita y diagnóstico',
      postalCodes: ['06100', '06140', '06170', '06600', '06700', '06720', '06760', '06780', '06800', '06840', '06850',
        '06860', '06870', '06880', '06890', '06900', '06920', '06950', '06970', '03100'],
      postalCodeCount: 20,
      services: PLUMBER_SERVICES.slice(1).map(({ name, price, durationMinutes }) => ({ name, price, durationMinutes })),
    },
  })],
];

// The request page's week (Fase 29B), from the scenes' Sunday 4 October: free times with the trip of each visit already
// left free, as the server gives them (PublicBookingSlotsDto). Mexico City is UTC-6 all year.
const WEEK_DAYS = ['domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado'];
const WEEK_FREE: Record<number, string[]> = {
  5: ['09:00', '09:30', '10:00', '12:30', '13:00', '16:00', '16:30', '17:00'],
  6: ['09:00', '11:30', '12:00', '15:00'],
  7: ['10:00', '10:30', '11:00', '17:00'],
  8: ['09:00', '09:30', '13:00'],
  9: ['12:00', '12:30', '16:00', '16:30', '17:00'],
  10: ['09:00', '10:00'],
};
const timeLabel = (time: string) => {
  const [hour, minute] = time.split(':').map(Number);
  return `${hour % 12 === 0 ? 12 : hour % 12}:${String(minute).padStart(2, '0')} ${hour < 12 ? 'a. m.' : 'p. m.'}`;
};
const visitWeek = () => ({
  days: Array.from({ length: 7 }, (_, index) => {
    const day = 4 + index;
    const date = `2026-10-${String(day).padStart(2, '0')}`;
    const label = `${WEEK_DAYS[index]} ${day} de octubre`;
    return {
      date,
      label,
      slots: (WEEK_FREE[day] ?? []).map((time) => ({ startAt: `${date}T${time}:00-06:00`, label: timeLabel(time), date, dateLabel: label })),
    };
  }),
  nextFrom: '2026-10-11',
});

/** The postal codes the plumber goes to: Roma, Condesa and around, the visit $400 more IVA in all of them. */
const PLUMBER_POSTAL_CODES = ['06100', '06140', '06170', '06600', '06700', '06720', '06760'];

/** The link of the visit requested on the page (Fase 29B): 43 characters, as the API gives it. */
export const VISIT_LINK = 'Vq7Lm2Qv7RtW4yXk9PbN1sDf5GhJ3aEu6Ki0Oo-_PlM';

/**
 * The request page of the plumber's visits (Fase 29B): «Visita y diagnóstico» of an hour, from $350 more IVA, taken off
 * the quote when the customer hires the work; whoever is free goes. It goes to Roma Norte (06700), not to 72000.
 */
const plumberVisit: ExtraRoutes = (data, { ok }) => [
  ['GET', /^\/public\/menu\/plomeria-hernandez\/home-visit$/, () => ok({
    businessName: 'Plomería Hernández',
    slug: 'plomeria-hernandez',
    logoUrl: null,
    contactPhone: '5512345678',
    theme: TALAVERA,
    pricesIncludeTax: false,
    daysAhead: 30,
    visitName: 'Visita y diagnóstico',
    durationMinutes: 60,
    chargePolicy: CREDITED_IF_HIRED,
    price: { amount: 350, isStarting: true },
    staff: [],
  })],
  ['GET', /^\/public\/menu\/plomeria-hernandez\/home-visit\/coverage$/, (_match, query) => {
    const postalCode = query.get('postalCode') ?? '';
    const covered = PLUMBER_POSTAL_CODES.includes(postalCode);
    return ok({ postalCode, covered, price: covered ? 400 : null });
  }],
  ['GET', /^\/public\/menu\/plomeria-hernandez\/home-visit\/slots$/, () => ok(visitWeek())],
  ['POST', /^\/public\/menu\/plomeria-hernandez\/home-visits$/, (_match, _query, body: { startAt?: string; postalCode?: string }) => ok({
    serviceName: 'Visita y diagnóstico',
    startAt: body?.startAt,
    endAt: body?.startAt,
    dateLabel: 'lunes 5 de octubre',
    timeLabel: '10:00 a. m.',
    businessName: 'Plomería Hernández',
    contactPhone: '5512345678',
    address: null,
    visit: { postalCode: body?.postalCode ?? '', chargePolicy: CREDITED_IF_HIRED },
    staffName: null,
    linkedToAccount: data.profile.role === 'Customer',
    trackingToken: VISIT_LINK,
  })],
];

/**
 * The visit as its link shows it (Fase 29H): «Por confirmar» at 06700, with the photos of what happens the customer sent
 * (`sent`, counted again with each one that arrives) and whether it takes more.
 */
function visitOfTheLink(sent: number) {
  return {
    id: 'visita-plomeria',
    serviceName: 'Visita y diagnóstico',
    startAt: '2026-10-05T16:00:00Z',
    endAt: '2026-10-05T17:00:00Z',
    dateLabel: 'lunes 5 de octubre',
    timeLabel: '10:00 a. m.',
    status: 5,
    statusLabel: 'Por confirmar',
    canCancel: true,
    businessName: 'Plomería Hernández',
    businessSlug: 'plomeria-hernandez',
    businessContactPhone: '5512345678',
    address: null,
    visit: { postalCode: '06700', chargePolicy: CREDITED_IF_HIRED },
    staffName: null,
    problemPhotos: sent,
    canAddPhotos: sent < 5,
  };
}

/** The visit's link with its photos: each one sent takes the next place, up to five (Fase 29H). */
const visitPhotos = (sentBefore: number): ExtraRoutes => (data, helpers) => {
  let sent = sentBefore;
  return [
    ...plumberVisit(data, helpers),
    ['POST', /^\/public\/appointments\/tracking$/, () => helpers.ok(visitOfTheLink(sent))],
    ['POST', /^\/public\/appointments\/tracking\/photos$/, () => helpers.ok(visitOfTheLink(++sent))],
  ];
};

export const OFICIOS_EN_EXPLORAR = asOption(tradesInExplore);
export const PLOMERO_EN_CONFIGURACION = asOption(plumberSettings);
export const TIENDA_DE_PLOMERIA = asOption(plumberStore);
export const VISITA_DE_PLOMERIA = asOption(plumberVisit);
/** The request with photos of what happens, none sent yet (Fase 29H). */
export const VISITA_CON_FOTOS = asOption(visitPhotos(0));
/** The link of a visit that already has two photos and takes three more (Fase 29H). */
export const ENLACE_CON_FOTOS = asOption(visitPhotos(2));
