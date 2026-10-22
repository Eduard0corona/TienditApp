// Phase 26C endpoints for the fake backend: the term of a customer and the reminders of the fiado (ADR 0035). A customer's
// own term (PUT /customers/{id}/credit-term) is `creditTermDays` of their record, which Por cobrar and the account of
// routes-p15 read; the reminders (GET and PUT /customers/credit-reminders) are `data.creditReminders`, off at first, with
// the active templates of Mensajes for «Recordatorio del fiado» and whether WhatsApp is connected (`data.p2.settings`).
// Fase 26E (ADR 0043): the notice before something comes due is `data.creditDueSoon`, off at first, two days before, with
// the templates for «Aviso antes de vencer»; a PUT without `dueSoon` leaves it as it is.
import { phase2Data } from './routes-p2.mjs';

// MessagePurpose as the contract numbers it.
const CREDIT_REMINDER = 3;
const CREDIT_DUE_SOON = 4;
const DEFAULT_BODY = 'Hola {nombre}, te recordamos que en {negocio} tienes un saldo de {monto}; {vencido} venció el {fecha}. '
  + 'Si ya pagaste, ignora este mensaje. Escribe BAJA si no quieres recibir recordatorios.';
const DUE_SOON_BODY = 'Hola {nombre}, te recordamos que en {negocio} tienes un saldo de {monto}; {porvencer} vence el {fecha}. '
  + 'Si ya pagaste, ignora este mensaje. Escribe BAJA si no quieres recibir recordatorios.';

const settingsOf = (data) => data.creditReminders ?? (data.creditReminders = { enabled: false, everyDays: 7, templateId: null });
const dueSoonOf = (data) => data.creditDueSoon ?? (data.creditDueSoon = { enabled: false, daysBefore: 2, templateId: null });

/** The active templates of a use, by name, as the settings screen lists them. */
const templatesOf = (p2, purpose) => p2.templates
  .filter((t) => t.purpose === purpose && t.isActive)
  .sort((a, b) => a.name.localeCompare(b.name))
  .map((t) => ({ id: t.id, name: t.name, body: t.body, providerTemplateName: t.providerTemplateName ?? null }));

/** What the owner sees to set the reminders up (GetCreditRemindersQuery): a retired template reads as none. */
function creditReminders(data) {
  const p2 = data.p2 ?? (data.p2 = phase2Data(data));
  const settings = settingsOf(data);
  const dueSoon = dueSoonOf(data);
  const templates = templatesOf(p2, CREDIT_REMINDER);
  const dueSoonTemplates = templatesOf(p2, CREDIT_DUE_SOON);
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
    dueSoon: {
      enabled: dueSoon.enabled,
      daysBefore: dueSoon.daysBefore,
      templateId: dueSoonTemplates.some((t) => t.id === dueSoon.templateId) ? dueSoon.templateId : null,
      defaultBody: DUE_SOON_BODY,
      placeholders: ['nombre', 'negocio', 'monto', 'porvencer', 'fecha'],
      templates: dueSoonTemplates,
      minDaysBefore: 1,
      maxDaysBefore: 7,
    },
  };
}

export function phase26cRoutes(data, { ok, notFound }) {
  return [
    ['GET', /^\/customers\/credit-reminders$/, () => ok(creditReminders(data))],
    ['PUT', /^\/customers\/credit-reminders$/, (m, q, body) => {
      const current = creditReminders(data);
      if ((body?.enabled || body?.dueSoon?.enabled) && !current.whatsAppConnected) {
        return { status: 400, json: { code: 'bad_request', detail: 'Conecta WhatsApp en Mi negocio antes de enviar mensajes.' } };
      }
      Object.assign(settingsOf(data), { enabled: !!body?.enabled, everyDays: body?.everyDays ?? 7, templateId: body?.templateId ?? null });
      if (body?.dueSoon) {
        Object.assign(dueSoonOf(data), {
          enabled: !!body.dueSoon.enabled, daysBefore: body.dueSoon.daysBefore ?? 2, templateId: body.dueSoon.templateId ?? null,
        });
      }
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
