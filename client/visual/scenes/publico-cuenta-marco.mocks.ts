import { asOption, type DemoData, type SceneOptions } from '../fixtures';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;
type PrepareData = NonNullable<SceneOptions['prepareData']>;

// DayOfWeek, SpecialDayKind, TenantStatus and SubscriptionStatus as the contract numbers them.
const SUNDAY = 0;
const SATURDAY = 6;
const CLOSED_DAY = 0;
const TENANT_SUSPENDED = 2;
const SUBSCRIPTION_SUSPENDED = 3;
// The fixed clock of the scenes in the business's time (Sunday 2026-10-04).
const NOW_LOCAL = '13:25:00';

interface OpeningInterval {
  day: number;
  opens: string;
  closes: string;
}

interface DemoProduct {
  id: string;
  categoryId: string;
  name: string;
  description?: string;
  priceAmount: number;
  compareAtPrice?: number;
  taxRate: number;
  isTaxExempt: boolean;
  unit: number;
  isAvailable: boolean;
  isFeatured: boolean;
  sortOrder: number;
  allergens?: string;
  availableFromTime?: string;
  availableToTime?: string;
  optionGroups?: unknown[];
  _itemType: string;
}

// Monday to Friday 8:00 to 20:00 and Saturday with a break, which the week list writes as two openings.
const WEEKDAYS: OpeningInterval[] = [
  ...[1, 2, 3, 4, 5].map((day) => ({ day, opens: '08:00', closes: '20:00' })),
  { day: SATURDAY, opens: '08:00', closes: '14:00' },
  { day: SATURDAY, opens: '16:00', closes: '21:00' },
];

/**
 * Whether the business is open now, as GetPublicMenuQuery works it out in the business's time: open on Sunday until
 * 15:00, or closed because on Sunday it closes at 13:00 (it opens again tomorrow at 8:00).
 */
function openingHours(closed: boolean) {
  return closed
    ? { intervals: [{ day: SUNDAY, opens: '09:00', closes: '13:00' }, ...WEEKDAYS], today: SUNDAY, isOpen: false, opensAt: '08:00', opensInDays: 1 }
    : { intervals: [{ day: SUNDAY, opens: '09:00', closes: '15:00' }, ...WEEKDAYS], today: SUNDAY, isOpen: true, closesAt: '15:00', closesInDays: 0 };
}

/**
 * Today (Sunday 2026-10-04) is a special day, as GetPublicMenuQuery words it since the Fase 21 (todayDate present, the
 * texts from BusinessHoursText): closed all day for inventory, opening again tomorrow at 8:00; or open from 10:00 to
 * 14:00 for inventory, with the anniversary closing on Friday announced.
 */
function specialDayHours(kind: SpecialDayScene) {
  const week = { intervals: [{ day: SUNDAY, opens: '09:00', closes: '15:00' }, ...WEEKDAYS], today: SUNDAY, todayDate: '2026-10-04', hasWeeklyHours: true };
  return kind === 'cierre'
    ? {
      ...week,
      isOpen: false,
      opensAt: '08:00',
      opensInDays: 1,
      opensOn: '2026-10-05',
      statusLabel: 'Cerrado',
      statusDetail: 'por Inventario · abre mañana a las 8:00',
      closedNotice: 'No recibimos pedidos en línea por Inventario: vuelve mañana a las 8:00. Mientras, puedes ver el menú.',
      upcomingNotice: null,
      closedBySpecialDay: true,
      todayHours: { date: '2026-10-04', closed: true, special: true, openings: [], reason: 'Inventario' },
      specialDays: [{ from: '2026-11-02', to: '2026-11-02', kind: CLOSED_DAY, openings: [], reason: 'Día de Muertos' }],
    }
    : {
      ...week,
      isOpen: true,
      closesAt: '14:00',
      closesInDays: 0,
      closesOn: '2026-10-04',
      statusLabel: 'Abierto',
      statusDetail: 'cierra a las 14:00 por Inventario',
      closedNotice: null,
      upcomingNotice: 'El viernes 9 cerramos por Aniversario.',
      closedBySpecialDay: false,
      todayHours: { date: '2026-10-04', closed: false, special: true, openings: [{ opens: '10:00', closes: '14:00' }], reason: 'Inventario' },
      specialDays: [{ from: '2026-10-09', to: '2026-10-09', kind: CLOSED_DAY, openings: [], reason: 'Aniversario' }],
    };
}

type SpecialDayScene = 'cierre' | 'horario';

/** An error as the API answers it (ApiProblem): ProblemDetails with its code and the detail repeated in `error`. */
function problem(status: number, code: string, title: string, detail: string) {
  return { status, json: { status, title, detail, code, error: detail, traceId: '00-visual-00' } };
}

/** The address of the default branch in one line, as AddressText.OneLine(Branch) writes it. */
function mainBranchAddress(data: DemoData): string {
  const branch = data.locations.find((location: { isDefault: boolean }) => location.isDefault);
  return [branch.addressLine1, branch.addressLine2, `${branch.postalCode} ${branch.city}`, branch.state].filter(Boolean).join(', ');
}

/**
 * The public menu (PublicMenuDto). The shared route has no hours, address or `ordersPaused`; this one answers like
 * GetPublicMenuQuery: featured products first, services left out, an unknown slug is a 404 and, closed by its hours,
 * the business takes no online orders unless the QR is of one of its tables in use (not on a special day it closes).
 */
function publicMenu(data: DemoData, slug: string, table: string | null) {
  if (slug !== data.tenant.slug) return problem(404, 'not_found', 'Not Found', 'Este negocio no tiene menú en línea.');
  const specialDay = data.menuSpecialDay as SpecialDayScene | undefined;
  const hours = specialDay ? specialDayHours(specialDay) : openingHours(!!data.menuClosed);
  const seated = !!table && data.p3.room.tables.some((t: { name: string; isActive: boolean }) => t.isActive && t.name === table.trim());
  const acceptsOrders = hours.isOpen || (seated && specialDay !== 'cierre');
  const products = (data.products as DemoProduct[])
    .filter((p) => p.isAvailable && p._itemType === 'Product')
    .sort((a, b) => Number(b.isFeatured) - Number(a.isFeatured) || a.sortOrder - b.sortOrder);
  const availableNow = (p: DemoProduct) => !p.availableFromTime || !p.availableToTime
    || (p.availableFromTime <= NOW_LOCAL && NOW_LOCAL <= p.availableToTime);
  const categories = data.categories
    .filter((category: { isActive: boolean }) => category.isActive)
    .map((category: { id: string; name: string }) => ({
      id: category.id,
      name: category.name,
      products: products.filter((p) => p.categoryId === category.id).map((p) => ({
        id: p.id, name: p.name, description: p.description ?? null, price: p.priceAmount, compareAtPrice: p.compareAtPrice ?? null,
        taxRate: p.taxRate, isTaxExempt: p.isTaxExempt, imageUrl: null, unit: p.unit, isFeatured: p.isFeatured,
        availableNow: availableNow(p), allergens: p.allergens ?? null, optionGroups: p.optionGroups ?? [],
      })),
    }))
    .filter((category: { products: unknown[] }) => category.products.length > 0);
  return {
    status: 200,
    json: {
      businessName: data.tenant.name, slug: data.tenant.slug, logoUrl: null, contactPhone: data.tenant.contactPhone,
      currency: 'MXN', pricesIncludeTax: data.tenant.pricesIncludeTax, defaultTaxRate: data.tenant.defaultTaxRate,
      acceptsOrders, ordersPaused: !acceptsOrders, theme: data.tenant.theme, address: mainBranchAddress(data),
      openingHours: hours, categories,
    },
  };
}

/** The menu closed by its hours. */
const menuClosed: PrepareData = (data) => {
  data.menuClosed = true;
};

/** Today the business closes for inventory: a special day. */
const menuSpecialClosing: PrepareData = (data) => {
  data.menuSpecialDay = 'cierre';
};

/** Today the business opens with special hours for inventory, and closes on Friday for its anniversary. */
const menuSpecialHours: PrepareData = (data) => {
  data.menuSpecialDay = 'horario';
};

/** A business suspended for billing: the frame shows SuspendedNotice instead of the screen. */
const suspendedBusiness: PrepareData = (data) => {
  data.tenant.status = TENANT_SUSPENDED;
  data.suspended = true;
};

/** Inicio cannot load its figures: the API fails with a 500. */
const failingMetrics: PrepareData = (data) => {
  data.metricsFail = true;
};

/** An account with two-step verification: the login asks for the code instead of opening the session. */
const twoFactorAccount: PrepareData = (data) => {
  data.twoFactor = true;
};

/** The public menu, sign-in and recovery answers, and the edge states of the frame (each off unless a scene sets it). */
const publicRoutes: ExtraRoutes = (data, { ok }) => [
  ['GET', /^\/public\/menu\/([^/]+)$/, (match, query) => publicMenu(data, decodeURIComponent(match[1]), query.get('table'))],
  ['POST', /^\/auth\/login$/, () => (data.twoFactor
    ? ok({ requiresTwoFactor: true, twoFactorChallengeToken: 'desafio-visual', userId: data.USER_ID })
    : undefined)],
  ['POST', /^\/auth\/forgot-password$/, () => ok({ message: 'Si el correo está registrado, te enviamos un enlace.' })],
  // A link whose token is "vencido" expired; any other confirms the account.
  ['GET', /^\/auth\/confirm-email$/, (_match, query) => (query.get('token') === 'vencido'
    ? problem(400, 'business_rule', 'Bad Request', 'El enlace ya venció. Pide uno nuevo con tu correo.')
    : ok({ message: 'Correo confirmado.' }))],
  ['GET', /^\/dashboard\/metrics$/, () => (data.metricsFail
    ? problem(500, 'server_error', 'Internal Server Error', 'Ocurrió un error inesperado.')
    : undefined)],
  ['GET', /^\/auth\/my-tenant\/subscription$/, () => (data.suspended
    ? ok({
      hasPlan: true,
      plan: {
        id: 'plan-crece', code: 'CRECE', name: 'Crece', description: null, monthlyPrice: 549, currency: 'MXN', modules: 63,
        maxBranches: 3, maxUsers: 10, trialDays: 14, isDefaultForNewBusinesses: false, isActive: true, sortOrder: 0, tenantsCount: 9,
      },
      status: SUBSCRIPTION_SUSPENDED, startedAt: '2026-06-01T15:00:00Z', trialEndsAt: '2026-06-15T15:00:00Z',
      paidThrough: '2026-09-02T15:00:00Z', suspendsAt: '2026-09-09T15:00:00Z',
      statusReason: 'Sin pago después de los días de gracia.', suspendedByPlatform: false, branchesUsed: 2, usersUsed: 6, payments: [],
    })
    : undefined)],
];

export const PUBLICO_ROUTES = asOption(publicRoutes);
export const MENU_CERRADO = asOption(menuClosed);
export const MENU_CIERRE_ESPECIAL = asOption(menuSpecialClosing);
export const MENU_HORARIO_ESPECIAL = asOption(menuSpecialHours);
export const NEGOCIO_SUSPENDIDO = asOption(suspendedBusiness);
export const INICIO_CON_ERROR = asOption(failingMetrics);
export const CON_DOS_PASOS = asOption(twoFactorAccount);
