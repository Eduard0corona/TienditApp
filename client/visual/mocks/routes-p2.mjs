// Phase 2 endpoints for the fake backend: access, customer card, messaging, cash shifts, refunds.
import { NOW_ISO } from './data.mjs';

const ago = (minutes) => new Date(new Date(NOW_ISO).getTime() - minutes * 60000).toISOString();

export function phase2Data(data) {
  const templates = [
    { id: 't-promo', name: '2x1 de fin de semana', purpose: 0, body: 'Hola {nombre}, este fin de semana tenemos 2x1 en lattes en {negocio}. Escribe BAJA si no quieres recibir mensajes.', providerTemplateName: 'promo_fin_semana', languageCode: 'es_MX', isActive: true, parameters: ['nombre', 'negocio'], createdAt: ago(5000) },
    { id: 't-volver', name: 'Te extrañamos', purpose: 0, body: 'Hola {nombre}, hace tiempo que no te vemos en {negocio}. Tu siguiente café va por nuestra cuenta.', providerTemplateName: null, languageCode: 'es_MX', isActive: true, parameters: ['nombre', 'negocio'], createdAt: ago(9000) },
    { id: 't-cita', name: 'Recordatorio de cita', purpose: 1, body: 'Hola {nombre}, te recordamos tu cita de {servicio} el {fecha} a las {hora} en {negocio}.', providerTemplateName: 'cita_recordatorio', languageCode: 'es_MX', isActive: true, parameters: ['nombre', 'servicio', 'fecha', 'hora', 'negocio'], createdAt: ago(9500) },
  ];
  const progress = (p) => ({ pending: 0, sent: 0, delivered: 0, read: 0, failed: 0, skipped: 0, cancelled: 0, ...p });
  const campaigns = [
    { id: 'c-1', name: '2x1 lattes sábado', templateId: 't-promo', templateName: '2x1 de fin de semana', audienceKind: 1, audienceMinimumOrders: 2, audienceDays: 90, status: 1, scheduledAt: ago(30), startedAt: ago(29), recipientCount: 184, excludedCount: 6, createdBy: 'Mariana', createdAt: ago(40), progress: progress({ pending: 61, sent: 123, delivered: 110, read: 72, failed: 3, skipped: 0 }) },
    { id: 'c-2', name: 'Regresa por tu café', templateId: 't-volver', templateName: 'Te extrañamos', audienceKind: 2, audienceMinimumOrders: 1, audienceDays: 60, status: 2, scheduledAt: ago(4000), startedAt: ago(4000), completedAt: ago(3980), recipientCount: 52, excludedCount: 2, createdBy: 'Mariana', createdAt: ago(4100), progress: progress({ sent: 49, delivered: 47, read: 31, failed: 2, skipped: 1 }) },
    { id: 'c-3', name: 'Menú de temporada', templateId: 't-promo', templateName: '2x1 de fin de semana', audienceKind: 0, audienceMinimumOrders: 1, audienceDays: null, status: 0, scheduledAt: new Date(new Date(NOW_ISO).getTime() + 26 * 3600000).toISOString(), recipientCount: 0, excludedCount: 0, createdBy: 'Mariana', createdAt: ago(20), progress: progress({}) },
  ];
  const messages = [
    { id: 'm-1', purpose: 0, campaignId: 'c-1', phone: '+525541238890', recipientName: 'María Fernanda López', body: 'Hola María, este fin de semana…', status: 1, attempts: 1, createdAt: ago(29), nextAttemptAt: ago(29), sentAt: ago(28), deliveredAt: ago(27), readAt: ago(20) },
    { id: 'm-2', purpose: 0, campaignId: 'c-1', phone: '+525522107765', recipientName: 'Valeria Morales', body: 'Hola Valeria, este fin de semana…', status: 1, attempts: 1, createdAt: ago(29), nextAttemptAt: ago(29), sentAt: ago(28), deliveredAt: ago(27) },
    { id: 'm-3', purpose: 0, campaignId: 'c-1', phone: '+525599210087', recipientName: 'Roberto Sánchez', body: 'Hola Roberto, este fin de semana…', status: 2, attempts: 1, createdAt: ago(29), nextAttemptAt: ago(29), sentAt: ago(28), lastError: 'WhatsApp no pudo entregar el mensaje (código 131026).' },
    { id: 'm-4', purpose: 0, campaignId: 'c-1', phone: '+525533487712', recipientName: 'Luis Ángel Martínez', body: 'Hola Luis, este fin de semana…', status: 0, attempts: 0, createdAt: ago(29), nextAttemptAt: ago(1) },
    { id: 'r-1', purpose: 1, appointmentId: 'a-1', phone: '+525514576630', recipientName: 'Daniela Torres', body: 'Hola Daniela, te recordamos tu cita de Corte el lunes 5 de octubre a las 10:00 a. m. en Café Luna.', status: 1, attempts: 1, createdAt: ago(120), nextAttemptAt: ago(120), sentAt: ago(119), deliveredAt: ago(118) },
    { id: 'r-2', purpose: 1, appointmentId: 'a-2', phone: '+525570124456', recipientName: 'Ana Patricia Gómez', body: 'Hola Ana, te recordamos tu cita de Tinte el lunes 5 de octubre a las 12:30 p. m. en Café Luna.', status: 3, attempts: 0, createdAt: ago(60), nextAttemptAt: ago(60), lastError: 'El cliente pidió no recibir mensajes.' },
    { id: 'n-1', purpose: 2, appointmentId: 'a-3', phone: '+525514576630', recipientName: 'Daniela Torres', body: 'Hola Daniela, tu cita de Corte ahora es el martes 6 de octubre a las 11:00 a. m. en Café Luna.', status: 1, attempts: 1, createdAt: ago(30), nextAttemptAt: ago(30), sentAt: ago(30), deliveredAt: ago(29) },
    { id: 'n-2', purpose: 2, appointmentId: 'a-3', phone: '+525514576630', recipientName: 'Daniela Torres', body: 'Hola Daniela, quedó agendada tu cita de Corte el lunes 5 de octubre a las 10:00 a. m. en Café Luna.', status: 3, attempts: 0, createdAt: ago(45), nextAttemptAt: ago(45), lastError: 'Hay un aviso más reciente de la misma cita.' },
    { id: 'n-3', purpose: 2, appointmentId: 'a-4', phone: '+525570124456', recipientName: 'Ana Patricia Gómez', body: 'Hola Ana, tu cita de Tinte del lunes 5 de octubre a las 12:30 p. m. en Café Luna está confirmada.', status: 2, attempts: 1, createdAt: ago(90), nextAttemptAt: ago(90), lastError: 'WhatsApp no lo entregó: el cliente no escribió en las últimas 24 horas. Usa una plantilla aprobada en Meta.' },
  ];
  const optOuts = [
    { id: 'o-1', phone: '+525587761234', source: 0, optedOutAt: ago(1440), isActive: true },
    { id: 'o-2', phone: '+525511112222', source: 1, optedOutAt: ago(10000), isActive: true },
  ];
  const settings = {
    whatsAppConnected: true, appointmentsEnabled: true, appointmentRemindersEnabled: true, appointmentReminderHoursBefore: 24,
    appointmentReminderTemplateId: 't-cita', defaultReminderBody: 'Hola {nombre}, te recordamos tu cita de {servicio} el {fecha} a las {hora} en {negocio}. Si no puedes venir, respóndenos este mensaje. Escribe BAJA si no quieres recibir recordatorios.',
    minReminderHoursBefore: 1, maxReminderHoursBefore: 72, campaignPlaceholders: ['nombre', 'negocio'], reminderPlaceholders: ['nombre', 'negocio', 'servicio', 'fecha', 'hora'],
  };
  return { templates, campaigns, messages, optOuts, settings };
}

export function phase2Routes(data, { ok, notFound, paginate }) {
  const p2 = data.p2 ?? (data.p2 = phase2Data(data));
  const customerCard = (phone) => {
    const orders = data.orders.filter((o) => o.customerPhone && phone && o.customerPhone.slice(-10) === phone.replace(/\D/g, '').slice(-10));
    const live = orders.filter((o) => o.status !== 99);
    const spent = live.reduce((s, o) => s + o.paidAmount - (o.refundedAmount ?? 0), 0);
    return {
      phone: phone ? `+52${phone.replace(/\D/g, '').slice(-10)}` : null, name: orders[0]?.customerName ?? 'María Fernanda López', customerId: null, email: null,
      ordersCount: live.length || 7, totalSpent: Math.round((spent || 1843.5) * 100) / 100, averageTicket: 263.36, balance: live.reduce((s, o) => s + o.balance, 0),
      firstOrderAt: ago(60000), lastOrderAt: ago(30),
      recentOrders: (orders.length ? orders : data.orders.slice(0, 4)).slice(0, 5).map((o, i) => ({ id: o.id, orderNumber: o.orderNumber, createdAt: o.createdAt, status: o.status, channel: i === 0 ? 1 : 0, total: o.total, balance: o.balance })),
      favoriteProducts: [{ productId: 'p1', productName: 'Latte', quantity: 14 }, { productId: 'p2', productName: 'Torta de jamón', quantity: 9 }, { productId: 'p3', productName: 'Agua de jamaica', quantity: 6 }],
      upcomingAppointments: [], lastVisitAt: null, optedOut: false, optedOutAt: null, conversationId: data.conversations[0]?.id ?? null,
      notes: null, addresses: [],
      ...(data.cardExtras ?? {}),
    };
  };
  return [
    ['GET', /^\/me\/access$/, () => ok({ userId: data.USER_ID, role: 'TENANT_ADMIN', tenantRole: 'Owner', tenantId: data.TENANT_ID, isMember: true, isTenantManager: true, isPlatformAdmin: false, permissions: [...data.profile.permissions, 'ProcessRefunds', 'ManageCampaigns'], capabilities: data.tenant.capabilities })],
    ['GET', /^\/customers\/card$/, (m, q) => ok(customerCard(q.get('phone') ?? data.customers[1]?.phone ?? '5541238890'))],
    ['GET', /^\/messaging\/templates$/, (m, q) => {
      let rows = p2.templates;
      if (q.get('purpose')) rows = rows.filter((t) => t.purpose === Number(q.get('purpose')));
      if (q.get('includeInactive') === 'false') rows = rows.filter((t) => t.isActive);
      return ok(rows);
    }],
    ['GET', /^\/messaging\/campaigns$/, (m, q) => ok(paginate(p2.campaigns, q, 10))],
    ['GET', /^\/messaging\/campaigns\/([^/]+)$/, (m) => {
      const c = p2.campaigns.find((x) => x.id === m[1]);
      return c ? ok(c) : notFound('Campaña');
    }],
    ['POST', /^\/messaging\/campaigns\/preview$/, (m, q, body) => {
      const t = p2.templates.find((x) => x.id === body?.templateId) ?? p2.templates[0];
      const names = ['María Fernanda', 'Valeria', 'Roberto'];
      return ok({ recipientCount: body?.audienceKind === 0 ? 312 : 184, excludedCount: 6, truncated: false, maxRecipients: 5000, usesApprovedTemplate: !!t.providerTemplateName,
        samples: names.map((n, i) => ({ recipientName: `${n} ${['López', 'Morales', 'Sánchez'][i]}`, phone: `+5255••••${String(1234 + i * 1111).slice(0, 4)}`, text: t.body.replace('{nombre}', n.split(' ')[0]).replace('{negocio}', data.tenant.name) })) });
    }],
    ['POST', /^\/messaging\/campaigns$/, (m, q, body) => {
      const c = { id: `c-${p2.campaigns.length + 1}`, name: body.name, templateId: body.templateId, templateName: p2.templates.find((t) => t.id === body.templateId)?.name, audienceKind: body.audienceKind, audienceMinimumOrders: body.minimumOrders, audienceDays: body.days ?? null, status: 0, scheduledAt: body.scheduledAt ?? NOW_ISO, recipientCount: 0, excludedCount: 0, createdBy: 'Mariana', createdAt: NOW_ISO, progress: { pending: 0, sent: 0, delivered: 0, read: 0, failed: 0, skipped: 0, cancelled: 0 } };
      p2.campaigns.unshift(c);
      return ok(c);
    }],
    ['POST', /^\/messaging\/campaigns\/([^/]+)\/cancel$/, (m) => {
      const c = p2.campaigns.find((x) => x.id === m[1]);
      if (!c) return notFound('Campaña');
      c.status = 3; c.cancelledAt = NOW_ISO;
      return ok(c);
    }],
    ['GET', /^\/messaging\/messages$/, (m, q) => {
      let rows = p2.messages;
      if (q.get('campaignId')) rows = rows.filter((x) => x.campaignId === q.get('campaignId'));
      if (q.get('purpose')) rows = rows.filter((x) => x.purpose === Number(q.get('purpose')));
      return ok(paginate(rows, q, 20));
    }],
    ['GET', /^\/cash\/sessions$/, (m, q) => ok(paginate([
      { ...data.cashSession, openedByUserId: data.USER_ID, inventoryLocationName: 'Roma', openedByName: 'Mariana' },
      { id: 'cs-prev', openedAt: ago(1500), closedAt: ago(1000), openedBy: 'luis@cafeluna.mx', openedByName: 'Luis Ramírez', inventoryLocationName: 'Roma', openingAmount: 1000, closingAmount: 3240, expectedClosingAmount: 3290, closingDifference: -50, closingNotes: 'Faltaron $50: un cambio mal dado', status: 1, inflows: 2490, outflows: 200, nonCashInflows: 1830, expectedBalance: 3290, branchName: 'Roma', cashCount: [{ denomination: 500, quantity: 4 }, { denomination: 200, quantity: 5 }, { denomination: 20, quantity: 12 }] },
    ], q, 20))],
    ['GET', /^\/messaging\/opt-outs$/, (m, q) => ok(paginate(p2.optOuts, q, 20))],
    ['POST', /^\/messaging\/opt-outs$/, () => ({ status: 204, json: null })],
    ['GET', /^\/messaging\/settings$/, () => ok(p2.settings)],
    ['PUT', /^\/messaging\/settings\/reminders$/, (m, q, body) => {
      Object.assign(p2.settings, { appointmentRemindersEnabled: body.enabled, appointmentReminderHoursBefore: body.hoursBefore, appointmentReminderTemplateId: body.templateId ?? null });
      return ok(p2.settings);
    }],
    ['POST', /^\/messaging\/templates$/, (m, q, body) => ok({ id: `t-${p2.templates.length + 1}`, ...body, isActive: true, parameters: [], createdAt: NOW_ISO })],
    ['PUT', /^\/messaging\/templates\/([^/]+)$/, (m, q, body) => ok({ id: m[1], ...body, parameters: [], createdAt: NOW_ISO })],
    ['POST', /^\/orders\/([^/]+)\/(refunds|void)$/, (m, q, body) => {
      const o = data.orders.find((x) => x.id === m[1]);
      if (!o) return notFound('Orden');
      const amount = m[2] === 'void' ? (o.paidAmount - (o.refundedAmount ?? 0)) : body.amount;
      o.refundedAmount = Math.round(((o.refundedAmount ?? 0) + amount) * 100) / 100;
      o.netPaidAmount = Math.round((o.paidAmount - o.refundedAmount) * 100) / 100;
      o.refunds = [...(o.refunds ?? []), { id: `rf-${(o.refunds ?? []).length + 1}`, amount, method: body.method ?? 0, reason: body.reason, refundedAt: NOW_ISO, refundedBy: 'Mariana', restockItems: !!body.restockItems, lines: body.items ?? [] }];
      if (m[2] === 'void' && o.allowedTransitions?.includes(99)) { o.status = 99; o.allowedTransitions = []; }
      return ok(o);
    }],
  ];
}
