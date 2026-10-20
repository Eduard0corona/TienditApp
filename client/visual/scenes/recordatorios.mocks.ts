import { asOption, type DemoData } from '../fixtures';
import { at } from '../mocks/data.mjs';
import { phase2Data } from '../mocks/routes-p2.mjs';
import { phoneKey } from '../mocks/routes-p15.mjs';
import { withCredit } from './fiado.mocks';

// MessagePurpose and OutgoingMessageStatus as the contract numbers them.
const CREDIT_REMINDER = 3;
const SENT = 1;
const FAILED = 2;
const SKIPPED = 3;

const customerOf = (data: DemoData, name: string) => data.customers.find((candidate: { name: string }) => candidate.name === name);

/**
 * Who Café La Esquina sells on credit (fiado.mocks) with a term of 15 days and terms of their own (Fase 26C): the offices
 * of Grupo Altavista pay each month (30 days, nothing overdue), Roberto in 7 days and María Fernanda in 5, so what they
 * bought on 26 and 28 September is overdue. The reminders go out every 7 days with «Saldo vencido», a template approved
 * in Meta: Roberto got one yesterday, María Fernanda's did not reach her and Juan Carlos's was skipped once he paid.
 */
function withTermsAndReminders(data: DemoData) {
  withCredit(data);
  data.creditTermDays = 15;
  customerOf(data, 'Oficinas Grupo Altavista').creditTermDays = 30;
  customerOf(data, 'Roberto Sánchez Vega').creditTermDays = 7;
  customerOf(data, 'María Fernanda López').creditTermDays = 5;
  data.creditReminders = { enabled: true, everyDays: 7, templateId: 't-fiado' };

  const p2 = data.p2 ?? (data.p2 = phase2Data(data));
  p2.templates.push({
    id: 't-fiado', name: 'Saldo vencido', purpose: CREDIT_REMINDER,
    body: 'Hola {nombre}, en {negocio} tienes {vencido} vencido desde el {fecha}, de un saldo de {monto}. Si ya pagaste, ignora este mensaje. Escribe BAJA si no quieres recibir recordatorios.',
    providerTemplateName: 'saldo_vencido', languageCode: 'es_MX', isActive: true,
    parameters: ['nombre', 'negocio', 'vencido', 'fecha', 'monto'], createdAt: at(18, 10, -9),
  });
  const reminder = (id: string, name: string, body: string, status: number, createdAt: string, extra: object) => ({
    id, purpose: CREDIT_REMINDER, phone: phoneKey(customerOf(data, name)?.phone ?? ''), recipientName: name, body, status,
    attempts: status === SKIPPED ? 0 : 1, createdAt, nextAttemptAt: createdAt, ...extra,
  });
  const yesterday = at(10, 5, -1);
  p2.messages.push(
    reminder('cr-1', 'Roberto Sánchez Vega', 'Hola Roberto, en Café La Esquina tienes $360.00 vencido desde el sábado 3 de octubre, de un saldo de $360.00. Si ya pagaste, ignora este mensaje. Escribe BAJA si no quieres recibir recordatorios.',
      SENT, yesterday, { sentAt: yesterday, deliveredAt: yesterday }),
    reminder('cr-2', 'María Fernanda López', 'Hola María, en Café La Esquina tienes $146.00 vencido desde el sábado 3 de octubre, de un saldo de $316.00. Si ya pagaste, ignora este mensaje. Escribe BAJA si no quieres recibir recordatorios.',
      FAILED, at(10, 5, 0), { lastError: 'WhatsApp no lo entregó: el cliente no escribió en las últimas 24 horas. Usa una plantilla aprobada en Meta.' }),
    reminder('cr-3', 'Juan Carlos Hernández', 'Hola Juan, en Café La Esquina tienes $85.00 vencido desde el martes 29 de septiembre, de un saldo de $85.00. Si ya pagaste, ignora este mensaje. Escribe BAJA si no quieres recibir recordatorios.',
      SKIPPED, at(10, 5, -2), { lastError: 'Ya no tiene nada vencido.' }),
  );
  data.lastCreditReminders = { [phoneKey(customerOf(data, 'Roberto Sánchez Vega').phone) as string]: yesterday };
}

/**
 * The same business before setting up the reminders: without a term of its own (only Roberto pays in 7 days, so only
 * his sale of 26 September is overdue) and without WhatsApp connected.
 */
function withOwnTermWithoutWhatsApp(data: DemoData) {
  withCredit(data);
  customerOf(data, 'Roberto Sánchez Vega').creditTermDays = 7;
  const p2 = data.p2 ?? (data.p2 = phase2Data(data));
  p2.settings.whatsAppConnected = false;
}

export const CON_RECORDATORIOS = asOption(withTermsAndReminders);
export const SIN_WHATSAPP = asOption(withOwnTermWithoutWhatsApp);
