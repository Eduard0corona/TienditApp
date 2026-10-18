// Phase 24A endpoints for the fake backend: Explorar, the directory of businesses (GET /public/directory, with the
// customer's postal code since the 24B and the products a search finds since the 24F), and how the demo business shows
// there (GET and PUT /auth/my-tenant/directory). The directory is `data.directory`, in the order the API answers it
// (taking orders first, then open, then closed), and its products `data.directoryProducts`; a scene file changes them in
// its `prepareData`.

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
  name, slug, logoUrl: null, coverImageUrl: null, category, description: null, theme: NOPAL, city: 'Ciudad de México',
  deliveryOnly: false, deliveryAvailable: false, acceptsOrders: false, ordersPaused: false, statusLabel: null,
  statusDetail: null, closed: false, pricesIncludeTax: true, booksOnline: false,
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
      logoUrl: '/visual-img/logo.svg', coverImageUrl: '/visual-img/portada.svg',
      description: 'Café de especialidad, pan dulce y chilaquiles desde 2021.',
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

/** A product a search finds (Fase 24F), by the slug of its business: what its menu shows. */
const product = (id, slug, name, price, overrides = {}) => ({
  id, slug, name, description: null, price, compareAtPrice: null, imageUrl: null, ...overrides,
});

/** The products of the menus of Explorar: those of Café La Esquina with their photos, the others without. */
export function demoDirectoryProducts() {
  return [
    product('pd-chilaquiles', 'cafe-la-esquina', 'Chilaquiles verdes', 95, {
      description: 'Con pollo, crema y queso fresco', compareAtPrice: 110, imageUrl: '/visual-img/chilaquiles.svg',
    }),
    product('pd-latte', 'cafe-la-esquina', 'Latte', 55, { imageUrl: '/visual-img/latte.svg' }),
    product('pd-capuchino', 'cafe-la-esquina', 'Capuchino', 52, { imageUrl: '/visual-img/capuchino.svg' }),
    product('pd-concha', 'cafe-la-esquina', 'Concha de vainilla', 18, { imageUrl: '/visual-img/concha.svg' }),
    product('pd-pastor', 'taqueria-el-guero', 'Tacos al pastor (orden de 5)', 85, { description: 'Con piña, cebolla y cilantro' }),
    product('pd-gringa', 'taqueria-el-guero', 'Gringa de pastor', 65),
    product('pd-chilaquiles-rojos', 'cocina-de-lupita', 'Chilaquiles rojos con huevo', 80, { description: 'Con frijoles de la olla' }),
    product('pd-corrida', 'cocina-de-lupita', 'Comida corrida del día', 110),
    product('pd-cafe-olla', 'abarrotes-dona-mary', 'Café de olla molido 500 g', 89),
  ];
}

/** Lowercase and without accents, as the API compares a search. */
const comparable = (text) => (text ?? '').normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();

/** Each card says whether its business delivers to the postal code, as the API does for a business that delivers. */
export const withDeliveryTo = (data, businesses, postalCode) => (postalCode
  ? businesses.map((business) => {
    if (!business.deliveryAvailable) return business;
    const zone = data.directoryZones[business.slug]?.[postalCode];
    return {
      ...business, deliversToPostalCode: !!zone, deliveryFee: zone?.fee ?? null,
      minimumOrder: zone?.minimumOrder ?? null, freeDeliveryFrom: zone?.freeDeliveryFrom ?? null,
    };
  })
  : businesses);

/**
 * The products with every word in their name or description, of the businesses shown (Fase 24F): those whose name has
 * them first, then by the order of Explorar and by name; three of a business and twelve in all, as the API answers them.
 * The demo businesses write their prices with IVA: none adds it.
 */
function searchProducts(data, businesses, words) {
  const position = new Map(businesses.map((business, index) => [business.slug, index]));
  const perBusiness = new Map();
  return data.directoryProducts
    .filter((item) => position.has(item.slug))
    .filter((item) => words.every((word) => comparable(`${item.name} ${item.description ?? ''}`).includes(word)))
    .map((item) => ({ item, text: comparable(item.name), byName: words.every((word) => comparable(item.name).includes(word)) ? 0 : 1 }))
    .sort((a, b) => a.byName - b.byName || position.get(a.item.slug) - position.get(b.item.slug)
      || (a.text < b.text ? -1 : a.text > b.text ? 1 : 0))
    .filter(({ item }) => {
      const shown = perBusiness.get(item.slug) ?? 0;
      perBusiness.set(item.slug, shown + 1);
      return shown < 3;
    })
    .slice(0, 12)
    .map(({ item }) => {
      const business = businesses[position.get(item.slug)];
      return {
        id: item.id, name: item.name, price: item.price, compareAtPrice: item.compareAtPrice, imageUrl: item.imageUrl,
        businessName: business.name, businessSlug: business.slug, theme: business.theme,
        acceptsOrders: business.acceptsOrders, addsTax: false,
      };
    });
}

export function phase24aRoutes(data, { ok, paginate }) {
  data.directory ??= demoDirectory();
  data.directoryProducts ??= demoDirectoryProducts();
  data.directoryZones ??= demoDeliveryZones();
  data.directoryProfile ??= {
    category: FOOD, suggestedCategory: FOOD, description: 'Café de especialidad, pan dulce y chilaquiles desde 2021.', listed: true,
  };
  const visibility = () => (!data.tenant.onlineMenuMode ? MENU_OFF : data.directoryProfile.visibility ?? SHOWN);

  return [
    ['GET', /^\/public\/directory$/, (m, q) => {
      const category = q.get('category');
      const words = comparable(q.get('q')).split(/\s+/).filter(Boolean);
      const inCategory = data.directory.filter((business) => category === null || String(business.category) === category);
      const found = inCategory
        .filter((business) => words.every((word) => comparable(`${business.name} ${business.description ?? ''}`).includes(word)));
      const firstPage = (Number(q.get('pageNumber')) || 1) === 1;
      return ok({
        categories: Array.from({ length: CATEGORIES }, (_, value) => ({
          category: value,
          count: data.directory.filter((business) => business.category === value).length,
        })),
        businesses: paginate(withDeliveryTo(data, found, q.get('postalCode')), q, 24),
        products: words.length > 0 && firstPage ? searchProducts(data, inCategory, words) : [],
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
