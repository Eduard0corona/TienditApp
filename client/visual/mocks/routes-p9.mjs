// Phase 24A endpoints for the fake backend: Explorar, the directory of businesses (GET /public/directory, with the
// customer's postal code since the 24B), and how the demo business shows there (GET and PUT /auth/my-tenant/directory).
// The directory is `data.directory`, in the order the API answers it (taking orders first, then open, then closed); a
// scene file changes it in its `prepareData`.

// BusinessCategory, BusinessTheme and DirectoryVisibility as the contract numbers them.
const FOOD = 0;
const GROCERIES = 1;
const HARDWARE = 5;
const STATIONERY = 6;
const BEAUTY = 7;
const NOPAL = 0;
const TALAVERA = 1;
const SHOWN = 0;
const MENU_OFF = 2;
const CATEGORIES = 11;

/** A card of Explorar: only what the menu of the business already shows. */
const card = (name, slug, category, overrides = {}) => ({
  name, slug, logoUrl: null, category, description: null, theme: NOPAL, city: 'Ciudad de México', deliveryOnly: false,
  deliveryAvailable: false, acceptsOrders: false, ordersPaused: false, statusLabel: null, statusDetail: null, closed: false,
  pricesIncludeTax: true,
  ...overrides,
});

/**
 * Where each business that delivers reaches, by its slug (Fase 24B): Café La Esquina takes orders to Roma and Condesa.
 * Explorar answers with them when the customer gives a postal code, never with the name of a zone.
 */
export function demoDeliveryZones() {
  return {
    'cafe-la-esquina': {
      '06700': { fee: 35, minimumOrder: 150, freeDeliveryFrom: 400 },
      '06140': { fee: 45, minimumOrder: 0, freeDeliveryFrom: null },
    },
  };
}

/** The businesses of the demo city, as Explorar lists them on Sunday at 13:25. */
export function demoDirectory() {
  return [
    card('Café La Esquina', 'cafe-la-esquina', FOOD, {
      logoUrl: '/visual-img/logo.svg', description: 'Café de especialidad, pan dulce y chilaquiles desde 2021.',
      acceptsOrders: true, deliveryAvailable: true, statusLabel: 'Abierto', statusDetail: 'cierra a las 15:00',
    }),
    card('Taquería El Güero', 'taqueria-el-guero', FOOD, {
      description: 'Tacos al pastor y de suadero. Gringas y quesadillas.', theme: TALAVERA, acceptsOrders: true,
    }),
    card('Abarrotes Doña Mary', 'abarrotes-dona-mary', GROCERIES, {
      description: 'Abarrotes, tortillas y refrescos fríos.', ordersPaused: true, statusLabel: 'Abierto', statusDetail: 'cierra a las 21:00',
    }),
    card('Cocina de Lupita', 'cocina-de-lupita', FOOD, {
      description: 'Comida corrida casera de lunes a sábado.', city: null, deliveryOnly: true, deliveryAvailable: false,
    }),
    card('Ferretería Don Beto', 'ferreteria-don-beto', HARDWARE, { description: 'Tornillería, pinturas y plomería.' }),
    card('Papelería La Pluma', 'papeleria-la-pluma', STATIONERY, {
      ordersPaused: true, closed: true, statusLabel: 'Cerrado', statusDetail: 'abre mañana a las 9:00',
    }),
    card('Estética Bella', 'estetica-bella', BEAUTY, {
      description: 'Cortes, tintes y uñas.', closed: true, statusLabel: 'Cerrado', statusDetail: 'abre mañana a las 10:00',
    }),
  ];
}

/** Lowercase and without accents, as the API compares a search. */
const comparable = (text) => (text ?? '').normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();

export function phase24aRoutes(data, { ok, paginate }) {
  /** Each card says whether its business delivers to the postal code, as the API does for a business that delivers. */
  const withDeliveryTo = (businesses, postalCode) => (postalCode
    ? businesses.map((business) => {
      if (!business.deliveryAvailable) return business;
      const zone = data.directoryZones[business.slug]?.[postalCode];
      return {
        ...business, deliversToPostalCode: !!zone, deliveryFee: zone?.fee ?? null,
        minimumOrder: zone?.minimumOrder ?? null, freeDeliveryFrom: zone?.freeDeliveryFrom ?? null,
      };
    })
    : businesses);
  data.directory ??= demoDirectory();
  data.directoryZones ??= demoDeliveryZones();
  data.directoryProfile ??= {
    category: FOOD, suggestedCategory: FOOD, description: 'Café de especialidad, pan dulce y chilaquiles desde 2021.', listed: true,
  };
  const visibility = () => (!data.tenant.onlineMenuMode ? MENU_OFF : data.directoryProfile.visibility ?? SHOWN);

  return [
    ['GET', /^\/public\/directory$/, (m, q) => {
      const category = q.get('category');
      const words = comparable(q.get('q')).split(/\s+/).filter(Boolean);
      const found = data.directory
        .filter((business) => category === null || String(business.category) === category)
        .filter((business) => words.every((word) => comparable(`${business.name} ${business.description ?? ''}`).includes(word)));
      return ok({
        categories: Array.from({ length: CATEGORIES }, (_, value) => ({
          category: value,
          count: data.directory.filter((business) => business.category === value).length,
        })),
        businesses: paginate(withDeliveryTo(found, q.get('postalCode')), q, 24),
        postalCode: q.get('postalCode') || null,
      });
    }],
    ['GET', /^\/auth\/my-tenant\/directory$/, () => ok({ ...data.directoryProfile, visibility: visibility() })],
    ['PUT', /^\/auth\/my-tenant\/directory$/, (m, q, body) => {
      data.directoryProfile = {
        ...data.directoryProfile, category: body.category, description: body.description?.trim() || null, listed: body.listed,
      };
      return ok({ ...data.directoryProfile, visibility: body.listed ? visibility() : 1 });
    }],
  ];
}
