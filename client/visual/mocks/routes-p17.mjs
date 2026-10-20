// Phase 26C endpoints for the fake backend: the term of a customer and the reminders of the fiado (ADR 0035). A customer's
// own term (PUT /customers/{id}/credit-term) is `creditTermDays` of their record, which Por cobrar and the account of
// routes-p15 read; the reminders (GET and PUT /customers/credit-reminders) are `data.creditReminders`, off at first, with
// the active templates of Mensajes for «Recordatorio del fiado» and whether WhatsApp is connected (`data.p2.settings`).
import { phase2Data } from './routes-p2.mjs';

// MessagePurpose as the contract numbers it.
const CREDIT_REMINDER = 3;
const DEFAULT_BODY = 'Hola {nombre}, te recordamos que en {negocio} tienes un saldo de {monto}; {vencido} venció el {fecha}. '
  + 'Si ya pagaste, ignora este mensaje. Escribe BAJA si no quieres recibir recordatorios.';

const settingsOf = (data) => data.creditReminders ?? (data.creditReminders = { enabled: false, everyDays: 7, templateId: null });

/** What the owner sees to set the reminders up (GetCreditRemindersQuery): a retired template reads as none. */
function creditReminders(data) {
  const p2 = data.p2 ?? (data.p2 = phase2Data(data));
  const settings = settingsOf(data);
  const templates = p2.templates
    .filter((t) => t.purpose === CREDIT_REMINDER && t.isActive)
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((t) => ({ id: t.id, name: t.name, body: t.body, providerTemplateName: t.providerTemplateName ?? null }));
  return {
    enabled: settings.enabled,
    everyDays: settings.everyDays,
    templateId: templates.some((t) => t.id === settings.templateId) ? settings.templateId : null,
    whatsAppConnected: p2.settings.whatsAppConnected,
    creditTermDays: data.creditTermDays ?? null,
    customersWithOwnTerm: data.customers.filter((c) => c.creditTermDays != null).length,
    defaultBody: DEFAULT_BODY,
    placeholders: ['nombre', 'negocio', 'monto', 'vencido', 'fecha'],
    templates,
    minEveryDays: 1,
    maxEveryDays: 30,
    sendFrom: '10:00',
    sendUntil: '19:00',
  };
}

export function phase26cRoutes(data, { ok, notFound }) {
  return [
    ['GET', /^\/customers\/credit-reminders$/, () => ok(creditReminders(data))],
    ['PUT', /^\/customers\/credit-reminders$/, (m, q, body) => {
      const current = creditReminders(data);
      if (body?.enabled && !current.whatsAppConnected) {
        return { status: 400, json: { code: 'bad_request', detail: 'Conecta WhatsApp en Mi negocio antes de enviar mensajes.' } };
      }
      Object.assign(settingsOf(data), { enabled: !!body?.enabled, everyDays: body?.everyDays ?? 7, templateId: body?.templateId ?? null });
      return ok(creditReminders(data));
    }],
    ['PUT', /^\/customers\/([^/]+)\/credit-term$/, (m, q, body) => {
      const customer = data.customers.find((c) => c.id === m[1]);
      if (!customer) return notFound('Cliente');
      customer.creditTermDays = body?.creditTermDays ?? null;
      return ok(customer);
    }],
  ];
}
