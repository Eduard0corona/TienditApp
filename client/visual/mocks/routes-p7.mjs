// Phase 5E endpoints for the fake backend: purchase orders with the supplier phone and units, closing a drawer.
import { NOW_ISO } from './data.mjs';

const r2 = (value) => Math.round(value * 100) / 100;

function seedPhase5e(data) {
  if (data.p5e) return;
  for (const po of data.purchaseOrders) {
    const supplier = data.suppliers.find((s) => s.id === po.supplierId);
    po.supplierPhone = supplier?.phone ?? null;
    for (const line of po.lines) {
      line.unit = data.supplies.find((s) => s.id === line.supplyItemId)?.unit ?? 0;
    }
  }
  data.p5e = true;
}

export function phase5eRoutes(data, { ok, notFound }) {
  seedPhase5e(data);
  return [
    ['GET', /^\/dashboard\/attention$/, () => ok({ items: data.attentionItems ?? [
      { key: 'onlineOrdersToConfirm', count: 2, urgentCount: 0 },
      { key: 'lowStock', count: 4, urgentCount: 1 },
      { key: 'drawersOpenSinceYesterday', count: 1, urgentCount: 0 },
      { key: 'unreadChats', count: 3, urgentCount: 0 },
    ] })],
    ['POST', /^\/purchaseorders\/([^/]+)\/close$/, (m, q, body) => {
      const po = data.purchaseOrders.find((x) => x.id === m[1]);
      if (!po) return notFound('Orden de compra');
      if (po.status !== 2) return { status: 400, json: { error: 'Solo una compra con recepción parcial se cierra sin esperar el resto.' } };
      Object.assign(po, { status: 4, closedAt: NOW_ISO, closeReason: body?.reason ?? null });
      return ok(po);
    }],
    ['POST', /^\/cash\/([^/]+)\/close$/, (m, q, body) => {
      const session = data.cashSession;
      if (!session || session.id !== m[1]) return notFound('Caja');
      const expected = session.expectedBalance;
      const closed = {
        ...session,
        status: 1,
        closedAt: NOW_ISO,
        closedBy: session.openedBy,
        closedByName: session.openedByName ?? null,
        closingAmount: body.closingAmount,
        expectedClosingAmount: expected,
        closingDifference: r2(body.closingAmount - expected),
        closingNotes: body.notes ?? null,
        cashCount: body.cashCount ?? [],
        isMine: true,
        branchName: 'Roma',
      };
      data.cashSession = null;
      return ok(closed);
    }],
  ];
}
