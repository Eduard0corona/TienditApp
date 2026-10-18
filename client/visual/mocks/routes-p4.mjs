// Phase 4C endpoints for the fake backend: archived categories, purchase order actions, agenda order and stock policy.
import { NOW_ISO } from './data.mjs';

const r2 = (value) => Math.round(value * 100) / 100;

function seedPhase4(data) {
  if (data.p4) return data.p4;
  for (const category of data.categories) {
    category.productCount = data.products.filter((product) => product.categoryId === category.id).length;
  }
  data.tenant.insufficientStockPolicy ??= 0;
  // Chat: the first conversation is in a person's hands (bot paused) and its replies show every tick.
  const first = data.conversations[0];
  first.autoRepliesPausedUntil = new Date(new Date(NOW_ISO).getTime() + 3 * 3600_000).toISOString();
  const thread = data.messagesByConversation[first.id] ?? [];
  const outbound = thread.filter((m) => m.direction === 'Outbound').sort((a, b) => a.sentAt.localeCompare(b.sentAt));
  ['Read', 'Read', 'Delivered'].forEach((status, index) => { if (outbound[index]) outbound[index].status = status; });
  thread.push({ id: 'mock-sent-now', conversationId: first.id, direction: 'Outbound', body: 'Ya está lista, te esperamos en caja 🙂', status: 'Sent', sentAt: NOW_ISO });
  thread.push({ id: 'mock-photo', conversationId: first.id, direction: 'Inbound', body: '📷 Foto: así quiero el empaque', status: 'Read', sentAt: new Date(new Date(NOW_ISO).getTime() - 60_000).toISOString() });
  thread.sort((a, b) => b.sentAt.localeCompare(a.sentAt));
  data.cardExtras = {
    customerId: 'cust-mafer',
    notes: 'Prefiere las tortas sin chipotle. Paga con tarjeta.',
    addresses: [{ id: 'addr-1', label: 'Oficina', addressLine: 'Av. Insurgentes Sur 1235, piso 4, Col. Del Valle', references: 'Recepción, preguntar por María', isDefault: true }],
  };
  data.p4 = { seeded: true };
  return data.p4;
}

function recalc(po) {
  for (const line of po.lines) {
    line.pendingQuantity = r2(line.quantity - line.receivedQuantity);
    line.subTotal = r2(line.quantity * line.unitCost);
    line.taxAmount = r2(line.subTotal * line.taxPercent / 100);
    line.total = r2(line.subTotal + line.taxAmount);
  }
  po.subTotal = r2(po.lines.reduce((sum, line) => sum + line.subTotal, 0));
  po.tax = r2(po.lines.reduce((sum, line) => sum + line.taxAmount, 0));
  po.total = r2(po.subTotal + po.tax);
}

export function phase4Routes(data, { ok, notFound, paginate, createMockOrder }) {
  seedPhase4(data);
  const bad = (error) => ({ status: 400, json: { error } });
  const findPo = (id) => data.purchaseOrders.find((po) => po.id === id);
  return [
    // Mock-only switch: the next sale answers with these stock warnings (Vender shows them in a toast).
    ['POST', /^\/__mock\/stock-warnings$/, (m, q, body) => { data.nextStockWarnings = body; return ok(null); }],
    ['POST', /^\/orders$/, (m, q, body) => {
      const order = createMockOrder(data, body);
      if (data.nextStockWarnings) {
        order.stockWarnings = data.nextStockWarnings;
        data.nextStockWarnings = null;
      }
      return ok(order);
    }],
    ['GET', /^\/categories$/, (m, q) => ok(q.get('includeArchived') === 'true'
      ? [...data.categories].sort((a, b) => Number(b.isActive) - Number(a.isActive) || a.sortOrder - b.sortOrder)
      : data.categories.filter((c) => c.isActive))],
    ['PATCH', /^\/categories\/([^/]+)\/active$/, (m, q, body) => {
      const category = data.categories.find((c) => c.id === m[1]);
      if (!category) return notFound('Categoría');
      if (!body.isActive && category.productCount > 0) {
        if (!body.moveProductsToCategoryId) return bad(`La categoría tiene ${category.productCount} productos: elige a qué categoría pasarlos antes de archivarla.`);
        const target = data.categories.find((c) => c.id === body.moveProductsToCategoryId && c.isActive && c.id !== category.id);
        if (!target) return bad('Elige otra categoría activa para los productos.');
        target.productCount += category.productCount;
        category.productCount = 0;
      }
      category.isActive = body.isActive;
      return ok(null);
    }],
    ['POST', /^\/purchaseorders\/([^/]+)\/send$/, (m) => {
      const po = findPo(m[1]);
      if (!po) return notFound('Compra');
      if (po.status !== 0) return { status: 422, json: { detail: 'Solo una compra en borrador puede enviarse.' } };
      po.status = 1;
      return ok(po);
    }],
    ['POST', /^\/purchaseorders\/([^/]+)\/cancel$/, (m) => {
      const po = findPo(m[1]);
      if (!po) return notFound('Compra');
      if (po.lines.some((line) => line.receivedQuantity > 0)) return { status: 422, json: { detail: 'La compra ya tiene mercancía recibida; no se puede cancelar.' } };
      po.status = 99;
      return ok(po);
    }],
    ['POST', /^\/purchaseorders\/([^/]+)\/receive$/, (m, q, body) => {
      const po = findPo(m[1]);
      if (!po) return notFound('Compra');
      for (const received of body.lines) {
        const line = po.lines.find((l) => l.id === received.purchaseOrderLineId);
        line.receivedQuantity = r2(line.receivedQuantity + received.quantityReceived);
        if (received.unitCost !== undefined) line.unitCost = received.unitCost;
      }
      recalc(po);
      po.receivedAt = NOW_ISO;
      po.status = po.lines.every((line) => line.pendingQuantity <= 0) ? 3 : 2;
      return ok(po);
    }],
    ['GET', /^\/appointments$/, (m, q) => {
      let rows = data.appointments;
      const from = q.get('fromDate');
      const to = q.get('toDate');
      if (from) rows = rows.filter((a) => a.startAt >= from);
      if (to) rows = rows.filter((a) => a.startAt <= to);
      // Where they happen (Fase 22E): at the customer's address or at the business.
      if (q.has('homeVisit')) rows = rows.filter((a) => Boolean(a.visit) === (q.get('homeVisit') === 'true'));
      // By status: the agenda counts the requests booked online that wait (Fase 24D).
      if (q.has('status')) rows = rows.filter((a) => a.status === Number(q.get('status')));
      const history = !from && to;
      rows = [...rows].sort((a, b) => (history ? b.startAt.localeCompare(a.startAt) : a.startAt.localeCompare(b.startAt)));
      return ok(paginate(rows, q, 10));
    }],
    ['PUT', /^\/whatsapp\/chat\/([^/]+)\/auto-replies$/, (m, q, body) => {
      const conversation = data.conversations.find((c) => c.id === m[1]);
      if (!conversation) return notFound('Conversación');
      conversation.autoRepliesPausedUntil = body.paused ? new Date(new Date(NOW_ISO).getTime() + 24 * 3600_000).toISOString() : null;
      return ok(conversation);
    }],
    ['PUT', /^\/customers\/([^/]+)\/notes$/, (m, q, body) => { data.cardExtras.notes = body.notes; return ok({}); }],
    ['POST', /^\/customers\/([^/]+)\/addresses$/, (m, q, body) => {
      data.cardExtras.addresses.push({ id: `addr-${data.cardExtras.addresses.length + 1}`, label: body.label || 'Dirección', addressLine: body.addressLine, references: body.references, isDefault: data.cardExtras.addresses.length === 0 });
      return ok({});
    }],
    ['PUT', /^\/customers\/([^/]+)\/addresses\/([^/]+)$/, (m) => {
      data.cardExtras.addresses.forEach((a) => { a.isDefault = a.id === m[2]; });
      return ok({});
    }],
    ['DELETE', /^\/customers\/([^/]+)\/addresses\/([^/]+)$/, (m) => {
      data.cardExtras.addresses = data.cardExtras.addresses.filter((a) => a.id !== m[2]);
      return ok({});
    }],
    // A customer account: no business, only its own orders.
    ['GET', /^\/me\/access$/, () => (data.profile.role === 'Customer'
      ? ok({ userId: data.USER_ID, role: 'CUSTOMER', tenantRole: null, tenantId: null, isMember: false, isTenantManager: false, isPlatformAdmin: false, permissions: [], capabilities: 0 })
      : null)],
    // MyOrderDto: each line with its product and options, as «Pedir otra vez» puts them back in the store's cart, and the
    // step of each order (Fase 24C): the tortas on their way and the laundry accepted are in progress, the latte is before.
    ['GET', /^\/me\/orders$/, (m, q) => {
      const productId = (name) => data.products.find((p) => p.name === name)?.id ?? `otro-negocio:${name}`;
      const item = (productName, quantity, optionIds = [], product = productName) => ({ productId: productId(product), productName, quantity, optionIds });
      const orders = [
        { id: 'my-1', orderNumber: 'ORD-000412', businessName: 'Café La Esquina', businessSlug: 'cafe-la-esquina', canOrderAgain: true, status: 3, orderType: 0, step: 4, stepLabel: 'En camino', total: 235, paidAmount: 0, createdAt: new Date(new Date(NOW_ISO).getTime() - 40 * 60_000).toISOString(),
          items: [item('Torta de jamón', 2), item('Agua de jamaica', 2)] },
        { id: 'my-2', orderNumber: 'ORD-000088', businessName: 'Lavandería Burbujas', businessSlug: 'lavanderia-burbujas', canOrderAgain: false, status: 1, orderType: 1, step: 1, stepLabel: 'Aceptado', total: 180, paidAmount: 0, createdAt: new Date(new Date(NOW_ISO).getTime() - 2 * 86400_000).toISOString(),
          items: [item('Lavado por kilo', 4.5)] },
        { id: 'my-3', orderNumber: 'ORD-000390', businessName: 'Café La Esquina', businessSlug: 'cafe-la-esquina', canOrderAgain: true, status: 7, orderType: 0, step: 5, stepLabel: 'Entregado', total: 95, paidAmount: 95, createdAt: new Date(new Date(NOW_ISO).getTime() - 9 * 86400_000).toISOString(),
          items: [item('Latte', 1, ['o-gr']), item('Croissant', 1, [], 'Croissant de mantequilla')] },
      ];
      // MyOrdersScope: 1 in progress, 2 before, 0 all.
      const finished = (order) => [6, 7, 99].includes(order.status);
      const scope = Number(q.get('scope') ?? 0);
      return ok(paginate(scope === 1 ? orders.filter((o) => !finished(o)) : scope === 2 ? orders.filter(finished) : orders, q, 10));
    }],
    ['PUT', /^\/auth\/my-tenant\/sales-policy$/, (m, q, body) => {
      Object.assign(data.tenant, body);
      return ok(body);
    }],
  ];
}
