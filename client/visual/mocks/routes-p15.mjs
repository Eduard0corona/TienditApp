// Phase 26A endpoints for the fake backend: el fiado (ADR 0033). Who owes the business (GET /customers/receivables), the
// account of a customer (GET /customers/account), a payment to it (POST /customers/account/payments) and how much they may
// owe (PUT /customers/{id}/credit-limit). What is owed comes from `data.orders` by the server's rule (Receivables): not
// cancelled, with a phone and a balance, and delivered or sold on credit, except what a courier still collects. The limit
// is `creditLimit` of the customer in `data.customers`; without one, nobody is sold on credit.
import { NOW_ISO } from './data.mjs';

// OrderStatus as the contract numbers it.
const DELIVERED = 6;
const COMPLETED = 7;
const CANCELLED = 99;
const RECENT_PAYMENTS = 10;

const r2 = (value) => Math.round(value * 100) / 100;

/** The phone the orders are grouped by: +52 and the last ten digits, as the server keys them. */
export function phoneKey(phone) {
  const digits = String(phone ?? '').replace(/\D/g, '');
  return digits.length >= 10 ? `+52${digits.slice(-10)}` : null;
}

/** The server's rule of an order owed (Receivables.IsOwed). */
export function isOwed(order) {
  return order.status !== CANCELLED && !!phoneKey(order.customerPhone) && order.total > order.paidAmount
    && (!!order.soldOnCredit || order.status === DELIVERED || order.status === COMPLETED)
    && !(order.delivery?.cashToCollect != null && order.status === DELIVERED);
}

/** What the orders with a phone owe, as the customer card and the account add it up. */
export function owedBy(data, phone) {
  const key = phoneKey(phone);
  return r2(data.orders.filter((o) => isOwed(o) && phoneKey(o.customerPhone) === key).reduce((sum, o) => sum + o.total - o.paidAmount, 0));
}

/** The customer record of a phone: the active one first (CreditAccount.RecordAsync). */
export function recordOf(data, key) {
  const records = data.customers.filter((c) => !c.isPublicGeneral && phoneKey(c.phone) === key);
  return records.find((c) => c.isActive) ?? records.find((c) => c.creditLimit != null) ?? records[0] ?? null;
}

/** The business sells to them on credit now: an active customer record with a phone and a limit. */
export const sellsOnCredit = (record) => !!record && record.isActive && !!record.phone && record.creditLimit != null;

function account(data, key, record) {
  const orders = data.orders
    .filter((o) => isOwed(o) && phoneKey(o.customerPhone) === key)
    .sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  const owed = r2(orders.reduce((sum, o) => sum + o.total - o.paidAmount, 0));
  const limit = record?.creditLimit ?? null;
  const payments = data.orders
    .filter((o) => phoneKey(o.customerPhone) === key)
    .flatMap((o) => (o.payments ?? []).map((p) => ({ orderNumber: o.orderNumber, paidAt: p.paidAt, amount: p.amount, method: p.method })))
    .sort((a, b) => b.paidAt.localeCompare(a.paidAt))
    .slice(0, RECENT_PAYMENTS);
  return {
    phone: key,
    name: record?.name ?? [...orders].reverse()[0]?.customerName ?? null,
    customerId: record?.id ?? null,
    creditLimit: limit,
    sellsOnCredit: sellsOnCredit(record),
    owed,
    available: limit == null ? null : r2(Math.max(0, limit - owed)),
    overLimit: limit != null && owed > limit,
    orders: orders.map((o) => ({
      orderId: o.id, orderNumber: o.orderNumber, createdAt: o.createdAt, status: o.status, soldOnCredit: !!o.soldOnCredit,
      total: o.total, paid: o.paidAmount, balance: r2(o.total - o.paidAmount),
    })),
    ordersCount: orders.length,
    recentPayments: payments,
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
  const byPhone = new Map();
  for (const { key, order } of lines) {
    const row = byPhone.get(key) ?? { phone: key, owed: 0, orders: 0, since: order.createdAt, latest: order };
    row.owed = r2(row.owed + order.total - order.paidAmount);
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
    page: {
      ...page,
      items: page.items.map((row) => {
        const record = recordOf(data, row.phone);
        const limit = record?.creditLimit ?? null;
        return {
          phone: row.phone, name: record?.name ?? row.latest.customerName, customerId: record?.id ?? null, owed: row.owed,
          orders: row.orders, since: row.since, creditLimit: limit, overLimit: limit != null && row.owed > limit,
        };
      }),
    },
  };
}

/** A payment to the account, split over the orders owed from the oldest (change on the last part), as the server does. */
function pay(data, body) {
  const key = body?.customerId ? phoneKey(data.customers.find((c) => c.id === body.customerId)?.phone) : phoneKey(body?.phone);
  const orders = data.orders.filter((o) => isOwed(o) && phoneKey(o.customerPhone) === key).sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  let left = body?.amount ?? 0;
  for (const order of orders) {
    if (left <= 0) break;
    const part = r2(Math.min(left, order.total - order.paidAmount));
    order.payments.push({ id: `pay-${order.payments.length + 1}-${order.id}`, amount: part, method: body.method, paidAt: NOW_ISO, reference: body.reference, notes: body.notes });
    order.paidAmount = r2(order.paidAmount + part);
    order.balance = r2(order.total - order.paidAmount);
    left = r2(left - part);
  }
  return account(data, key, recordOf(data, key));
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
    ['POST', /^\/customers\/account\/payments$/, (m, q, body) => ok(pay(data, body))],
    ['PUT', /^\/customers\/([^/]+)\/credit-limit$/, (m, q, body) => {
      const customer = data.customers.find((c) => c.id === m[1]);
      if (!customer) return notFound('Cliente');
      customer.creditLimit = body?.creditLimit ?? null;
      return ok(customer);
    }],
  ];
}
