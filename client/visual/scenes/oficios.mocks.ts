import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { demoDirectory } from '../mocks/routes-p9.mjs';

// BusinessCategory, BusinessTheme and HomeVisitChargePolicy as the contract numbers them.
const HOME_REPAIRS = 11;
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

export const OFICIOS_EN_EXPLORAR = asOption(tradesInExplore);
export const PLOMERO_EN_CONFIGURACION = asOption(plumberSettings);
export const TIENDA_DE_PLOMERIA = asOption(plumberStore);
