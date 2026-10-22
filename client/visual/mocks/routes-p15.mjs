// Phase 26A endpoints for the fake backend: el fiado (ADR 0033). Who owes the business (GET /customers/receivables), the
// account of a customer (GET /customers/account), a payment to it (POST /customers/account/payments) and how much they may
// owe (PUT /customers/{id}/credit-limit). What is owed comes from `data.orders` by the server's rule (Receivables): not
// cancelled, with a phone and a balance, and delivered or sold on credit, except what a courier still collects. The limit
// is `creditLimit` of the customer in `data.customers`; without one, nobody is sold on credit. Fase 26B: what the business
// forgave (`writtenOffAmount` and `writeOffs` of each order) is not owed, and with a term (`data.creditTermDays`) what is
// past it is overdue, by the business's calendar (America/Mexico_City, UTC-6). Fase 26C: a customer record's own term
// (`creditTermDays`, the shortest when two share the phone) replaces the business's, and with the reminders on
// (`data.creditReminders`, routes-p17) Por cobrar and the account say every how many days; `data.lastCreditReminders`
// keeps the last reminder sent to each phone key. Fase 26D: the balance in a customer's favor is the sum of
// `data.balanceEntries` of their phone key ({ id, phone, kind, amount (signed), method?, orderNumber?, reference?, notes?,
// occurredAt }); leaving it (POST .../balance/deposits) and giving it back (POST .../balance/returns) add one.
import { NOW_ISO } from './data.mjs';

// OrderStatus as the contract numbers it.
const DELIVERED = 6;
const COMPLETED = 7;
const CANCELLED = 99;
const RECENT_PAYMENTS = 10;

const r2 = (value) => Math.round(value * 100) / 100;
const RECENT_WRITE_OFFS = 10;
const RECENT_BALANCE_ENTRIES = 10;
// CustomerBalanceEntryKind as the contract numbers it.
const DEPOSIT = 0;
const APPLIED = 2;
const RETURNED = 3;
// OrderPaymentMethod.CustomerBalance as the contract numbers it.
const CUSTOMER_BALANCE = 7;
const TZ_OFFSET_MS = 6 * 3_600_000;

/** What is still owed of an order: its total minus what was paid and what the business forgave (Order.Balance). */
export const balanceOf = (order) => r2(order.total - order.paidAmount - (order.writtenOffAmount ?? 0));

/** The business's calendar date of an instant ("2026-10-04"). */
const businessDate = (iso) => new Date(new Date(iso).getTime() - TZ_OFFSET_MS).toISOString().slice(0, 10);

/** The term of a customer of their own (CreditTerm.OwnTerms): the shortest of the records with the phone key; else null. */
export function ownTermOf(data, key) {
  const terms = data.customers
    .filter((c) => !c.isPublicGeneral && c.creditTermDays != null && phoneKey(c.phone) === key)
    .map((c) => c.creditTermDays);
  return terms.length > 0 ? Math.min(...terms) : null;
}

/** The term the orders of a phone key fall due with: their own, else the business's. */
const termOf = (data, key) => ownTermOf(data, key) ?? data.creditTermDays ?? null;

/** Every how many days the reminders repeat (CreditReminders.EveryDaysAsync); null while they are off. */
export const reminderEveryDays = (data) => (data.creditReminders?.enabled ? data.creditReminders.everyDays : null);

/** How many days before the last day to pay the notice goes (CreditReminders.DueSoonDaysBeforeAsync); null while off. */
const dueSoonDaysBefore = (data) => (data.creditDueSoon?.enabled ? data.creditDueSoon.daysBefore : null);

/** The last day to pay an order with its customer's term (CreditTerm.DueOn); null without a term. */
function dueOn(data, order) {
  const term = termOf(data, phoneKey(order.customerPhone));
  if (term == null) return null;
  const day = new Date(`${businessDate(order.createdAt)}T12:00:00Z`);
  day.setUTCDate(day.getUTCDate() + term);
  return day.toISOString().slice(0, 10);
}

/** Its last day to pay has passed. */
export const isOverdue = (data, order) => {
  const due = dueOn(data, order);
  return due != null && due < businessDate(NOW_ISO);
};

/** The phone the orders are grouped by: +52 and the last ten digits, as the server keys them. */
export function phoneKey(phone) {
  const digits = String(phone ?? '').replace(/\D/g, '');
  return digits.length >= 10 ? `+52${digits.slice(-10)}` : null;
}

/** The server's rule of an order owed (Receivables.IsOwed). */
export function isOwed(order) {
  return order.status !== CANCELLED && !!phoneKey(order.customerPhone) && balanceOf(order) > 0
    && (!!order.soldOnCredit || order.status === DELIVERED || order.status === COMPLETED)
    && !(order.delivery?.cashToCollect != null && order.status === DELIVERED);
}

/** What the orders with a phone owe, as the customer card and the account add it up. */
export function owedBy(data, phone) {
  const key = phoneKey(phone);
  return r2(data.orders.filter((o) => isOwed(o) && phoneKey(o.customerPhone) === key).reduce((sum, o) => sum + balanceOf(o), 0));
}

/** The customer record of a phone: the active one first (CreditAccount.RecordAsync). */
export function recordOf(data, key) {
  const records = data.customers.filter((c) => !c.isPublicGeneral && phoneKey(c.phone) === key);
  return records.find((c) => c.isActive) ?? records.find((c) => c.creditLimit != null) ?? records[0] ?? null;
}

/** What a phone key has in its favor (CustomerBalances.OfAsync). */
export const balanceOfAccount = (data, key) =>
  r2((data.balanceEntries ?? []).filter((e) => e.phone === key).reduce((sum, e) => sum + e.amount, 0));

/** The business sells to them on credit now: an active customer record with a phone and a limit. */
export const sellsOnCredit = (record) => !!record && record.isActive && !!record.phone && record.creditLimit != null;

export function account(data, key, record) {
  const orders = data.orders
    .filter((o) => isOwed(o) && phoneKey(o.customerPhone) === key)
    .sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  const owed = r2(orders.reduce((sum, o) => sum + balanceOf(o), 0));
  const limit = record?.creditLimit ?? null;
  const payments = data.orders
    .filter((o) => phoneKey(o.customerPhone) === key)
    .flatMap((o) => (o.payments ?? []).map((p) => ({ orderNumber: o.orderNumber, paidAt: p.paidAt, amount: p.amount, method: p.method })))
    .sort((a, b) => b.paidAt.localeCompare(a.paidAt))
    .slice(0, RECENT_PAYMENTS);
  const writeOffs = data.orders
    .filter((o) => phoneKey(o.customerPhone) === key && o.status !== CANCELLED)
    .flatMap((o) => (o.writeOffs ?? []).map((w) => ({
      id: w.id, orderNumber: o.orderNumber, writtenOffAt: w.writtenOffAt, amount: w.amount, reason: w.reason, notes: w.notes ?? null,
      reversedAt: w.reversedAt ?? null,
    })))
    .sort((a, b) => b.writtenOffAt.localeCompare(a.writtenOffAt))
    .slice(0, RECENT_WRITE_OFFS);
  return {
    phone: key,
    name: record?.name ?? [...orders].reverse()[0]?.customerName ?? null,
    customerId: record?.id ?? null,
    creditLimit: limit,
    sellsOnCredit: sellsOnCredit(record),
    owed,
    available: limit == null ? null : r2(Math.max(0, limit - owed)),
    overLimit: limit != null && owed > limit,
    creditTermDays: termOf(data, key),
    ownCreditTermDays: ownTermOf(data, key),
    overdue: r2(orders.filter((o) => isOverdue(data, o)).reduce((sum, o) => sum + balanceOf(o), 0)),
    creditReminderEveryDays: reminderEveryDays(data),
    lastReminderAt: data.lastCreditReminders?.[key] ?? null,
    lastDueSoonAt: data.lastCreditDueSoon?.[key] ?? null,
    orders: orders.map((o) => ({
      orderId: o.id, orderNumber: o.orderNumber, createdAt: o.createdAt, status: o.status, soldOnCredit: !!o.soldOnCredit,
      total: o.total, paid: o.paidAmount, writtenOff: o.writtenOffAmount ?? 0, balance: balanceOf(o),
      dueOn: dueOn(data, o), isOverdue: isOverdue(data, o),
    })),
    ordersCount: orders.length,
    recentPayments: payments,
    recentWriteOffs: writeOffs,
    creditBalance: balanceOfAccount(data, key),
    recentBalanceEntries: (data.balanceEntries ?? [])
      .filter((e) => e.phone === key)
      .sort((a, b) => b.occurredAt.localeCompare(a.occurredAt))
      .slice(0, RECENT_BALANCE_ENTRIES)
      .map((e) => ({
        id: e.id, kind: e.kind, amount: e.amount, method: e.method ?? null, orderNumber: e.orderNumber ?? null,
        reference: e.reference ?? null, notes: e.notes ?? null, occurredAt: e.occurredAt,
      })),
  };
}

function receivables(data, q, paginate) {
  let lines = data.orders.filter(isOwed).map((o) => ({ key: phoneKey(o.customerPhone), order: o }));
  const search = (q.get('search') ?? '').trim().toLowerCase();
  if (search) {
    const digits = search.replace(/\D/g, '');
    const named = new Set(data.customers.filter((c) => c.name.toLowerCase().includes(search)).map((c) => phoneKey(c.phone)));
    const matching = new Set(lines
      .filter(({ key, order }) => order.customerName.toLowerCase().includes(search) || named.has(key) || (digits.length >= 3 && key.includes(digits)))
      .map(({ key }) => key));
    lines = lines.filter(({ key }) => matching.has(key));
  }
  if (q.get('overdueOnly') === 'true') {
    const overdue = new Set(lines.filter(({ order }) => isOverdue(data, order)).map(({ key }) => key));
    lines = lines.filter(({ key }) => overdue.has(key));
  }
  const byPhone = new Map();
  for (const { key, order } of lines) {
    const row = byPhone.get(key) ?? { phone: key, owed: 0, overdue: 0, orders: 0, since: order.createdAt, latest: order };
    row.owed = r2(row.owed + balanceOf(order));
    if (isOverdue(data, order)) row.overdue = r2(row.overdue + balanceOf(order));
    row.orders += 1;
    if (order.createdAt < row.since) row.since = order.createdAt;
    if (order.createdAt > row.latest.createdAt) row.latest = order;
    byPhone.set(key, row);
  }
  const rows = [...byPhone.values()].sort(q.get('sort') === '1'
    ? (a, b) => a.since.localeCompare(b.since) || a.phone.localeCompare(b.phone)
    : (a, b) => b.owed - a.owed || a.phone.localeCompare(b.phone));
  const page = paginate(rows, q, 20);
  return {
    totalOwed: r2(rows.reduce((sum, row) => sum + row.owed, 0)),
    totalOverdue: r2(rows.reduce((sum, row) => sum + row.overdue, 0)),
    creditTermDays: data.creditTermDays ?? null,
    creditReminderEveryDays: reminderEveryDays(data),
    creditDueSoonDaysBefore: dueSoonDaysBefore(data),
    totalCreditBalance: r2((data.balanceEntries ?? []).reduce((sum, e) => sum + e.amount, 0)),
    page: {
      ...page,
      items: page.items.map((row) => {
        const record = recordOf(data, row.phone);
        // A customer deactivated keeps their limit, but is not sold on credit.
        const limit = sellsOnCredit(record) ? record.creditLimit : null;
        return {
          phone: row.phone, name: record?.name ?? row.latest.customerName, customerId: record?.id ?? null, owed: row.owed,
          orders: row.orders, since: row.since, creditLimit: limit, overLimit: limit != null && row.owed > limit, overdue: row.overdue,
          ownCreditTermDays: ownTermOf(data, row.phone), creditBalance: balanceOfAccount(data, row.phone),
        };
      }),
    },
  };
}

/**
 * A payment to the account, split over the orders owed from the oldest (change on the last part), as the server does. One
 * that expected another balance (sent twice, or after another one) is refused. Fase 26D: paid with the balance, each part
 * takes from it (Applied); with `restInFavor`, what is over what they owe stays in their favor (Deposit). One that finds
 * the balance changed is refused too.
 */
function pay(data, body) {
  const key = body?.customerId ? phoneKey(data.customers.find((c) => c.id === body.customerId)?.phone) : phoneKey(body?.phone);
  const orders = data.orders.filter((o) => isOwed(o) && phoneKey(o.customerPhone) === key).sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  const owed = r2(orders.reduce((sum, o) => sum + balanceOf(o), 0));
  if (body?.expectedOwed != null && Math.abs(body.expectedOwed - owed) > 0.001) {
    return { status: 400, json: { code: 'bad_request', detail: `La cuenta cambió: ahora debe $${owed.toFixed(2)}. Revisa el estado de cuenta antes de cobrar.` } };
  }
  const balance = balanceOfAccount(data, key);
  if (body?.expectedBalance != null && Math.abs(body.expectedBalance - balance) > 0.001) {
    return { status: 400, json: { code: 'bad_request', detail: `El saldo a favor cambió: ahora tiene $${balance.toFixed(2)}. Revisa el estado de cuenta antes de seguir.` } };
  }
  const entries = data.balanceEntries ?? (data.balanceEntries = []);
  const withBalance = body?.method === CUSTOMER_BALANCE;
  let left = body?.amount ?? 0;
  for (const order of orders) {
    if (left <= 0) break;
    const part = r2(Math.min(left, balanceOf(order)));
    order.payments.push({ id: `pay-${order.payments.length + 1}-${order.id}`, amount: part, method: body.method, paidAt: NOW_ISO, reference: body.reference, notes: body.notes });
    order.paidAmount = r2(order.paidAmount + part);
    order.balance = balanceOf(order);
    left = r2(left - part);
    if (withBalance) {
      entries.push({ id: `be-${entries.length + 1}`, phone: key, kind: APPLIED, amount: -part, orderNumber: order.orderNumber, occurredAt: NOW_ISO });
    }
  }
  if (left > 0 && body?.restInFavor) {
    entries.push({
      id: `be-${entries.length + 1}`, phone: key, kind: DEPOSIT, amount: left, method: body.method, reference: body.reference ?? null,
      notes: body.notes ?? 'Lo que sobró de un abono', occurredAt: NOW_ISO,
    });
  }
  return { status: 200, json: account(data, key, recordOf(data, key)) };
}

/** Leaving it in their favor or giving it back (Fase 26D): one movement more, refused when the balance is not the one seen. */
function moveBalance(data, body, kind) {
  const key = body?.customerId ? phoneKey(data.customers.find((c) => c.id === body.customerId)?.phone) : phoneKey(body?.phone);
  const balance = balanceOfAccount(data, key);
  if (body?.expectedBalance != null && Math.abs(body.expectedBalance - balance) > 0.001) {
    return { status: 400, json: { code: 'bad_request', detail: `El saldo a favor cambió: ahora tiene $${balance.toFixed(2)}. Revisa el estado de cuenta antes de seguir.` } };
  }
  const entries = data.balanceEntries ?? (data.balanceEntries = []);
  entries.push({
    id: `be-${entries.length + 1}`, phone: key, kind, amount: kind === DEPOSIT ? body.amount : -body.amount, method: body.method,
    reference: body.reference ?? null, notes: body.notes ?? null, occurredAt: NOW_ISO,
  });
  return { status: 200, json: account(data, key, recordOf(data, key)) };
}

export function phase26aRoutes(data, { ok, notFound, paginate }) {
  return [
    ['GET', /^\/customers\/receivables$/, (m, q) => ok(receivables(data, q, paginate))],
    ['GET', /^\/customers\/account$/, (m, q) => {
      const record = q.get('customerId') ? data.customers.find((c) => c.id === q.get('customerId')) : null;
      const key = phoneKey(record ? record.phone : q.get('phone'));
      if (!key) return notFound('Cliente');
      return ok(account(data, key, record ?? recordOf(data, key)));
    }],
    ['POST', /^\/customers\/account\/payments$/, (m, q, body) => pay(data, body)],
    ['POST', /^\/customers\/account\/balance\/deposits$/, (m, q, body) => moveBalance(data, body, DEPOSIT)],
    ['POST', /^\/customers\/account\/balance\/returns$/, (m, q, body) => moveBalance(data, body, RETURNED)],
    ['PUT', /^\/customers\/([^/]+)\/credit-limit$/, (m, q, body) => {
      const customer = data.customers.find((c) => c.id === m[1]);
      if (!customer) return notFound('Cliente');
      customer.creditLimit = body?.creditLimit ?? null;
      return ok(customer);
    }],
  ];
}
