// Phase 26B endpoints for the fake backend: condonar and the term of the fiado (ADR 0034). A write-off of an account
// (POST /customers/account/write-offs) goes on one order or from the oldest, never above what is owed, and is refused when
// the account changed; undoing one (POST …/write-offs/{id}/reversal) marks it undone, once, and owes it again; the term
// (PUT /customers/credit-term) is `data.creditTermDays`, which Por cobrar and the account of routes-p15 read.
import { NOW_ISO } from './data.mjs';
import { account, balanceOf, isOwed, phoneKey, recordOf } from './routes-p15.mjs';

const r2 = (value) => Math.round(value * 100) / 100;

function writeOff(data, body) {
  const key = body?.customerId ? phoneKey(data.customers.find((c) => c.id === body.customerId)?.phone) : phoneKey(body?.phone);
  const orders = data.orders.filter((o) => isOwed(o) && phoneKey(o.customerPhone) === key).sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  const owed = r2(orders.reduce((sum, o) => sum + balanceOf(o), 0));
  if (owed === 0) return { status: 400, json: { code: 'bad_request', detail: 'No debe nada: no hay nada que condonar.' } };
  if (Math.abs((body?.expectedOwed ?? 0) - owed) > 0.001) {
    return { status: 400, json: { code: 'bad_request', detail: `La cuenta cambió: ahora debe $${owed.toFixed(2)}. Revisa el estado de cuenta antes de condonar.` } };
  }
  const targets = body?.orderId ? orders.filter((o) => o.id === body.orderId) : orders;
  let left = body?.amount ?? 0;
  for (const order of targets) {
    if (left <= 0) break;
    const part = r2(Math.min(left, balanceOf(order)));
    order.writeOffs = [...(order.writeOffs ?? []), {
      id: `condonado-${(order.writeOffs ?? []).length + 1}-${order.id}`, amount: part, reason: body.reason, notes: body.notes ?? null,
      writtenOffAt: NOW_ISO, writtenOffBy: data.OWNER_EMAIL,
    }];
    order.writtenOffAmount = r2((order.writtenOffAmount ?? 0) + part);
    order.balance = balanceOf(order);
    left = r2(left - part);
  }
  return { status: 200, json: account(data, key, recordOf(data, key)) };
}

function reverse(data, writeOffId) {
  const order = data.orders.find((o) => (o.writeOffs ?? []).some((w) => w.id === writeOffId));
  if (!order) return { status: 404, json: { code: 'not_found', detail: 'No encontramos eso.' } };
  const record = order.writeOffs.find((w) => w.id === writeOffId);
  if (record.reversedAt) return { status: 400, json: { code: 'bad_request', detail: 'Eso ya se deshizo.' } };
  record.reversedAt = NOW_ISO;
  record.reversedBy = data.OWNER_EMAIL;
  order.writtenOffAmount = r2(order.writtenOffAmount - record.amount);
  order.balance = balanceOf(order);
  const key = phoneKey(order.customerPhone);
  return { status: 200, json: account(data, key, recordOf(data, key)) };
}

export function phase26bRoutes(data, { ok }) {
  return [
    ['POST', /^\/customers\/account\/write-offs$/, (m, q, body) => writeOff(data, body)],
    ['POST', /^\/customers\/account\/write-offs\/([^/]+)\/reversal$/, (m) => reverse(data, m[1])],
    ['PUT', /^\/customers\/credit-term$/, (m, q, body) => {
      data.creditTermDays = body?.creditTermDays ?? null;
      return ok({ creditTermDays: data.creditTermDays });
    }],
  ];
}
