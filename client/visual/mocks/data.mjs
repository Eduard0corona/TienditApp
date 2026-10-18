// Demo data for "Café La Esquina" (CDMX): a café that sells food/drinks and also offers services
// (coffee-machine maintenance, barista workshops, terrace rental). Shapes follow client/src/types/index.ts.
// All timestamps are relative to NOW (2026-10-04 13:25 America/Mexico_City, UTC-6).
import { createHash } from 'node:crypto';

export const NOW_ISO = '2026-10-04T19:25:00.000Z';
const TZ_OFFSET_H = 6; // America/Mexico_City has no DST since 2022

export function uuid(seed) {
  const h = createHash('md5').update(String(seed)).digest('hex');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-4${h.slice(13, 16)}-${'89ab'[parseInt(h[16], 16) % 4]}${h.slice(17, 20)}-${h.slice(20, 32)}`;
}

/** Local CDMX wall time on 2026-10-04 (+dayOffset) as an ISO UTC string. */
export function at(h, m = 0, dayOffset = 0) {
  return new Date(Date.UTC(2026, 9, 4 + dayOffset, h + TZ_OFFSET_H, m)).toISOString();
}

const r2 = (x) => Math.round(x * 100) / 100;

// Small deterministic PRNG so generated orders are stable between runs.
function mulberry32(seed) {
  let a = seed;
  return () => {
    a |= 0; a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export const ALL_PERMISSIONS = [
  'ManageCatalog', 'ManageOrders', 'ManageServiceBoard', 'ManageInventory', 'ManageCash',
  'ManageCustomers', 'ViewReports', 'ManageWhatsApp', 'ManageAiAgent', 'ManageEmployees', 'ManageAppointments',
];

export function buildData() {
  const TENANT_ID = uuid('tenant:cafe-la-esquina');
  const TENANT2_ID = uuid('tenant:la-esquina-catering');
  const USER_ID = uuid('user:mariana');
  const OWNER_EMAIL = 'mariana.ortiz@cafelaesquina.mx';

  // ---------------------------------------------------------------- tenant / user
  const tenant = {
    theme: 0,
    id: TENANT_ID,
    name: 'Café La Esquina',
    slug: 'cafe-la-esquina',
    businessType: 0, // Food
    status: 0,
    contactEmail: 'hola@cafelaesquina.mx',
    contactPhone: '+525552641180',
    capabilities: 63, // Retail|Service|Food|Inventory|Appointments|Kitchen
    pricesIncludeTax: true,
    defaultTaxRate: 16,
    autoCloseCounterSales: true,
    onboardingCompletedAt: '2026-02-12T17:00:00.000Z',
    createdAt: '2026-02-12T16:20:00.000Z',
    rfc: 'CLE210315AB7',
    legalName: 'Café La Esquina S.A. de C.V.',
    industry: 'Alimentos y Bebidas',
    taxRegime: '601 - General de Ley Personas Morales',
    street: 'Av. Álvaro Obregón',
    exteriorNumber: '182',
    interiorNumber: 'Local 3',
    neighborhood: 'Roma Norte',
    zipCode: '06700',
    city: 'Ciudad de México',
    state: 'Ciudad de México',
    country: 'México',
  };

  const profile = {
    id: USER_ID,
    email: OWNER_EMAIL,
    firstName: 'Mariana',
    lastName: 'Ortiz',
    fullName: 'Mariana Ortiz',
    phoneNumber: '+5215541230099',
    phoneNumberConfirmed: true,
    emailConfirmed: true,
    role: 'TenantAdmin',
    tenantRole: 'Owner',
    permissions: ALL_PERMISSIONS,
    tenantId: TENANT_ID,
    isActive: true,
    twoFactorEnabled: false,
    createdAt: '2026-02-12T16:20:00.000Z',
  };

  const myTenants = [
    { tenantId: TENANT_ID, name: 'Café La Esquina', slug: 'cafe-la-esquina', role: 'Owner', status: 'Active' },
    { tenantId: TENANT2_ID, name: 'La Esquina Catering', slug: 'la-esquina-catering', role: 'Admin', status: 'Active' },
  ];

  // ---------------------------------------------------------------- catalog
  const cat = (key, name, description, sortOrder, isActive = true) => ({ id: uuid(`cat:${key}`), key, name, description, sortOrder, isActive });
  const categoryList = [
    cat('calientes', 'Bebidas calientes', 'Café de especialidad, tés y chocolate', 1),
    cat('frias', 'Bebidas frías', 'Frappés, cold brew y aguas frescas', 2),
    cat('desayunos', 'Desayunos', 'Chilaquiles, molletes y huevos al gusto (8:00 a 12:30)', 3),
    cat('tortas', 'Tortas y sándwiches', 'Preparados al momento', 4),
    cat('pan', 'Panadería', 'Pan dulce horneado diario', 5),
    cat('grano', 'Café en grano', 'Bolsas de café para llevar a casa', 6),
    cat('servicios', 'Servicios', 'Mantenimiento de equipo, talleres y eventos', 7),
    cat('temporada', 'Temporada', 'Productos de Día de Muertos', 8, false),
  ];
  const C = Object.fromEntries(categoryList.map((c) => [c.key, c]));
  const categories = categoryList.map(({ key, ...rest }) => rest);

  const P = {};
  const products = [];
  let sort = 0;
  const addItem = (key, itemType, categoryKey, name, price, extra = {}) => {
    sort += 1;
    const p = {
      id: uuid(`prod:${key}`),
      categoryId: C[categoryKey].id,
      name,
      description: extra.description,
      sku: extra.sku,
      barcode: extra.barcode,
      sortOrder: sort,
      tags: extra.tags,
      unit: extra.unit ?? (itemType === 'Service' ? 52 : 0),
      priceAmount: price,
      priceCurrency: 'MXN',
      compareAtPrice: extra.compareAtPrice,
      taxRate: extra.taxRate ?? 16,
      isTaxExempt: extra.isTaxExempt ?? false,
      hasDiscount: !!extra.compareAtPrice,
      isAvailable: extra.isAvailable ?? true,
      isFeatured: extra.isFeatured ?? false,
      preparationTimeMinutes: extra.prep,
      calories: extra.calories,
      allergens: extra.allergens,
      stockQuantity: extra.stock,
      lowStockThreshold: extra.lowStock,
      maxQuantityPerOrder: extra.maxQty,
      availableFromTime: extra.from,
      availableToTime: extra.to,
      isLowStock: extra.stock != null && extra.lowStock != null ? extra.stock <= extra.lowStock : false,
      isTrackable: extra.isTrackable ?? false,
      notifyCustomer: extra.notifyCustomer ?? false,
      _itemType: itemType,
    };
    P[key] = p;
    products.push(p);
    return p;
  };

  addItem('americano', 'Product', 'calientes', 'Café americano', 35, { description: 'Café de Chiapas, 12 oz', sku: 'BEB-001', prep: 3, isFeatured: true });
  addItem('espresso', 'Product', 'calientes', 'Espresso doble', 40, { description: 'Doble carga, tueste medio', sku: 'BEB-002', prep: 2 });
  addItem('capuchino', 'Product', 'calientes', 'Capuchino', 50, { description: 'Espresso, leche y espuma, 12 oz', sku: 'BEB-003', prep: 4, allergens: 'Lácteos' });
  addItem('latte', 'Product', 'calientes', 'Latte', 55, { description: 'Espresso con leche vaporizada, 12 oz', sku: 'BEB-004', prep: 4, isFeatured: true, allergens: 'Lácteos' });
  addItem('chocolate', 'Product', 'calientes', 'Chocolate caliente', 45, { description: 'Chocolate de Oaxaca con leche', sku: 'BEB-005', prep: 4 });
  addItem('chai', 'Product', 'calientes', 'Chai latte', 55, { description: 'Té negro especiado con leche', sku: 'BEB-006', prep: 4 });
  addItem('frappe', 'Product', 'frias', 'Frappé moka', 65, { description: 'Café, chocolate y hielo frapeado', sku: 'BEB-010', prep: 5, compareAtPrice: 75 });
  addItem('coldbrew', 'Product', 'frias', 'Cold brew', 55, { description: 'Extracción en frío por 18 horas', sku: 'BEB-011', prep: 2 });
  addItem('jamaica', 'Product', 'frias', 'Agua de jamaica', 30, { description: 'Vaso de 500 ml', sku: 'BEB-012', prep: 1 });
  addItem('chilaquiles', 'Product', 'desayunos', 'Chilaquiles verdes', 85, { description: 'Con pollo, crema y queso fresco', sku: 'ALI-001', prep: 12, from: '08:00:00', to: '12:30:00', unit: 30 });
  addItem('molletes', 'Product', 'desayunos', 'Molletes con pico de gallo', 60, { description: 'Bolillo, frijoles y queso gratinado', sku: 'ALI-002', prep: 10, from: '08:00:00', to: '12:30:00', unit: 30 });
  addItem('torta', 'Product', 'tortas', 'Torta de jamón', 70, { description: 'Telera, jamón de pierna, aguacate y chipotle', sku: 'ALI-010', prep: 8 });
  addItem('sandwich', 'Product', 'tortas', 'Sándwich de pavo y panela', 75, { description: 'Pan multigrano, pavo, panela y espinaca', sku: 'ALI-011', prep: 8 });
  addItem('croissant', 'Product', 'pan', 'Croissant de mantequilla', 38, { description: 'Horneado cada mañana', sku: 'PAN-001', stock: 14, lowStock: 6 });
  addItem('concha', 'Product', 'pan', 'Concha de vainilla', 18, { description: 'Pan dulce tradicional', sku: 'PAN-002', stock: 24, lowStock: 10 });
  addItem('elote', 'Product', 'pan', 'Pan de elote', 45, { description: 'Rebanada', sku: 'PAN-003', stock: 5, lowStock: 8, unit: 31 });
  addItem('chiapas250', 'Product', 'grano', 'Café de Chiapas 250 g', 180, { description: 'Altura, notas a cacao y nuez', sku: 'GRA-250-CHP', barcode: '7501234567012', stock: 14, lowStock: 5, taxRate: 0 });
  addItem('veracruz500', 'Product', 'grano', 'Café de Veracruz 500 g', 320, { description: 'Coatepec, tueste medio', sku: 'GRA-500-VER', barcode: '7501234567029', stock: 3, lowStock: 5, taxRate: 0 });
  addItem('panmuerto', 'Product', 'temporada', 'Pan de muerto', 55, { description: 'Disponible a partir del 15 de octubre', sku: 'PAN-020', isAvailable: false });
  addItem('granel', 'Product', 'grano', 'Café molido a granel', 420, { description: 'Por kilo, molienda al momento', sku: 'GRA-KG', unit: 10, taxRate: 0 });

  addItem('mant', 'Service', 'servicios', 'Mantenimiento de cafetera espresso', 450, { description: 'Limpieza profunda, empaques y ajuste de presión', unit: 52, prep: 120, isTrackable: true, notifyCustomer: true });
  addItem('descal', 'Service', 'servicios', 'Descalcificación de cafetera', 250, { description: 'Retiro de sarro en caldera y grupo', unit: 52, prep: 60, isTrackable: true, notifyCustomer: true });
  addItem('molino', 'Service', 'servicios', 'Calibración de molino', 180, { description: 'Ajuste y limpieza de muelas', unit: 52, prep: 45, isTrackable: true, notifyCustomer: false });
  addItem('taller', 'Service', 'servicios', 'Taller de barismo básico', 650, { description: 'Sesión de 3 horas, incluye material', unit: 51, prep: 180 });
  addItem('cata', 'Service', 'servicios', 'Cata de café de especialidad', 350, { description: 'Degustación guiada de 4 orígenes', unit: 51, prep: 90 });
  addItem('terraza', 'Service', 'servicios', 'Renta de terraza para eventos', 600, { description: 'Hasta 30 personas, precio por hora', unit: 50, from: '16:00:00', to: '22:00:00' });
  addItem('coffeebreak', 'Service', 'servicios', 'Coffee break para oficinas', 1200, { description: 'Café y pan para 10 personas', unit: 52, isAvailable: false });

  // ---------------------------------------------------------------- customers
  const cust = (key, name, phone, email, extra = {}) => ({
    id: uuid(`cust:${key}`), name, phone, email,
    isPublicGeneral: !!extra.publicGeneral, isActive: extra.isActive ?? true,
    createdAt: extra.createdAt ?? '2026-03-02T17:10:00.000Z', updatedAt: extra.updatedAt,
  });
  const customerList = [
    cust('general', 'Público en general', undefined, undefined, { publicGeneral: true, createdAt: '2026-02-12T16:20:00.000Z' }),
    cust('mafer', 'María Fernanda López', '55 4123 8890', 'mafer.lopez@gmail.com', { createdAt: '2026-02-20T15:02:00.000Z' }),
    cust('jc', 'Juan Carlos Hernández', '55 2876 1043', 'jc.hernandez@outlook.com', { createdAt: '2026-03-05T14:40:00.000Z' }),
    cust('sofia', 'Sofía Ramírez Cruz', '55 6190 3327', undefined, { createdAt: '2026-03-18T19:22:00.000Z' }),
    cust('luis', 'Luis Ángel Martínez', '55 3348 7712', 'luis.martinez@yahoo.com.mx', { createdAt: '2026-04-02T16:05:00.000Z' }),
    cust('ana', 'Ana Patricia Gómez', '55 7012 4456', 'anapaty.gomez@gmail.com', { createdAt: '2026-04-11T18:30:00.000Z' }),
    cust('roberto', 'Roberto Sánchez Vega', '55 9921 0087', 'rsanchez@altavista.com.mx', { createdAt: '2026-05-06T15:45:00.000Z' }),
    cust('daniela', 'Daniela Torres', '55 1457 6630', 'dani.torres@gmail.com', { createdAt: '2026-05-21T20:12:00.000Z' }),
    cust('altavista', 'Oficinas Grupo Altavista', '55 5208 9100', 'compras@altavista.com.mx', { createdAt: '2026-06-03T16:00:00.000Z' }),
    cust('jorge', 'Jorge Castillo', '55 8834 2219', undefined, { isActive: false, createdAt: '2026-06-15T17:30:00.000Z' }),
    cust('valeria', 'Valeria Morales', '55 2210 7765', 'vale.morales@icloud.com', { createdAt: '2026-07-09T14:55:00.000Z' }),
    cust('fernando', 'Fernando Ruiz', '55 6677 1290', 'fer.ruiz@gmail.com', { createdAt: '2026-08-22T19:40:00.000Z' }),
    cust('karla', 'Karla Mendoza', '55 3021 7788', undefined, { createdAt: '2026-09-14T15:18:00.000Z' }),
  ];
  const CU = Object.fromEntries(customerList.map((c, i) => [['general', 'mafer', 'jc', 'sofia', 'luis', 'ana', 'roberto', 'daniela', 'altavista', 'jorge', 'valeria', 'fernando', 'karla'][i], c]));
  const e164 = (phone) => (phone ? `+52${phone.replace(/\s+/g, '')}` : undefined);

  // ---------------------------------------------------------------- locations / branches
  const loc = (key, name, code, extra) => ({
    id: uuid(`loc:${key}`), name, code, description: extra.description,
    addressLine1: extra.address, addressLine2: extra.address2, city: 'Ciudad de México', state: 'Ciudad de México',
    country: 'México', postalCode: extra.postalCode, contactName: extra.contactName, contactPhone: extra.contactPhone,
    isDefault: !!extra.isDefault, isActive: extra.isActive ?? true, serviceMode: extra.serviceMode ?? 0,
    latitude: extra.latitude ?? null, longitude: extra.longitude ?? null,
    createdAt: extra.createdAt ?? '2026-02-12T16:30:00.000Z',
  });
  const locations = [
    loc('roma', 'Sucursal Roma Norte', 'ROMA', { description: 'Sucursal principal y cocina', address: 'Av. Álvaro Obregón 182, Local 3', address2: 'Col. Roma Norte', postalCode: '06700', contactName: 'Mariana Ortiz', contactPhone: '55 5264 1180', isDefault: true, latitude: 19.419444, longitude: -99.162222 }),
    loc('coyoacan', 'Sucursal Coyoacán', 'COY', { description: 'Barra de café y panadería', address: 'Calle Francisco Sosa 45', address2: 'Col. Villa Coyoacán', postalCode: '04000', contactName: 'Carlos Méndez', contactPhone: '55 5658 2290', createdAt: '2026-05-10T15:00:00.000Z' }),
    loc('bodega', 'Bodega Central', 'BOD', { description: 'Almacén de insumos secos y desechables', address: 'Calle Pedro Moreno 210', address2: 'Col. Guerrero', postalCode: '06300', contactName: 'Diego Herrera', contactPhone: '55 5526 7714', createdAt: '2026-03-01T15:00:00.000Z' }),
    loc('kiosko', 'Kiosko Parque México', 'KPM', { description: 'Temporada de verano (cerrado)', address: 'Av. México s/n, Parque México', address2: 'Col. Hipódromo', postalCode: '06100', contactName: 'Paola Jiménez', contactPhone: '55 1123 4498', isActive: false, createdAt: '2026-06-01T15:00:00.000Z' }),
  ];
  const L = { roma: locations[0], coy: locations[1], bod: locations[2], kpm: locations[3] };

  // ---------------------------------------------------------------- employees
  const STAFF = {
    carlos: { id: uuid('user:carlos'), name: 'Carlos Méndez' },
    lucia: { id: uuid('user:lucia'), name: 'Lucía Navarro' },
    diego: { id: uuid('user:diego'), name: 'Diego Herrera' },
  };

  // ---------------------------------------------------------------- orders
  const RETAIL_TRANSITIONS = {
    0: [1, 4, 2, 5, 3, 6, 99], 1: [4, 2, 5, 3, 6, 99], 4: [1, 2, 5, 3, 6, 99], 2: [5, 3, 6, 99], 5: [2, 3, 6, 99], 3: [6], 6: [], 99: [],
  };
  const SERVICE_TRANSITIONS = { 0: [4, 1, 5, 7, 99], 1: [4, 5, 7, 99], 4: [1, 5, 7, 99], 5: [7, 99], 7: [], 99: [] };

  const orders = [];
  const makeOrder = (num, spec) => {
    const id = uuid(`order:${num}`);
    const items = spec.items.map(([key, qty, notes], idx) => {
      const p = P[key];
      return { id: uuid(`order:${num}:item:${idx}`), productId: p.id, productName: p.name, quantity: qty, unitPrice: p.priceAmount, totalPrice: r2(p.priceAmount * qty), notes, taxRate: 16, isTaxExempt: false };
    });
    const subTotal = r2(items.reduce((s, it) => s + it.totalPrice, 0));
    const tax = r2(subTotal * 0.16);
    const total = r2(subTotal + tax);
    const payments = (spec.payments ?? []).map((pay, idx) => ({
      id: uuid(`order:${num}:pay:${idx}`),
      amount: pay.amount === 'full' ? total : pay.amount,
      method: pay.method,
      paidAt: pay.paidAt ?? spec.createdAt,
      inventoryLocationId: L.roma.id,
      reference: pay.reference,
      notes: pay.notes,
    }));
    const paidAmount = r2(payments.reduce((s, p) => s + p.amount, 0));
    const orderType = spec.orderType ?? 0;
    const transitions = (orderType === 1 ? SERVICE_TRANSITIONS : RETAIL_TRANSITIONS)[spec.status] ?? [];
    const history = [];
    const path = spec.statusPath ?? [];
    let prev = 0;
    path.forEach((st, idx) => {
      history.push({ id: uuid(`order:${num}:hist:${idx}`), previousStatus: prev, newStatus: st, changedAt: new Date(new Date(spec.createdAt).getTime() + (idx + 1) * 6 * 60000).toISOString(), changedBy: spec.changedBy ?? OWNER_EMAIL });
      prev = st;
    });
    const customer = spec.customer;
    const order = {
      id,
      customerUserId: undefined,
      temporaryCustomerId: customer?.id,
      createdByBusiness: spec.createdByBusiness ?? true,
      channel: spec.channel ?? 0,
      orderNumber: `ORD-${String(num).padStart(6, '0')}`,
      businessType: 0,
      orderType,
      status: spec.status,
      canCancel: transitions.includes(99) && payments.length === 0,
      allowedTransitions: transitions,
      customerName: spec.customerName ?? customer?.name ?? 'Público en general',
      customerPhone: spec.customerPhone ?? e164(customer?.phone),
      customerNotes: spec.customerNotes,
      tableNumber: spec.tableNumber,
      pricesIncludeTax: false,
      taxBreakdown: [{ ratePercent: 16, isExempt: false, taxableBase: subTotal, taxAmount: tax, total }],
      subTotal,
      tax,
      total,
      paidAmount,
      balance: r2(total - paidAmount),
      cancellationReason: spec.cancellationReason,
      createdAt: spec.createdAt,
      updatedAt: spec.updatedAt,
      items,
      statusHistory: history,
      serviceDetail: spec.serviceDetail ? { id: uuid(`order:${num}:svc`), ...spec.serviceDetail } : undefined,
      servicePieces: (spec.pieces ?? []).map((piece, idx) => ({ id: uuid(`order:${num}:piece:${idx}`), isTrackable: true, notifyCustomer: true, ...piece })),
      payments,
    };
    orders.push(order);
    return order;
  };

  // Yesterday: an overdue service order still waiting on the board.
  makeOrder(317, {
    orderType: 1, status: 4, statusPath: [4], customer: CU.luis, createdAt: at(16, 40, -1),
    items: [['mant', 1]],
    serviceDetail: { receivedAt: at(16, 40, -1), promisedAt: at(12, 0), priority: 2, intakeNotes: 'Cliente reporta que no levanta presión.', intakeChecklistJson: JSON.stringify([{ label: 'Portafiltro incluido', checked: true }, { label: 'Jarra de leche incluida', checked: false }, { label: 'Cable de corriente', checked: true }]) },
    pieces: [{ itemName: 'Cafetera Bialetti Moka Express 6 tazas', serviceProductId: P.mant.id, brand: 'Bialetti', model: 'Moka Express', color: 'Aluminio', conditionNotes: 'Empaque reseco, filtro tapado', requestedService: 'Mantenimiento de cafetera espresso', status: 0 }],
    payments: [{ amount: 150, method: 0, paidAt: at(16, 45, -1), notes: 'Anticipo' }],
  });

  // Morning rush (generated): delivered retail orders 07:02 - 10:05.
  const rand = mulberry32(20261004);
  const pick = (arr) => arr[Math.floor(rand() * arr.length)];
  const morningMenu = ['americano', 'americano', 'latte', 'capuchino', 'espresso', 'chilaquiles', 'molletes', 'concha', 'croissant', 'chocolate', 'torta', 'jamaica'];
  const walkIns = [null, null, null, null, CU.mafer, CU.sofia, CU.daniela, CU.karla, CU.fernando, CU.jc];
  const generatedNums = [318, 319, 320, 321, 322, 323, 324, 325, 326, 327, 328, 329, 330, 332, 333, 334, 335, 336];
  generatedNums.forEach((num, idx) => {
    const minutes = 7 * 60 + 2 + idx * 10 + Math.floor(rand() * 6) + (num > 331 ? 12 : 0);
    const nItems = 1 + Math.floor(rand() * 3);
    const chosen = new Map();
    for (let i = 0; i < nItems; i += 1) {
      const key = pick(morningMenu);
      chosen.set(key, (chosen.get(key) ?? 0) + 1 + (rand() < 0.2 ? 1 : 0));
    }
    const customer = pick(walkIns);
    makeOrder(num, {
      status: 6, statusPath: [1, 2, 3, 6], customer: customer ?? CU.general,
      customerName: customer ? undefined : 'Público en general',
      customerPhone: customer ? undefined : '+525530217788',
      tableNumber: rand() < 0.55 ? `Mesa ${1 + Math.floor(rand() * 8)}` : 'Para llevar',
      createdAt: at(Math.floor(minutes / 60), minutes % 60),
      items: [...chosen.entries()].map(([k, q]) => [k, q]),
      payments: [{ amount: 'full', method: rand() < 0.6 ? 0 : 1 }],
    });
  });

  makeOrder(331, {
    orderType: 1, status: 7, statusPath: [4, 5, 7], customer: CU.fernando, createdAt: at(9, 20),
    items: [['molino', 1]],
    serviceDetail: { receivedAt: at(9, 20), promisedAt: at(11, 0), priority: 1, intakeNotes: 'Molino de casa, cliente espera en barra.' },
    pieces: [{ itemName: 'Molino Baratza Encore', serviceProductId: P.molino.id, brand: 'Baratza', model: 'Encore', color: 'Negro', requestedService: 'Calibración de molino', status: 2, assignedToUserId: STAFF.carlos.id, assignedToName: STAFF.carlos.name, assignedAt: at(9, 30) }],
    payments: [{ amount: 'full', method: 1, paidAt: at(10, 5) }],
  });
  makeOrder(337, {
    orderType: 1, status: 5, statusPath: [4, 5], customer: CU.ana, createdAt: at(10, 15),
    items: [['descal', 1], ['molino', 1]],
    serviceDetail: {
      receivedAt: at(10, 15), promisedAt: at(14, 0), priority: 1,
      intakeNotes: 'La cafetera gotea por el grupo; el molino deja grano entero.',
      deliveryNotes: 'Avisar por WhatsApp cuando esté lista.',
      intakeChecklistJson: JSON.stringify([{ label: 'Portafiltro incluido', checked: true }, { label: 'Tanque de agua', checked: true }, { label: 'Tolva del molino', checked: true }]),
    },
    pieces: [
      { itemName: "Cafetera De'Longhi Dedica EC685", serviceProductId: P.descal.id, brand: "De'Longhi", model: 'EC685', color: 'Plata', conditionNotes: 'Sarro visible en la regadera', requestedService: 'Descalcificación de cafetera', status: 1, assignedToUserId: STAFF.carlos.id, assignedToName: STAFF.carlos.name, assignedAt: at(10, 40) },
      { itemName: 'Molino Hario Skerton Pro', serviceProductId: P.molino.id, brand: 'Hario', model: 'Skerton Pro', color: 'Negro', requestedService: 'Calibración de molino', status: 0, notifyCustomer: false },
    ],
    payments: [{ amount: 200, method: 0, paidAt: at(10, 18), notes: 'Anticipo' }],
  });
  makeOrder(338, {
    status: 6, statusPath: [1, 2, 3, 6], customer: CU.jc, tableNumber: 'Mesa 4', createdAt: at(10, 42),
    items: [['latte', 2], ['elote', 1]], payments: [{ amount: 100, method: 0 }],
  });
  makeOrder(339, {
    status: 6, statusPath: [1, 6], customer: CU.valeria, tableNumber: 'Para llevar', createdAt: at(11, 5),
    items: [['chiapas250', 1]], payments: [{ amount: 'full', method: 1, reference: 'Terminal 4821' }],
  });
  makeOrder(340, {
    status: 99, statusPath: [1, 99], customer: CU.general, customerName: 'Público en general', customerPhone: '+525519087734', tableNumber: 'Mesa 3', createdAt: at(11, 30),
    items: [['coldbrew', 1], ['croissant', 1]], cancellationReason: 'El cliente se retiró antes de recibir su pedido',
  });
  makeOrder(341, {
    status: 6, statusPath: [1, 2, 3, 6], customer: CU.sofia, tableNumber: 'Mesa 1', createdAt: at(11, 48),
    items: [['chai', 1], ['croissant', 1]], payments: [{ amount: 'full', method: 1 }],
  });
  makeOrder(342, {
    orderType: 1, status: 4, statusPath: [4], customer: CU.roberto, createdAt: at(12, 5),
    items: [['mant', 1]],
    customerNotes: 'Necesita la cafetera para el lunes temprano.',
    serviceDetail: {
      receivedAt: at(12, 5), promisedAt: at(18, 0, 1), priority: 2,
      intakeNotes: 'Fuga de vapor en la lanceta y baja presión.',
      intakeChecklistJson: JSON.stringify([{ label: 'Portafiltro incluido', checked: true }, { label: 'Jarra de leche incluida', checked: true }, { label: 'Golpe en carcasa lateral', checked: true }]),
    },
    pieces: [{ itemName: 'Cafetera espresso Oster Prima Latte', serviceProductId: P.mant.id, brand: 'Oster', model: 'BVSTEM6701', color: 'Rojo', conditionNotes: 'Rayones en bandeja de goteo', requestedService: 'Mantenimiento de cafetera espresso', internalNotes: 'Revisar empaque del grupo', status: 0 }],
    payments: [{ amount: 200, method: 0, paidAt: at(12, 8), notes: 'Anticipo' }],
  });
  makeOrder(343, {
    status: 6, statusPath: [1, 2, 3, 6], customer: CU.altavista, tableNumber: 'Pedido oficina', createdAt: at(12, 20),
    customerNotes: 'Entregar en recepción, piso 3', items: [['americano', 8], ['concha', 8]],
    payments: [{ amount: 'full', method: 2, reference: 'SPEI 0034917' }],
  });
  makeOrder(344, {
    status: 3, statusPath: [1, 2, 3], customer: CU.luis, tableNumber: 'Mesa 6', createdAt: at(12, 41),
    items: [['frappe', 1], ['sandwich', 1]],
  });
  makeOrder(345, {
    status: 2, statusPath: [1, 2], customer: CU.mafer, tableNumber: 'Para llevar', createdAt: at(12, 55),
    items: [['torta', 2, 'Una sin chipotle'], ['jamaica', 2]],
  });
  makeOrder(346, {
    status: 2, statusPath: [1, 2], customer: CU.general, customerName: 'Público en general', customerPhone: '+525530217788', tableNumber: 'Mesa 5', createdAt: at(13, 2),
    items: [['americano', 2], ['croissant', 2]],
  });
  makeOrder(347, {
    status: 1, statusPath: [1], customer: CU.daniela, channel: 1, createdAt: at(13, 12),
    items: [['capuchino', 1], ['sandwich', 1]],
  });
  makeOrder(348, {
    status: 0, customer: CU.valeria, createdByBusiness: false, channel: 2, createdAt: at(13, 21),
    customerNotes: 'Paso a recoger a las 13:45', items: [['latte', 2], ['croissant', 1]],
  });

  orders.sort((a, b) => b.createdAt.localeCompare(a.createdAt));

  // ---------------------------------------------------------------- dashboard metrics (today)
  const todayStart = at(0, 0);
  const todays = orders.filter((o) => o.createdAt >= todayStart);
  const completedStatuses = [6, 7];
  const statusName = { 0: 'Pending', 1: 'Confirmed', 2: 'InPreparation', 3: 'Ready', 4: 'Accepted', 5: 'InProgress', 6: 'Delivered', 7: 'Completed', 99: 'Cancelled' };
  const ordersByStatus = {};
  todays.forEach((o) => { ordersByStatus[statusName[o.status]] = (ordersByStatus[statusName[o.status]] ?? 0) + 1; });
  const top = new Map();
  todays.filter((o) => completedStatuses.includes(o.status)).forEach((o) => o.items.forEach((it) => {
    const cur = top.get(it.productId) ?? { productId: it.productId, productName: it.productName, itemType: products.find((p) => p.id === it.productId)._itemType, quantitySold: 0, totalAmount: 0 };
    cur.quantitySold += it.quantity; cur.totalAmount = r2(cur.totalAmount + it.totalPrice);
    top.set(it.productId, cur);
  }));
  const dashboard = {
    rangeFrom: todayStart,
    rangeTo: at(23, 59),
    totalOrders: todays.length,
    inProgressOrders: todays.filter((o) => [1, 4, 2, 5, 3].includes(o.status)).length,
    pendingOrders: todays.filter((o) => o.status === 0).length,
    completedOrders: todays.filter((o) => completedStatuses.includes(o.status)).length,
    cancelledOrders: todays.filter((o) => o.status === 99).length,
    uniqueCustomers: new Set(todays.map((o) => o.temporaryCustomerId ?? o.customerPhone)).size,
    activeProducts: products.filter((p) => p._itemType === 'Product' && p.isAvailable).length,
    activeServices: products.filter((p) => p._itemType === 'Service' && p.isAvailable).length,
    totalRevenue: r2(orders.flatMap((o) => o.payments).filter((p) => p.paidAt >= todayStart).reduce((s, p) => s + p.amount, 0)),
    paidOrders: orders.filter((o) => o.payments.some((p) => p.paidAt >= todayStart)).length,
    averageOrderValue: r2(orders.flatMap((o) => o.payments).filter((p) => p.paidAt >= todayStart).reduce((s, p) => s + p.amount, 0)
      / Math.max(1, orders.filter((o) => o.payments.some((p) => p.paidAt >= todayStart)).length)),
    outstandingBalance: r2(todays.filter((o) => o.status !== 99 && o.balance > 0).reduce((s, o) => s + o.balance, 0)),
    unpaidOrders: todays.filter((o) => o.status !== 99 && o.balance > 0).length,
    ordersByStatus,
    topSellingItems: [...top.values()].sort((a, b) => b.quantitySold - a.quantitySold).slice(0, 10),
  };

  // ---------------------------------------------------------------- kitchen + service board
  const kitchenOrders = orders
    // Same rule as GetKitchenOrdersQuery: food orders only (OrderType.Retail), before Ready.
    .filter((o) => o.businessType === 0 && o.orderType === 0 && [0, 1, 4, 2, 5].includes(o.status))
    .sort((a, b) => a.createdAt.localeCompare(b.createdAt))
    .map((o) => ({
      id: o.id, orderNumber: o.orderNumber, channel: o.channel ?? 0, externalOrderNumber: null, status: o.status,
      customerName: o.customerName, tableNumber: o.tableNumber, customerNotes: o.customerNotes, createdAt: o.createdAt, items: o.items,
    }));

  const nowMs = new Date(NOW_ISO).getTime();
  const boardPieces = [];
  orders.filter((o) => o.orderType === 1).forEach((o) => {
    o.servicePieces.forEach((piece) => {
      const promisedAt = o.serviceDetail?.promisedAt;
      let isOverdue = false; let delayMinutes; let minutesToPromise;
      if (promisedAt && (piece.status === 0 || piece.status === 1)) {
        const minutes = Math.round((nowMs - new Date(promisedAt).getTime()) / 60000);
        if (minutes > 0) { isOverdue = true; delayMinutes = minutes; } else { minutesToPromise = Math.abs(minutes); }
      }
      boardPieces.push({
        pieceId: piece.id, orderId: o.id, orderNumber: o.orderNumber, customerName: o.customerName, customerPhone: o.customerPhone,
        orderCreatedAt: o.createdAt, promisedAt, priority: o.serviceDetail?.priority ?? 1, itemName: piece.itemName,
        requestedService: piece.requestedService, internalNotes: piece.internalNotes, notifyCustomer: piece.notifyCustomer,
        status: piece.status, assignedToUserId: piece.assignedToUserId, assignedToName: piece.assignedToName, assignedAt: piece.assignedAt,
        isOverdue, delayMinutes, minutesToPromise,
      });
    });
  });
  // A cancelled piece from earlier in the week (visible only with "canceladas").
  boardPieces.push({
    pieceId: uuid('piece:cancelled'), orderId: uuid('order:309'), orderNumber: 'ORD-000309', customerName: 'Jorge Castillo', customerPhone: '+525588342219',
    orderCreatedAt: at(17, 10, -3), promisedAt: at(12, 0, -2), priority: 1, itemName: 'Prensa francesa Bodum 1 L', requestedService: 'Cambio de filtro',
    notifyCustomer: false, status: 99, isOverdue: false,
  });

  // ---------------------------------------------------------------- cash
  const cashSessionId = uuid('cash:session:today');
  const cashTx = [];
  const addTx = (type, amount, occurredAt, extra = {}) => cashTx.push({
    id: uuid(`cash:tx:${cashTx.length}`), cashSessionId, transactionType: type, amount, occurredAt,
    paymentMethod: extra.paymentMethod, inventoryLocationId: L.roma.id, inventoryLocationName: L.roma.name,
    orderId: extra.orderId, reference: extra.reference, notes: extra.notes, createdBy: extra.createdBy ?? OWNER_EMAIL,
  });
  orders.filter((o) => o.createdAt >= todayStart).forEach((o) => o.payments.forEach((pay) => {
    addTx(0, pay.amount, pay.paidAt, { paymentMethod: pay.method, orderId: o.id, reference: o.orderNumber, notes: pay.notes, createdBy: pay.method === 0 && o.createdAt < at(10, 0) ? 'carlos.mendez@cafelaesquina.mx' : OWNER_EMAIL });
  }));
  // The API stores outflows (refund, expense, withdrawal) as negative amounts (CashSession.ToSignedAmount).
  addTx(2, -180, at(9, 40), { reference: 'Hielo (6 bolsas)', notes: 'Pago a proveedor de hielo' });
  addTx(3, -2000, at(11, 15), { reference: 'Retiro a caja fuerte', notes: 'Exceso de efectivo' });
  addTx(4, 500, at(8, 10), { reference: 'Cambio', notes: 'Monedas para cambio' });
  addTx(2, -95, at(12, 30), { reference: 'Garrafones de agua', notes: '' });
  cashTx.sort((a, b) => b.occurredAt.localeCompare(a.occurredAt));
  const cashIn = r2(cashTx.filter((t) => (t.transactionType === 0 && t.paymentMethod === 0) || t.transactionType === 4).reduce((s, t) => s + t.amount, 0));
  const cashOut = r2(cashTx.filter((t) => [1, 2, 3].includes(t.transactionType)).reduce((s, t) => s - t.amount, 0));
  const nonCash = r2(cashTx.filter((t) => t.transactionType === 0 && t.paymentMethod !== 0).reduce((s, t) => s + t.amount, 0));
  const cashSession = {
    id: cashSessionId, openedAt: at(7, 0), openedBy: OWNER_EMAIL, openingAmount: 1500, status: 0,
    notes: 'Fondo turno matutino', inflows: cashIn, outflows: cashOut, nonCashInflows: nonCash,
    expectedBalance: r2(1500 + cashIn - cashOut),
  };
  const sumBy = (method) => r2(cashTx.filter((t) => t.transactionType === 0 && t.paymentMethod === method).reduce((s, t) => s + t.amount, 0));
  const salesTx = cashTx.filter((t) => t.transactionType === 0);
  const byUser = new Map();
  salesTx.forEach((t) => { const u = byUser.get(t.createdBy) ?? { userName: t.createdBy, paymentsCount: 0, totalAmount: 0 }; u.paymentsCount += 1; u.totalAmount = r2(u.totalAmount + t.amount); byUser.set(t.createdBy, u); });
  const dailyCut = {
    date: at(0, 0),
    salesPaymentsTotal: r2(salesTx.reduce((s, t) => s + t.amount, 0)),
    salesPaymentsCount: salesTx.length,
    cashTotal: sumBy(0), cardTotal: sumBy(1), transferTotal: sumBy(2), walletTotal: sumBy(3), otherTotal: sumBy(4),
    manualIncomeTotal: 500, manualExpenseTotal: 2275,
    netCashFlow: r2(salesTx.reduce((s, t) => s + t.amount, 0) + 500 - 2275),
    netCashOnlyFlow: r2(sumBy(0) + 500 - 2275),
    byUser: [...byUser.values()],
    byLocation: [{ inventoryLocationId: L.roma.id, locationName: L.roma.name, paymentsCount: salesTx.length, totalAmount: r2(salesTx.reduce((s, t) => s + t.amount, 0)) }],
    byBranch: [{ branchId: L.roma.id, branchName: L.roma.name, paymentsCount: salesTx.length, totalAmount: r2(salesTx.reduce((s, t) => s + t.amount, 0)) }],
  };

  // ---------------------------------------------------------------- inventory
  const sup = (key, name, extra) => {
    const onHand = extra.onHand;
    return {
      id: uuid(`supply:${key}`), name, code: extra.code, sku: extra.sku, barcode: extra.barcode, description: extra.description,
      brand: extra.brand, model: undefined, categoryName: extra.category, unit: extra.unit, defaultCost: extra.cost,
      lastCost: extra.lastCost ?? extra.cost, averageCost: extra.avgCost ?? extra.cost, minStock: extra.min, maxStock: extra.max,
      reorderPoint: extra.reorder, trackBatches: !!extra.batches, trackExpiration: !!extra.expiration, isPerishable: !!extra.perishable,
      isCritical: !!extra.critical, taxRate: extra.taxRate ?? 0, notes: extra.notes, isActive: extra.isActive ?? true,
      createdAt: '2026-02-14T16:00:00.000Z', updatedAt: extra.updatedAt ?? at(8, 30),
      totalOnHand: onHand, totalReserved: extra.reserved ?? 0, totalAvailable: r2(onHand - (extra.reserved ?? 0)),
      isLowStock: onHand <= extra.reorder,
    };
  };
  const supplies = [
    sup('cafe-chiapas', 'Café en grano Chiapas', { code: 'INS-001', sku: 'CAF-CHP-KG', category: 'Café', unit: 10, cost: 380, lastCost: 360, avgCost: 372.5, min: 5, max: 30, reorder: 8, onHand: 12.5, critical: true, brand: 'Finca Las Nubes' }),
    sup('leche', 'Leche entera', { code: 'INS-002', category: 'Lácteos', unit: 20, cost: 24, lastCost: 23.5, min: 20, max: 80, reorder: 30, onHand: 18, perishable: true, expiration: true, critical: true, brand: 'Lácteos San Juan', reserved: 2 }),
    sup('almendra', 'Leche de almendra', { code: 'INS-003', category: 'Lácteos', unit: 20, cost: 58, min: 6, max: 24, reorder: 8, onHand: 9, perishable: true, expiration: true }),
    sup('telera', 'Pan telera', { code: 'INS-004', category: 'Panadería', unit: 0, cost: 4.5, lastCost: 4.2, min: 30, max: 150, reorder: 40, onHand: 22, perishable: true, critical: true, brand: 'Panificadora La Espiga' }),
    // Phase 5A: the stock of a product lives in the ledger with the product's unit (31 = rebanada).
    { ...sup('elote', 'Pan de elote', { code: 'PAN-003', sku: 'PAN-003', category: 'Pan', unit: 31, cost: 18, min: 4, max: 30, reorder: 8, onHand: 5 }), productId: P.elote.id, productName: P.elote.name },
    sup('jamon', 'Jamón de pierna', { code: 'INS-005', category: 'Carnes frías', unit: 10, cost: 165, min: 3, max: 10, reorder: 4, onHand: 3.2, perishable: true, expiration: true, batches: true }),
    sup('panela', 'Queso panela', { code: 'INS-006', category: 'Lácteos', unit: 10, cost: 120, min: 2, max: 8, reorder: 3, onHand: 4.5, perishable: true, expiration: true }),
    sup('totopos', 'Totopos para chilaquiles', { code: 'INS-007', category: 'Abarrotes', unit: 10, cost: 28, min: 5, max: 20, reorder: 6, onHand: 0, critical: false }),
    sup('chocolate', 'Chocolate de Oaxaca en tablilla', { code: 'INS-008', category: 'Abarrotes', unit: 10, cost: 210, min: 2, max: 8, reorder: 3, onHand: 5 }),
    sup('vasos', 'Vasos desechables 12 oz (50 pzas)', { code: 'INS-009', category: 'Desechables', unit: 1, cost: 95, lastCost: 92, min: 4, max: 20, reorder: 6, onHand: 10, taxRate: 16, brand: 'Desechables del Centro' }),
    sup('tapas', 'Tapas para vaso 12 oz (50 pzas)', { code: 'INS-010', category: 'Desechables', unit: 1, cost: 60, min: 4, max: 20, reorder: 6, onHand: 3, taxRate: 16 }),
    sup('azucar', 'Azúcar mascabado', { code: 'INS-011', category: 'Abarrotes', unit: 10, cost: 42, min: 3, max: 12, reorder: 4, onHand: 7 }),
    sup('jamaica', 'Flor de jamaica', { code: 'INS-012', category: 'Abarrotes', unit: 10, cost: 160, min: 1, max: 5, reorder: 1.5, onHand: 2.4 }),
    sup('hielo', 'Hielo en bolsa 5 kg', { code: 'INS-013', category: 'Bebidas', unit: 0, cost: 30, min: 4, max: 15, reorder: 6, onHand: 8 }),
    sup('vainilla', 'Jarabe de vainilla 750 ml', { code: 'INS-014', category: 'Jarabes', unit: 0, cost: 185, min: 1, max: 6, reorder: 2, onHand: 4, isActive: false, notes: 'Descontinuado por el proveedor' }),
  ];
  const S = Object.fromEntries(supplies.map((s) => [s.code, s]));
  const stockRows = [];
  const addStock = (supply, location, onHand, extra = {}) => stockRows.push({
    stockId: uuid(`stock:${supply.id}:${location.id}`), supplyItemId: supply.id, supplyItemName: supply.name, supplyCode: supply.code,
    supplySku: supply.sku, supplyBarcode: supply.barcode, unit: supply.unit, inventoryLocationId: location.id,
    inventoryLocationName: location.name, inventoryLocationCode: location.code, onHandQuantity: onHand,
    reservedQuantity: extra.reserved ?? 0, availableQuantity: r2(onHand - (extra.reserved ?? 0)), minStock: supply.minStock,
    reorderPoint: supply.reorderPoint, maxStock: supply.maxStock, lastCost: supply.lastCost, lastMovementAt: extra.lastMovementAt ?? at(8, 30),
    isSupplyActive: supply.isActive, isLocationActive: location.isActive, isLowStock: onHand <= supply.reorderPoint,
  });
  addStock(S['INS-001'], L.roma, 8.5, { lastMovementAt: at(12, 20) });
  addStock(S['INS-001'], L.coy, 4, { lastMovementAt: at(9, 5) });
  addStock(S['PAN-003'], L.roma, 5, { lastMovementAt: at(11, 40) });
  addStock(S['INS-002'], L.roma, 14, { reserved: 2, lastMovementAt: at(12, 55) });
  addStock(S['INS-002'], L.coy, 4);
  addStock(S['INS-003'], L.roma, 9);
  addStock(S['INS-004'], L.roma, 22, { lastMovementAt: at(12, 55) });
  addStock(S['INS-005'], L.roma, 3.2, { lastMovementAt: at(12, 55) });
  addStock(S['INS-006'], L.roma, 4.5);
  addStock(S['INS-007'], L.roma, 0, { lastMovementAt: at(11, 40) });
  addStock(S['INS-008'], L.bod, 5, { lastMovementAt: at(10, 0, -2) });
  addStock(S['INS-009'], L.bod, 7, { lastMovementAt: at(17, 0, -3) });
  addStock(S['INS-009'], L.roma, 3);
  addStock(S['INS-010'], L.roma, 3);
  addStock(S['INS-011'], L.bod, 7);
  addStock(S['INS-012'], L.roma, 2.4);
  addStock(S['INS-013'], L.roma, 8, { lastMovementAt: at(9, 40) });
  addStock(S['INS-014'], L.bod, 4, { lastMovementAt: at(12, 0, -20) });

  const movements = [];
  const addMovement = (num, type, reason, date, lines, extra = {}) => {
    const id = uuid(`mov:${num}`);
    const mappedLines = lines.map(([supply, location, qty, unitCost, before], idx) => ({
      id: uuid(`mov:${num}:line:${idx}`), supplyItemId: supply.id, supplyItemName: supply.name, inventoryLocationId: location.id,
      inventoryLocationName: location.name, quantity: qty, unitCost, totalCost: unitCost != null ? r2(unitCost * qty) : undefined,
      quantityBefore: before, quantityAfter: r2([1, 3, 5, 7].includes(type) ? before - qty : before + qty),
      lotNumber: extra.lot, expirationDate: extra.expiration,
    }));
    movements.push({
      id, movementNumber: num, movementType: type, movementReason: reason, movementDate: date, referenceNumber: extra.reference,
      externalReference: extra.external, counterpartyName: extra.counterparty, documentNumber: extra.document, notes: extra.notes,
      totalAmount: r2(mappedLines.reduce((s, l) => s + (l.totalCost ?? 0), 0)), createdAt: date, lines: mappedLines,
    });
  };
  addMovement('INV-20261004-0004', 1, 1, at(12, 55), [[S['INS-004'], L.roma, 2, 4.5, 24], [S['INS-005'], L.roma, 0.24, 165, 3.44]], { reference: 'ORDER:ORD-000345', notes: 'Consumo por receta (2 tortas de jamón)' });
  addMovement('INV-20261004-0003', 1, 1, at(12, 20), [[S['INS-001'], L.roma, 0.144, 380, 8.644]], { reference: 'ORDER:ORD-000343', notes: 'Consumo por receta (8 cafés americanos)' });
  addMovement('INV-20261004-0002', 3, 4, at(11, 40), [[S['INS-007'], L.roma, 1.5, 28, 1.5]], { notes: 'Merma: totopos humedecidos', reference: 'MERMA-1004' });
  addMovement('INV-20261004-0001', 0, 0, at(8, 30), [[S['INS-002'], L.roma, 24, 23.5, 0], [S['INS-003'], L.roma, 6, 56, 3]], { counterparty: 'Lácteos San Juan', document: 'FAC-A-88213', reference: 'PO-20261003-0002', notes: 'Recepción parcial de lácteos' });
  addMovement('INV-20261003-0002', 5, 9, at(18, 10, -1), [[S['INS-001'], L.roma, 4, 372.5, 12.6]], { notes: 'Transferencia a Sucursal Coyoacán', reference: 'TR-0031' });
  addMovement('INV-20261003-0001', 0, 0, at(10, 15, -1), [[S['INS-004'], L.roma, 120, 4.2, 6]], { counterparty: 'Panificadora La Espiga', document: 'NV-5521', reference: 'PO-20261003-0001' });
  addMovement('INV-20261002-0001', 2, 6, at(19, 0, -2), [[S['INS-011'], L.bod, 0.5, 42, 6.5]], { notes: 'Conteo físico semanal' });

  const kardex = [];
  movements.forEach((m) => m.lines.forEach((line) => {
    const isOut = [1, 3, 5, 7].includes(m.movementType);
    kardex.push({
      movementLineId: line.id, movementId: m.id, movementNumber: m.movementNumber, movementDate: m.movementDate,
      movementType: m.movementType, movementReason: m.movementReason, referenceNumber: m.referenceNumber, externalReference: m.externalReference,
      supplyItemId: line.supplyItemId, supplyItemName: line.supplyItemName, inventoryLocationId: line.inventoryLocationId,
      inventoryLocationName: line.inventoryLocationName, quantityIn: isOut ? 0 : line.quantity, quantityOut: isOut ? line.quantity : 0,
      signedQuantity: isOut ? -line.quantity : line.quantity, quantityBefore: line.quantityBefore, quantityAfter: line.quantityAfter,
      unitCost: line.unitCost, totalCost: line.totalCost,
    });
  }));

  const alertItems = stockRows.filter((s) => s.isLowStock && s.isSupplyActive).map((s) => {
    const supply = supplies.find((x) => x.id === s.supplyItemId);
    return {
      supplyItemId: s.supplyItemId, supplyItemName: s.supplyItemName, inventoryLocationId: s.inventoryLocationId,
      inventoryLocationName: s.inventoryLocationName, onHandQuantity: s.onHandQuantity, reorderPoint: s.reorderPoint,
      deficit: r2(Math.max(0, s.reorderPoint - s.onHandQuantity)), isOutOfStock: s.onHandQuantity <= 0, isCritical: supply.isCritical,
    };
  });
  const alerts = {
    lowStockCount: alertItems.length,
    criticalLowStockCount: alertItems.filter((a) => a.isCritical).length,
    outOfStockCount: alertItems.filter((a) => a.isOutOfStock).length,
    missingRecipeProductCount: 9,
    items: alertItems,
  };

  const recipe = (productKey, lines) => ({
    productId: P[productKey].id, productName: P[productKey].name, productIsAvailable: P[productKey].isAvailable,
    lines: lines.map(([supply, qty, waste, location, notes], idx) => ({
      requirementId: uuid(`recipe:${productKey}:${idx}`), supplyItemId: supply.id, supplyItemName: supply.name, supplyCode: supply.code,
      inventoryLocationId: location?.id, inventoryLocationName: location?.name, quantity: qty, wastePercent: waste,
      effectiveQuantity: r2(qty * (1 + waste / 100) * 1000) / 1000, notes, isActive: true,
    })),
  });
  const recipes = {
    [P.jamaica.id]: recipe('jamaica', [[S['INS-012'], 0.02, 5, L.roma, 'Concentrado preparado diario'], [S['INS-011'], 0.03, 0, L.roma], [S['INS-013'], 0.1, 0, L.roma], [S['INS-009'], 0.02, 2, L.roma, '1 vaso']]),
    [P.americano.id]: recipe('americano', [[S['INS-001'], 0.018, 3, L.roma, '18 g por taza'], [S['INS-009'], 0.02, 2, L.roma], [S['INS-010'], 0.02, 2, L.roma]]),
    [P.latte.id]: recipe('latte', [[S['INS-001'], 0.018, 3, L.roma], [S['INS-002'], 0.25, 5, L.roma, 'Leche vaporizada']]),
    [P.torta.id]: recipe('torta', [[S['INS-004'], 1, 0, L.roma], [S['INS-005'], 0.12, 2, L.roma]]),
  };

  // ---------------------------------------------------------------- suppliers / purchases
  const supplierRow = (key, name, extra) => ({ id: uuid(`supplier:${key}`), name, contactName: extra.contact, phone: extra.phone, email: extra.email, taxId: extra.taxId, address: extra.address, notes: extra.notes, isActive: extra.isActive ?? true });
  const suppliers = [
    supplierRow('nubes', 'Café Finca Las Nubes', { contact: 'Ramiro Pérez', phone: '967 112 3344', email: 'ventas@fincalasnubes.mx', taxId: 'FLN150820KJ3', address: 'Carretera a Chamula km 4, San Cristóbal de las Casas, Chis.', notes: 'Entrega los martes' }),
    supplierRow('sanjuan', 'Lácteos San Juan', { contact: 'Gloria Reyes', phone: '55 5741 2200', email: 'pedidos@lacteossanjuan.com.mx', taxId: 'LSJ980312QW1', address: 'Av. Central 120, Ecatepec, Edo. Méx.' }),
    supplierRow('espiga', 'Panificadora La Espiga', { contact: 'Martín Aguilar', phone: '55 5530 8812', email: 'laespiga.pedidos@gmail.com', address: 'Calle Sur 74 #310, Col. Agrícola Oriental' }),
    supplierRow('desechables', 'Desechables del Centro', { contact: 'Karla Núñez', phone: '55 5522 4410', email: 'ventas@desechablesdelcentro.mx', taxId: 'DCE120607H45', address: 'República de Uruguay 88, Centro' }),
    supplierRow('ruiz', 'Abarrotes Hermanos Ruiz', { contact: 'Pedro Ruiz', phone: '55 5789 0021', email: 'hnosruiz@prodigy.net.mx', address: 'Central de Abasto, Bodega J-24' }),
    supplierRow('toluca', 'Carnes Frías Toluca', { contact: 'Héctor Salinas', phone: '722 214 5566', email: 'hsalinas@cftoluca.mx', isActive: false, notes: 'Suspendido por retrasos' }),
  ];
  const SU = Object.fromEntries(suppliers.map((s, i) => [['nubes', 'sanjuan', 'espiga', 'desechables', 'ruiz', 'toluca'][i], s]));
  const purchaseOrders = [];
  const addPo = (num, supplier, status, orderedAt, lines, extra = {}) => {
    const mapped = lines.map(([supply, location, qty, cost, tax, received], idx) => {
      const subTotal = r2(qty * cost); const taxAmount = r2(subTotal * tax / 100);
      return { id: uuid(`po:${num}:line:${idx}`), supplyItemId: supply.id, supplyItemName: supply.name, inventoryLocationId: location.id, inventoryLocationName: location.name, quantity: qty, receivedQuantity: received ?? 0, pendingQuantity: r2(qty - (received ?? 0)), unitCost: cost, taxPercent: tax, subTotal, taxAmount, total: r2(subTotal + taxAmount) };
    });
    const subTotal = r2(mapped.reduce((s, l) => s + l.subTotal, 0)); const tax = r2(mapped.reduce((s, l) => s + l.taxAmount, 0));
    purchaseOrders.push({ id: uuid(`po:${num}`), supplierId: supplier.id, supplierName: supplier.name, orderNumber: num, status, orderedAt, expectedDeliveryAt: extra.expected, receivedAt: extra.receivedAt, referenceNumber: extra.reference, notes: extra.notes, subTotal, tax, total: r2(subTotal + tax), lines: mapped });
  };
  addPo('PO-20261004-0001', SU.nubes, 0, at(9, 0), [[S['INS-001'], L.roma, 10, 360, 0, 0]], { expected: at(10, 0, 3), notes: 'Pedido quincenal de café' });
  addPo('PO-20261003-0002', SU.sanjuan, 2, at(17, 30, -1), [[S['INS-002'], L.roma, 60, 23.5, 0, 24], [S['INS-003'], L.roma, 12, 56, 0, 6]], { expected: at(8, 0), reference: 'FAC-A-88213' });
  addPo('PO-20261003-0001', SU.espiga, 3, at(8, 0, -1), [[S['INS-004'], L.roma, 120, 4.2, 0, 120]], { expected: at(10, 0, -1), receivedAt: at(10, 15, -1) });
  addPo('PO-20261002-0001', SU.desechables, 1, at(12, 0, -2), [[S['INS-009'], L.bod, 12, 92, 16, 0], [S['INS-010'], L.bod, 12, 58, 16, 0]], { expected: at(12, 0, 1) });
  addPo('PO-20260930-0001', SU.ruiz, 3, at(11, 0, -4), [[S['INS-011'], L.bod, 6, 42, 0, 6], [S['INS-008'], L.bod, 4, 205, 0, 4], [S['INS-012'], L.roma, 2, 160, 0, 2]], { receivedAt: at(16, 0, -4) });
  addPo('PO-20260928-0001', SU.toluca, 99, at(13, 0, -6), [[S['INS-005'], L.roma, 8, 160, 0, 0]], { notes: 'Cancelada: proveedor sin existencia' });

  // ---------------------------------------------------------------- appointments
  const appt = (key, customerName, customerPhone, serviceName, staffName, startAt, endAt, status, notes) => ({ id: uuid(`appt:${key}`), customerName, customerPhone, serviceName, staffName, startAt, endAt, status, notes });
  const appointments = [
    appt('a1', 'Fernando Ruiz', '55 6677 1290', 'Renta de terraza para eventos', 'Mariana Ortiz', at(18, 0, 6), at(22, 0, 6), 1, 'Cumpleaños, 25 personas. Pidió coffee break.'),
    appt('a2', 'Sofía Ramírez Cruz', '55 6190 3327', 'Mantenimiento de cafetera a domicilio', 'Carlos Méndez', at(10, 0, 1), at(11, 30, 1), 0, 'Cafetera Breville Barista Express'),
    appt('a3', 'Daniela Torres', '55 1457 6630', 'Taller de barismo básico', 'Carlos Méndez', at(16, 0), at(19, 0), 1, 'Grupo de 6 personas'),
    appt('a4', 'Ana Patricia Gómez', '55 7012 4456', 'Cata de café de especialidad', 'Mariana Ortiz', at(13, 0), at(14, 30), 2),
    appt('a5', 'Luis Ángel Martínez', '55 3348 7712', 'Calibración de molino', 'Carlos Méndez', at(11, 0), at(11, 45), 99, 'Reagendar la próxima semana'),
    appt('a6', 'Juan Carlos Hernández', '55 2876 1043', 'Taller de barismo básico', 'Carlos Méndez', at(9, 0), at(12, 0), 3),
    appt('a7', 'Valeria Morales', '55 2210 7765', 'Cata de café de especialidad', 'Mariana Ortiz', at(17, 0, -1), at(18, 30, -1), 4, 'No se presentó'),
    appt('a8', 'Oficinas Grupo Altavista', '55 5208 9100', 'Coffee break para oficinas', 'Diego Herrera', at(9, 0, 2), at(10, 0, 2), 0, 'Entregar en piso 3'),
    appt('a9', 'Karla Mendoza', '55 3021 7788', 'Taller de barismo básico', 'Carlos Méndez', at(16, 0, 7), at(19, 0, 7), 0),
  ].sort((a, b) => b.startAt.localeCompare(a.startAt));

  // ---------------------------------------------------------------- employees
  const employee = (key, first, last, email, phone, appRole, tenantRole, status, permissions, createdAt) => ({
    tenantUserId: uuid(`tu:${key}`), userId: key === 'mariana' ? USER_ID : uuid(`user:${key}`), email, firstName: first, lastName: last,
    phoneNumber: phone, appRole, tenantRole, status, isActive: status === 'Active', createdAt, permissions,
  });
  const employees = [
    employee('mariana', 'Mariana', 'Ortiz', OWNER_EMAIL, '+5215541230099', 'TenantAdmin', 'Owner', 'Active', ALL_PERMISSIONS, '2026-02-12T16:20:00.000Z'),
    employee('paola', 'Paola', 'Jiménez', 'paola.jimenez@cafelaesquina.mx', '+5215511234498', 'TenantAdmin', 'Admin', 'Active', ALL_PERMISSIONS.filter((p) => p !== 'ManageEmployees'), '2026-03-01T15:00:00.000Z'),
    employee('carlos', 'Carlos', 'Méndez', 'carlos.mendez@cafelaesquina.mx', '+5215556582290', 'Staff', 'Staff', 'Active', ['ManageOrders', 'ManageServiceBoard', 'ManageCash', 'ManageAppointments'], '2026-03-04T15:00:00.000Z'),
    employee('lucia', 'Lucía', 'Navarro', 'lucia.navarro@cafelaesquina.mx', '+5215543321187', 'Kitchen', 'Staff', 'Active', ['ManageServiceBoard'], '2026-04-20T15:00:00.000Z'),
    employee('diego', 'Diego', 'Herrera', 'diego.herrera@cafelaesquina.mx', '+5215555267714', 'Staff', 'Staff', 'Active', ['ManageOrders', 'ManageCash', 'ManageCustomers', 'ManageInventory'], '2026-05-02T15:00:00.000Z'),
    employee('andrea', 'Andrea', 'Vázquez', 'andrea.vazquez@gmail.com', '+5215590012233', 'Staff', 'Staff', 'Invited', ['ManageOrders'], '2026-09-28T15:00:00.000Z'),
    employee('ivan', 'Iván', 'Rosales', 'ivan.rosales@cafelaesquina.mx', '+5215566778899', 'Staff', 'Staff', 'Disabled', ['ManageOrders', 'ManageCash'], '2026-03-15T15:00:00.000Z'),
  ];
  const permissionOptions = [
    { code: 'ManageCatalog', name: 'Catalogo', description: 'Gestionar productos, categorias y servicios.' },
    { code: 'ManageOrders', name: 'Ordenes', description: 'Crear, actualizar y operar ordenes.' },
    { code: 'ManageServiceBoard', name: 'Tablero de servicios', description: 'Actualizar piezas y estados operativos.' },
    { code: 'ManageInventory', name: 'Inventario', description: 'Administrar insumos, stock, compras y proveedores.' },
    { code: 'ManageCash', name: 'Caja', description: 'Registrar pagos, movimientos y cortes.' },
    { code: 'ManageCustomers', name: 'Clientes', description: 'Administrar clientes del negocio.' },
    { code: 'ViewReports', name: 'Reportes', description: 'Visualizar dashboards, reportes y auditoria.' },
    { code: 'ManageWhatsApp', name: 'WhatsApp', description: 'Acceder a configuracion y chat de WhatsApp.' },
    { code: 'ManageAiAgent', name: 'Agente IA', description: 'Configurar agentes de IA.' },
    { code: 'ManageEmployees', name: 'Empleados', description: 'Administrar usuarios empleados, roles y permisos.' },
    { code: 'ManageAppointments', name: 'Citas', description: 'Gestionar agenda y citas.' },
  ];

  // ---------------------------------------------------------------- audit
  const auditLogs = [];
  const addAudit = (entityName, entityId, action, performedBy, timestamp, metadata) => auditLogs.push({ id: uuid(`audit:${auditLogs.length}`), entityName, entityId, action, performedBy, timestamp, metadata: metadata ? JSON.stringify(metadata) : undefined });
  const byNum = (n) => orders.find((o) => o.orderNumber === `ORD-${String(n).padStart(6, '0')}`);
  addAudit('Order', byNum(348).id, 'OrderCreated', 'whatsapp-agent', at(13, 21), { OrderNumber: 'ORD-000348', BusinessType: 'Food', Total: byNum(348).total });
  addAudit('Order', byNum(347).id, 'OrderStatusChanged', OWNER_EMAIL, at(13, 14), { OrderNumber: 'ORD-000347', PreviousStatus: 'Pending', NewStatus: 'Confirmed' });
  addAudit('Order', byNum(347).id, 'OrderCreated', OWNER_EMAIL, at(13, 12), { OrderNumber: 'ORD-000347', BusinessType: 'Food', Total: byNum(347).total });
  addAudit('Order', byNum(346).id, 'OrderStatusChanged', 'lucia.navarro@cafelaesquina.mx', at(13, 8), { OrderNumber: 'ORD-000346', PreviousStatus: 'Confirmed', NewStatus: 'InPreparation' });
  addAudit('Order', byNum(345).id, 'OrderStatusChanged', 'lucia.navarro@cafelaesquina.mx', at(13, 1), { OrderNumber: 'ORD-000345', PreviousStatus: 'Confirmed', NewStatus: 'InPreparation' });
  addAudit('Order', byNum(344).id, 'OrderStatusChanged', 'lucia.navarro@cafelaesquina.mx', at(12, 58), { OrderNumber: 'ORD-000344', PreviousStatus: 'InPreparation', NewStatus: 'Ready' });
  addAudit('Order', byNum(342).id, 'OrderCreated', OWNER_EMAIL, at(12, 5), { OrderNumber: 'ORD-000342', BusinessType: 'Food', Total: byNum(342).total });
  addAudit('Order', byNum(340).id, 'OrderCancelled', 'carlos.mendez@cafelaesquina.mx', at(11, 41), { OrderNumber: 'ORD-000340', PreviousStatus: 'Confirmed', Reason: 'El cliente se retiró antes de recibir su pedido' });
  addAudit('ServiceOrderPiece', byNum(337).servicePieces[0].id, 'ServiceOrderPieceStatusChanged', 'carlos.mendez@cafelaesquina.mx', at(10, 41), { OrderNumber: 'ORD-000337', PreviousStatus: 'Accepted', NewStatus: 'InProgress' });
  addAudit('ServiceOrderPiece', byNum(331).servicePieces[0].id, 'ServiceOrderPieceStatusChanged', 'carlos.mendez@cafelaesquina.mx', at(9, 58), { OrderNumber: 'ORD-000331', PreviousStatus: 'InProgress', NewStatus: 'Completed' });
  addAudit('Order', byNum(331).id, 'OrderStatusChanged', 'carlos.mendez@cafelaesquina.mx', at(10, 5), { OrderNumber: 'ORD-000331', PreviousStatus: 'InProgress', NewStatus: 'Completed' });
  addAudit('TenantSettings', TENANT_ID, 'TenantCapabilitiesUpdated', OWNER_EMAIL, at(19, 2, -2), { PreviousCapabilities: 'Retail, Food, Inventory, Kitchen', NewCapabilities: 'Retail, Service, Food, Inventory, Appointments, Kitchen' });
  for (let i = 0; i < 26; i += 1) {
    const n = 336 - i;
    const o = byNum(n);
    if (!o) continue;
    addAudit('Order', o.id, i % 2 ? 'OrderCreated' : 'OrderStatusChanged', i % 3 ? 'carlos.mendez@cafelaesquina.mx' : OWNER_EMAIL, o.createdAt, i % 2 ? { OrderNumber: o.orderNumber, BusinessType: 'Food', Total: o.total } : { OrderNumber: o.orderNumber, PreviousStatus: 'Ready', NewStatus: 'Delivered' });
  }
  auditLogs.sort((a, b) => b.timestamp.localeCompare(a.timestamp));

  // ---------------------------------------------------------------- AI agents
  const aiAgents = [
    {
      id: uuid('agent:pedidos'), name: 'Esquinita', role: 'Asistente de pedidos por WhatsApp', personality: 'Amable, cálida y breve. Usa un español mexicano neutro.',
      language: 'Espanol', tone: 'Amigable', businessName: 'Café La Esquina',
      businessDescription: 'Cafetería de especialidad en la Roma Norte. Desayunos, tortas y pan dulce. También damos mantenimiento a cafeteras y talleres de barismo.',
      businessProductsAndServices: 'Café americano $35, Latte $55, Capuchino $50, Frappé moka $65, Torta de jamón $70, Chilaquiles verdes $85 (8:00 a 12:30). Mantenimiento de cafetera espresso $450. Taller de barismo básico $650.',
      welcomeMessage: '¡Hola! Soy Esquinita, la asistente de Café La Esquina. ¿Quieres hacer un pedido para recoger o agendar un servicio?',
      fallbackMessage: 'Perdón, no entendí bien. ¿Me lo puedes repetir con otras palabras? Si prefieres, te comunico con una persona del equipo.',
      closingMessage: '¡Gracias por tu pedido! Te avisamos por aquí cuando esté listo.',
      askForName: true, askForPhone: true, askForEmail: false, askForAddress: false,
      customQuestions: '¿Es para llevar o para comer aquí?\n¿A qué hora pasas a recoger?',
      model: 'gpt-4.1-mini', temperature: 0.4, maxTokens: 600, useConversationMemory: true, strictMode: true, isActive: true,
      createdAt: '2026-06-10T17:00:00.000Z', updatedAt: at(18, 30, -3),
    },
    {
      id: uuid('agent:eventos'), name: 'Agente de eventos', role: 'Cotizaciones de terraza y coffee breaks', personality: 'Profesional y detallista.',
      language: 'Espanol', tone: 'Formal', businessName: 'Café La Esquina', businessDescription: 'Renta de terraza para eventos privados y coffee breaks para oficinas.',
      businessProductsAndServices: 'Renta de terraza $600 por hora (16:00 a 22:00). Coffee break para 10 personas $1,200.',
      welcomeMessage: 'Hola, gracias por escribir a Café La Esquina. ¿Para qué fecha y cuántas personas sería tu evento?',
      fallbackMessage: 'Gracias, un asesor te contactará para completar la cotización.',
      closingMessage: 'Te enviaremos la cotización por este medio.',
      askForName: true, askForPhone: true, askForEmail: true, askForAddress: false, customQuestions: '',
      model: 'gpt-4.1-mini', temperature: 0.3, maxTokens: 500, useConversationMemory: true, strictMode: false, isActive: false,
      createdAt: '2026-08-02T17:00:00.000Z',
    },
  ];

  // ---------------------------------------------------------------- WhatsApp
  const whatsAppConfig = {
    id: uuid('wa:config'), tenantId: TENANT_ID, isEnabled: true, phoneNumberId: '109876543210987', wabaId: '102345678901234',
    displayPhoneNumber: '+52 55 5264 1180', hasAccessToken: true, verifyToken: 'fpd_verify_8f3a2c71',
  };
  const conv = (key, name, phone, last, lastAt, unread) => ({ id: uuid(`conv:${key}`), customerPhone: phone, customerName: name, lastMessageBody: last, lastMessageAt: lastAt, unreadCount: unread });
  const conversations = [
    conv('mafer', 'María Fernanda López', '+5215541238890', '¿Ya está lista mi orden para llevar?', at(13, 19), 2),
    conv('valeria', 'Valeria Morales', '+5215522107765', 'Perfecto, paso a recoger a las 13:45', at(13, 22), 0),
    conv('unknown', undefined, '+5215587761234', 'Hola, ¿tienen servicio a domicilio?', at(12, 31), 1),
    conv('roberto', 'Roberto Sánchez Vega', '+5215599210087', 'Gracias, quedo pendiente de la cafetera.', at(12, 14), 0),
    conv('altavista', 'Oficinas Grupo Altavista', '+5215552089100', 'Recibido, gracias. Mañana repetimos el pedido.', at(12, 48), 0),
    conv('daniela', 'Daniela Torres', '+5215514576630', 'Somos 6 para el taller de las 4', at(11, 2), 0),
    conv('luis', 'Luis Ángel Martínez', '+5215533487712', 'Va, lo reagendamos para el martes.', at(19, 40, -1), 0),
    conv('ana', 'Ana Patricia Gómez', '+5215570124456', 'Ok, gracias por avisar.', at(17, 5, -2), 0),
  ].sort((a, b) => b.lastMessageAt.localeCompare(a.lastMessageAt));
  const msg = (convKey, idx, direction, body, sentAt, status) => ({ id: uuid(`msg:${convKey}:${idx}`), conversationId: uuid(`conv:${convKey}`), direction, body, status, sentAt });
  const messagesByConversation = {
    [uuid('conv:mafer')]: [
      msg('mafer', 1, 'Inbound', 'Hola, buenas tardes. ¿Me pueden preparar 2 tortas de jamón para llevar?', at(12, 49), 'Read'),
      msg('mafer', 2, 'Outbound', '¡Hola, María Fernanda! Claro que sí. ¿Les agregamos algo de tomar?', at(12, 50), 'Read'),
      msg('mafer', 3, 'Inbound', 'Dos aguas de jamaica, por favor. Y una de las tortas sin chipotle.', at(12, 52), 'Read'),
      msg('mafer', 4, 'Outbound', 'Listo: 2 tortas de jamón (una sin chipotle) y 2 aguas de jamaica. Total $232.00. Tu orden es la ORD-000345.', at(12, 55), 'Read'),
      msg('mafer', 5, 'Inbound', 'Perfecto, paso en 20 minutos.', at(12, 56), 'Read'),
      msg('mafer', 6, 'Outbound', 'Te esperamos. Te aviso por aquí cuando esté lista.', at(12, 57), 'Delivered'),
      msg('mafer', 7, 'Inbound', 'Ya voy para allá', at(13, 17), 'Read'),
      msg('mafer', 8, 'Inbound', '¿Ya está lista mi orden para llevar?', at(13, 19), 'Read'),
    ],
    [uuid('conv:valeria')]: [
      msg('valeria', 1, 'Inbound', 'Hola, quiero 2 lattes y un croissant para recoger', at(13, 20), 'Read'),
      msg('valeria', 2, 'Outbound', 'Con gusto, Valeria. Tu orden es la ORD-000348. ¿A qué hora pasas?', at(13, 21), 'Read'),
      msg('valeria', 3, 'Inbound', 'Perfecto, paso a recoger a las 13:45', at(13, 22), 'Read'),
    ],
    [uuid('conv:unknown')]: [
      msg('unknown', 1, 'Inbound', 'Hola, ¿tienen servicio a domicilio?', at(12, 31), 'Read'),
    ],
    [uuid('conv:roberto')]: [
      msg('roberto', 1, 'Outbound', 'Roberto, recibimos tu cafetera Oster. La tendremos lista mañana a las 6 pm.', at(12, 10), 'Read'),
      msg('roberto', 2, 'Inbound', 'Gracias, quedo pendiente de la cafetera.', at(12, 14), 'Read'),
    ],
  };
  // The API returns messages newest first; the page reverses them.
  Object.keys(messagesByConversation).forEach((k) => messagesByConversation[k].sort((a, b) => b.sentAt.localeCompare(a.sentAt)));

  // ---------------------------------------------------------------- reports (current month)
  const report = {
    rangeFrom: '2026-10-01T06:00:00.000Z', rangeTo: '2026-11-01T05:59:59.999Z',
    totalRevenue: 24836.4, totalTax: 3425.71, netRevenue: 21410.69, totalOrderPayments: 24112.0, totalPurchases: 8946.64,
    totalCogs: 7385.2, grossProfit: 14025.49, ordersCompleted: 112, appointmentsCompleted: 3, appointmentsScheduled: 9,
    revenueByPaymentMethod: { Cash: 13240.5, Card: 8671.5, Transfer: 2200.0 },
  };

  // Mirrors Application/Catalog/Templates/CategoryTemplateCatalog.cs (recommended: "food" for industry "Alimentos y Bebidas").
  const tpl = (id, name, description, businessType, items, isRecommended = false) => ({
    id, name, description, businessType, isRecommended, categoryCount: items.length,
    categories: items.map((n, i) => ({ name: n, sortOrder: (i + 1) * 10 })),
  });
  const categoryTemplates = [
    tpl('food', 'Alimentos', 'Categorias comunes para restaurantes, cafeterias y dark kitchens.', 0, ['Desayunos', 'Comidas', 'Cenas', 'Snacks', 'Bebidas', 'Postres', 'Promociones'], true),
    tpl('retail', 'Retail', 'Categorias base para tienda fisica o ecommerce con inventario.', 1, ['Novedades', 'Mas vendidos', 'Temporada', 'Accesorios', 'Limpieza y cuidado', 'Promociones']),
    tpl('services', 'Servicios', 'Categorias generales para negocios orientados a servicios.', 2, ['Servicios basicos', 'Servicios premium', 'Express', 'A domicilio', 'Membresias', 'Promociones']),
    tpl('laundry', 'Lavanderia de calzado', 'Plantilla especializada para limpieza y restauracion de tenis/calzado.', 2, ['Lavado basico', 'Lavado premium', 'Restauracion', 'Blanqueamiento', 'Impermeabilizado', 'Productos de cuidado']),
    tpl('barber', 'Barberia', 'Plantilla para barberias y salones con enfoque en citas.', 2, ['Corte', 'Barba', 'Paquetes', 'Tratamientos', 'Productos', 'Promociones']),
  ];

  const setup = {
    businessType: 0,
    capabilities: 63,
    pricesIncludeTax: true,
    defaultTaxRate: 16,
    completedAt: tenant.onboardingCompletedAt,
    hasProducts: true,
    steps: [
      { key: 'salesPolicy', done: true },
      { key: 'catalog', done: true },
      { key: 'cash', done: true },
      { key: 'firstSale', done: true },
      { key: 'team', done: false },
      { key: 'logo', done: false },
    ],
    starterCatalogs: [
      { id: 'cafe', name: 'Cafetería', description: 'Bebidas calientes y frías, panadería y desayunos.', businessType: 0, productCount: 14, categories: ['Bebidas calientes', 'Bebidas frías', 'Panadería', 'Desayunos'] },
      { id: 'fondita', name: 'Fonda o cocina económica', description: 'Comida corrida, guisados, antojitos y aguas frescas.', businessType: 0, productCount: 12, categories: ['Comida corrida', 'Antojitos', 'Bebidas'] },
      { id: 'tienda', name: 'Tienda de abarrotes', description: 'Refrescos, botanas, abarrotes y limpieza.', businessType: 1, productCount: 15, categories: ['Bebidas', 'Botanas', 'Abarrotes', 'Limpieza'] },
      { id: 'barberia', name: 'Barbería o estética', description: 'Cortes, barba, tratamientos y productos.', businessType: 2, productCount: 9, categories: ['Cortes', 'Barba', 'Productos'] },
    ],
  };

  // ---------------------------------------------------------------- special days (SpecialDaysDto, today 2026-10-04)
  // Kind as the contract numbers it: 0 closed, 1 other hours. The suggestions are the holidays of the next 90 days that
  // are not special days yet; Fin de año closes at 18:00 from the Thursday hours of the business profile (7:00 to 21:00).
  const specialDay = (key, from, to, kind, openings, reason, revision = 1) => ({
    id: uuid(`special:${key}`), from, to, kind, openings, reason, revision, inProgress: false,
  });
  const specialDays = {
    today: '2026-10-04',
    hasWeeklyHours: true,
    upcoming: [
      specialDay('revolucion', '2026-11-16', '2026-11-16', 0, [], 'Día de la Revolución'),
      specialDay('nochebuena', '2026-12-24', '2026-12-24', 1, [{ opens: '09:00', closes: '18:00' }], 'Nochebuena'),
      specialDay('navidad', '2026-12-25', '2026-12-25', 0, [], 'Navidad', 2),
    ],
    past: [specialDay('independencia', '2026-09-16', '2026-09-16', 0, [], 'Día de la Independencia')],
    suggestions: [
      { date: '2026-11-02', name: 'Día de Muertos', statutory: false, kind: 0, openings: [] },
      { date: '2026-12-12', name: 'Día de la Virgen de Guadalupe', statutory: false, kind: 0, openings: [] },
      { date: '2026-12-31', name: 'Fin de año', statutory: false, kind: 1, openings: [{ opens: '07:00', closes: '18:00' }] },
      { date: '2027-01-01', name: 'Año Nuevo', statutory: true, kind: 0, openings: [] },
    ],
    upcomingDates: 3,
    maxUpcomingDates: 120,
    maxDaysPerPeriod: 92,
    maxDaysAhead: 366,
    maxReasonLength: 40,
  };

  // The prices set in the app of each delivery platform, by channel: `{ products: { [productId]: price }, options:
  // { [optionId]: delta } }`. The demo set none: its orders of a platform would charge the menu's.
  const platformPrices = {};

  return {
    setup, tenant, profile, myTenants, categories, products, customers: customerList, locations, orders, dashboard, kitchenOrders,
    boardPieces, cashSession, cashTx, dailyCut, supplies, stockRows, movements, kardex, alerts, recipes, suppliers,
    purchaseOrders, appointments, employees, permissionOptions, auditLogs, aiAgents, whatsAppConfig, conversations,
    messagesByConversation, report, categoryTemplates, specialDays, platformPrices, USER_ID, TENANT_ID, OWNER_EMAIL,
  };
}

/**
 * What the demo business charges in the Rappi app (Catálogo › Precios en apps), for the scenes that show it: more than
 * in its menu for the americano ($35 there), the croissant ($38) and the latte ($55), and for a large one (+$12) with
 * almond milk (+$8; the latte's options are seeded with routes-p3). The rest charges the menu's.
 */
export function withRappiPrices(data) {
  const product = (name) => data.products.find((candidate) => candidate.name === name).id;
  data.platformPrices[3] = {
    products: { [product('Café americano')]: 42, [product('Croissant de mantequilla')]: 45, [product('Latte')]: 66 },
    options: { 'o-gr': 15, 'o-alm': 10 },
  };
}
