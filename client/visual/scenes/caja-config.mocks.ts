import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { at } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

// CashSessionStatus, CashTransactionType and OrderPaymentMethod as the contract numbers them.
const OPEN = 0;
const CLOSED = 1;
const SALE = 0;
const EXPENSE = 2;
const WITHDRAWAL = 3;
const DEPOSIT = 4;
const CASH = 0;
const CARD = 1;

const CARLOS = 'carlos.mendez@cafelaesquina.mx';
const DIEGO = 'diego.herrera@cafelaesquina.mx';

interface CountLine {
  denomination: number;
  quantity: number;
}

interface Movement {
  id: string;
  cashSessionId: string;
  transactionType: number;
  amount: number;
  occurredAt: string;
  paymentMethod?: number;
  inventoryLocationId: string;
  inventoryLocationName: string;
  reference?: string;
  notes?: string;
  createdBy: string;
}

interface Session {
  id: string;
  openedAt: string;
  openedBy: string;
  openedByName?: string | null;
  openingAmount: number;
  status: number;
  inflows: number;
  outflows: number;
  nonCashInflows: number;
  expectedBalance: number;
  isMine: boolean;
  branchId: string;
  branchName: string;
  inventoryLocationId: string;
  inventoryLocationName: string;
  cashCount: CountLine[];
  closedAt?: string;
  closedBy?: string;
  closedByName?: string | null;
  closingAmount?: number;
  expectedClosingAmount?: number;
  closingDifference?: number;
  closingNotes?: string;
  notes?: string;
}

interface Branch {
  id: string;
  name: string;
}

/** The name of a member of the business by their user (email), as the API gives it; null for anyone else. */
type NameOf = (user: string) => string | null;

function namesOf(data: DemoData): NameOf {
  const names = new Map<string, string>(
    (data.employees as Array<{ email: string; firstName: string; lastName: string }>)
      .map((member) => [member.email, `${member.firstName} ${member.lastName}`]),
  );
  return (user) => names.get(user) ?? null;
}

/** The cash a count adds up to, in cents (coins of $0.50 included). */
function countTotal(lines: CountLine[]) {
  return lines.reduce((sum, line) => sum + Math.round(line.denomination * 100) * line.quantity, 0) / 100;
}

/**
 * Yesterday's closed drawers and the one Carlos opened today at Coyoacán, beside the owner's own drawer of the demo
 * data. Each closed one tells a different ending: short with a note and the count by denomination, over by a little,
 * and exact with only the total typed.
 */
function shiftHistory(today: Session | null, roma: Branch, coyoacan: Branch, nameOf: NameOf) {
  const place = (branch: Branch) => ({
    branchId: branch.id, branchName: branch.name, inventoryLocationId: branch.id, inventoryLocationName: branch.name,
  });
  const closed = (id: string, openedBy: string, branch: Branch, openedAt: string, closedAt: string, amounts: {
    opening: number; inflows: number; outflows: number; nonCash: number; count: CountLine[]; counted?: number; notes?: string;
  }): Session => {
    const expected = amounts.opening + amounts.inflows - amounts.outflows;
    const counted = amounts.counted ?? countTotal(amounts.count);
    return {
      id, openedAt, openedBy, openedByName: nameOf(openedBy), openingAmount: amounts.opening, status: CLOSED, inflows: amounts.inflows,
      outflows: amounts.outflows, nonCashInflows: amounts.nonCash, expectedBalance: expected, isMine: openedBy === today?.openedBy,
      ...place(branch), cashCount: amounts.count, closedAt, closedBy: openedBy, closedByName: nameOf(openedBy), closingAmount: counted,
      expectedClosingAmount: expected, closingDifference: Math.round((counted - expected) * 100) / 100, closingNotes: amounts.notes,
    };
  };

  const sessions: Session[] = [
    {
      id: 'turno-coyoacan-hoy', openedAt: at(8, 0), openedBy: CARLOS, openedByName: nameOf(CARLOS), openingAmount: 800, status: OPEN, inflows: 1240.5,
      outflows: 150, nonCashInflows: 960, expectedBalance: 1890.5, isMine: false, ...place(coyoacan), cashCount: [],
      notes: 'Fondo de la barra',
    },
    ...(today ? [today] : []),
    closed('turno-roma-ayer-tarde', today?.openedBy ?? 'mariana.ortiz@cafelaesquina.mx', roma, at(15, 0, -1), at(21, 10, -1), {
      opening: 1500, inflows: 4870, outflows: 300, nonCash: 3920, notes: 'Propina en efectivo que no se registró',
      count: [{ denomination: 500, quantity: 10 }, { denomination: 200, quantity: 4 }, { denomination: 100, quantity: 2 },
        { denomination: 50, quantity: 1 }, { denomination: 20, quantity: 2 }],
    }),
    closed('turno-coyoacan-ayer', DIEGO, coyoacan, at(8, 0, -1), at(14, 30, -1), {
      opening: 800, inflows: 1985, outflows: 0, nonCash: 1210, count: [], counted: 2785,
    }),
    closed('turno-roma-ayer-manana', CARLOS, roma, at(7, 0, -1), at(15, 0, -1), {
      opening: 1500, inflows: 3260, outflows: 180, nonCash: 2140, notes: 'Faltaron $50: un cambio mal dado',
      count: [{ denomination: 500, quantity: 6 }, { denomination: 200, quantity: 5 }, { denomination: 100, quantity: 4 },
        { denomination: 50, quantity: 2 }, { denomination: 20, quantity: 1 }, { denomination: 10, quantity: 1 }],
    }),
  ];

  const movement = (session: Session, index: number, type: number, amount: number, occurredAt: string, extra: Partial<Movement> = {}): Movement => ({
    id: `${session.id}-mov-${index}`, cashSessionId: session.id, transactionType: type, amount, occurredAt,
    inventoryLocationId: session.inventoryLocationId, inventoryLocationName: session.inventoryLocationName, createdBy: session.openedBy, ...extra,
  });
  const byId = Object.fromEntries(sessions.map((session) => [session.id, session]));
  const morning = byId['turno-roma-ayer-manana'];
  const coyToday = byId['turno-coyoacan-hoy'];
  const movements: Record<string, Movement[]> = {
    [morning.id]: [
      movement(morning, 1, SALE, 232, at(14, 40, -1), { paymentMethod: CARD, reference: 'ORD-000316' }),
      movement(morning, 2, SALE, 185.6, at(12, 15, -1), { paymentMethod: CASH, reference: 'ORD-000309' }),
      movement(morning, 3, EXPENSE, -180, at(10, 30, -1), { reference: 'Hielo (6 bolsas)', notes: 'Pago a proveedor de hielo' }),
      movement(morning, 4, SALE, 98.6, at(8, 20, -1), { paymentMethod: CASH, reference: 'ORD-000302' }),
      movement(morning, 5, DEPOSIT, 300, at(7, 10, -1), { reference: 'Cambio', notes: 'Monedas para cambio' }),
    ],
    [coyToday.id]: [
      movement(coyToday, 1, WITHDRAWAL, -150, at(12, 0), { reference: 'Pago de pan', notes: 'Panificadora La Espiga' }),
      movement(coyToday, 2, SALE, 81.2, at(9, 5), { paymentMethod: CASH, reference: 'ORD-000329' }),
    ],
  };
  return { sessions, movements };
}

/**
 * Caja: the shift history and the movements of each drawer. The shared routes answer one list for every drawer (and a
 * history from another business); these answer by drawer, so a closed shift shows its own movements and its cut.
 */
const cashRoutes: ExtraRoutes = (data, { ok, paginate }) => {
  const today: Session | null = data.cashSession;
  const [roma, coyoacan] = data.locations as Branch[];
  const { sessions, movements } = shiftHistory(today, roma, coyoacan, namesOf(data));
  const todayId = today?.id;
  return [
    ['GET', /^\/cash\/sessions$/, (_match, query) => {
      let rows = sessions;
      const from = query.get('fromDate');
      const to = query.get('toDate');
      if (from) rows = rows.filter((session) => session.openedAt >= from);
      if (to) rows = rows.filter((session) => session.openedAt <= to);
      if (query.get('branchId')) rows = rows.filter((session) => session.branchId === query.get('branchId'));
      if (query.get('onlyWithDifference') === 'true') rows = rows.filter((session) => !!session.closingDifference);
      return ok(paginate(rows, query, 20));
    }],
    ['GET', /^\/cash\/transactions$/, (_match, query) => {
      const sessionId = query.get('cashSessionId');
      // Today's drawer (also once it is closed in the scene) moves what the demo data recorded today.
      let rows: Movement[] = sessionId && sessionId !== todayId ? movements[sessionId] ?? [] : data.cashTx;
      const search = (query.get('search') ?? '').toLowerCase();
      if (search) {
        rows = rows.filter((row) => [row.reference, row.notes, row.createdBy].some((value) => (value ?? '').toLowerCase().includes(search)));
      }
      return ok(paginate(rows, query, 10));
    }],
    // A platform admin belongs to no business: the platform console is theirs, not the business's.
    ['GET', /^\/me\/access$/, () => (data.profile.role === SUPER_ADMIN_PROFILE.role
      ? ok({ userId: data.USER_ID, role: 'SUPER_ADMIN', tenantRole: null, tenantId: data.TENANT_ID, isMember: false, isTenantManager: true,
        isPlatformAdmin: true, permissions: [], capabilities: data.tenant.capabilities })
      : undefined)],
  ];
};

/**
 * The fields the contract requires that the demo's drawer and day cut leave out (CashSessionDto.isMine, cashCount and
 * its branch; the tips and cash-only figures of DailyCashCutDto), and the names of the people the API gives with their
 * users. Without them the drawer reads as somebody else's. The day had tips: $120.00, $70.00 of them in cash, which the
 * cash-only net includes.
 */
function completeCashData(data: DemoData) {
  const [roma] = data.locations as Branch[];
  const nameOf = namesOf(data);
  if (data.cashSession) {
    Object.assign(data.cashSession, {
      isMine: true, cashCount: [], branchId: roma.id, branchName: roma.name, inventoryLocationId: roma.id, inventoryLocationName: roma.name,
      openedByName: nameOf(data.cashSession.openedBy),
    });
  }
  const cut = data.dailyCut;
  for (const user of cut.byUser as Array<{ userName: string; displayName?: string | null }>) user.displayName = nameOf(user.userName);
  Object.assign(cut, {
    tipsTotal: 120,
    cashTipsTotal: 70,
    cashManualIncomeTotal: cut.manualIncomeTotal,
    cashManualExpenseTotal: cut.manualExpenseTotal,
    netCashOnlyFlow: Math.round((cut.netCashOnlyFlow + 70) * 100) / 100,
  });
}

/** Nobody has opened their drawer yet: Mi caja asks for the opening float. */
function withoutDrawer(data: DemoData) {
  completeCashData(data);
  data.cashSession = null;
}

/**
 * Mi negocio as a business that has filled it in: its hours (two openings on Saturday, closed on Sunday) and its logo.
 * The logo is the demo picture (/uploads would answer 404).
 */
function businessProfile(data: DemoData) {
  completeCashData(data);
  const weekday = [1, 2, 3, 4, 5].map((day) => ({ day, opens: '07:00', closes: '21:00' }));
  data.tenant.openingHours = [...weekday, { day: 6, opens: '08:00', closes: '14:00' }, { day: 6, opens: '16:00', closes: '22:00' }];
  data.tenant.timeZone = 'America/Mexico_City';
  const logo = '/visual-img/logo.svg';
  data.tenant.logoUrl = logo;
  data.tenant.logoImages = { original: logo, large: logo, medium: logo, small: logo, thumbnail: logo };
}

/** Mi negocio with the online menu taking orders: its link, its QR and one QR per table. */
function businessWithOnlineMenu(data: DemoData) {
  businessProfile(data);
  data.tenant.onlineMenuMode = 2;
}

export const CAJA_ROUTES = asOption(cashRoutes);
export const CAJA_DATA = asOption(completeCashData);
export const SIN_CAJA_DATA = asOption(withoutDrawer);
export const NEGOCIO_DATA = asOption(businessProfile);
export const MENU_EN_LINEA_DATA = asOption(businessWithOnlineMenu);

/** The platform admin (TienditApp's own team): no role in the business, the platform console. */
export const SUPER_ADMIN_PROFILE = {
  firstName: 'Equipo',
  lastName: 'TienditApp',
  fullName: 'Equipo TienditApp',
  email: 'soporte@tienditapp.mx',
  role: 'SuperAdmin',
  tenantRole: null,
  permissions: [],
};

/** A cashier (the Cajero preset): sells, charges and runs a drawer, but does not configure the business. */
export const CASHIER_PROFILE = {
  firstName: 'Carlos',
  lastName: 'Méndez',
  fullName: 'Carlos Méndez',
  email: CARLOS,
  role: 'Staff',
  tenantRole: 'Staff',
  permissions: ['ManageOrders', 'ChargeOrders', 'ManageCash', 'ManageCustomers', 'ManageWhatsApp', 'ManageAppointments'],
};
