import type { CustomerAccount, CustomerAccountOrder, CustomerBalanceEntry } from '../../api/customerCredit';
import { CustomerBalanceEntryKind } from '../../api/generated/enums';
import { formatCalendarDate, formatDate } from '../../utils/dates';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney, parseMoneyInput } from '../../utils/money';

// Orders listed in the WhatsApp summary: more would make a message nobody reads to the end.
const SUMMARY_ORDERS = 10;

/**
 * The account as the customer reads it on WhatsApp: what they owe, order by order, and what they may still owe. The
 * business reviews it and taps send; nothing is sent by the server.
 */
export function accountSummaryText(account: CustomerAccount, businessName: string): string {
  const lines = [`*Estado de cuenta en ${businessName}*`];
  if (account.name) lines.push(account.name);
  lines.push('');
  // What they have in their favor (Fase 26D), whether they owe or not.
  const inFavor = account.creditBalance > 0 ? `Tienes *${formatMoney(account.creditBalance)}* a tu favor.` : null;
  if (account.owed <= 0) {
    if (inFavor) lines.push(inFavor, '');
    lines.push('No debes nada. ¡Gracias!');
    return lines.join('\n');
  }

  lines.push(`Debes *${formatMoney(account.owed)}*:`);
  account.orders.slice(0, SUMMARY_ORDERS).forEach((order) => {
    const due = order.isOverdue && order.dueOn ? ` (venció el ${formatCalendarDate(order.dueOn, { dateStyle: 'medium' })})` : '';
    lines.push(`• ${order.orderNumber} del ${formatDate(order.createdAt)}: ${formatMoney(order.balance)}${due}`);
  });
  const more = account.ordersCount - Math.min(account.orders.length, SUMMARY_ORDERS);
  if (more > 0) lines.push(more === 1 ? '• y 1 pedido más' : `• y ${more} pedidos más`);
  if (account.overdue > 0 && account.creditTermDays != null) {
    lines.push('', `De eso, *${formatMoney(account.overdue)}* ya venció: el plazo para pagar es de ${describeTerm(account.creditTermDays)}.`);
  }
  if (account.creditLimit != null && account.available != null) {
    lines.push('', `Tu límite de fiado es ${formatMoney(account.creditLimit)}: te quedan ${formatMoney(account.available)}.`);
  }
  if (inFavor) lines.push('', inFavor);
  lines.push('', '¡Gracias!');
  return lines.join('\n');
}

/**
 * A movement of the balance in their favor (Fase 26D), as the account lists it: what happened, how the money moved or the
 * order, e.g. «Dejó a favor · Efectivo» or «Pagó con saldo a favor · ORD-000123».
 */
export function describeBalanceEntry(entry: Pick<CustomerBalanceEntry, 'kind' | 'method' | 'orderNumber'>): string {
  const what = {
    [CustomerBalanceEntryKind.Deposit]: 'Dejó a favor',
    [CustomerBalanceEntryKind.Refund]: 'Reembolso a su favor',
    [CustomerBalanceEntryKind.Applied]: 'Pagó con saldo a favor',
    [CustomerBalanceEntryKind.Returned]: 'Se le devolvió',
  }[entry.kind];
  const detail = entry.method != null ? getPaymentMethodLabel(entry.method) : entry.orderNumber;
  return detail ? `${what} · ${detail}` : what;
}

/** Whole days since the order was made, as the list reads them. */
export function daysSince(createdAt: string, now: Date = new Date()): number {
  return Math.max(0, Math.floor((now.getTime() - new Date(createdAt).getTime()) / 86_400_000));
}

export function describeDays(days: number): string {
  if (days === 0) return 'hoy';
  return days === 1 ? 'hace 1 día' : `hace ${days} días`;
}

/** "15 días", or "1 día". */
export function describeTerm(days: number): string {
  return days === 1 ? '1 día' : `${days} días`;
}

/** When an order is due, in the account's words: "Vence el 16 oct 2026" or "Venció el 9 oct 2026"; null without a term. */
export function describeDue(order: Pick<CustomerAccountOrder, 'dueOn' | 'isOverdue'>): string | null {
  if (!order.dueOn) return null;
  const day = formatCalendarDate(order.dueOn, { dateStyle: 'medium' });
  return order.isOverdue ? `Venció el ${day}` : `Vence el ${day}`;
}

/** The highest limit the server takes (Customer.MaxCreditLimit). */
export const MAX_CREDIT_LIMIT = 1_000_000;

/** The term the server takes (TenantSettings.MinCreditTermDays and MaxCreditTermDays). */
export const MIN_CREDIT_TERM_DAYS = 1;
export const MAX_CREDIT_TERM_DAYS = 120;

/** What the term typed means, in the owner's words; null when it can be saved. */
export function creditTermProblem(typed: string): string | null {
  // Only digits: Number() would also take "1e1", "0x1F" or "15.0".
  if (!/^\d{1,3}$/.test(typed.trim())) return 'Escribe en cuántos días te pagan, por ejemplo 15.';
  const days = Number(typed.trim());
  if (days < MIN_CREDIT_TERM_DAYS || days > MAX_CREDIT_TERM_DAYS) return 'El plazo del fiado va de 1 a 120 días.';
  return null;
}

/** What the amount to forgive typed means; null when it can be sent (in cents, up to what is owed). */
export function writeOffProblem(typed: string, maxCents: number): string | null {
  const amount = parseMoneyInput(typed);
  if (typed.trim() === '' || amount === null || amount <= 0) return 'Escribe cuánto condonas, por ejemplo 50.';
  if (Math.round(amount * 100) > maxCents) return `No puedes condonar más de ${formatMoney(maxCents / 100)}.`;
  return null;
}

/** The days between two reminders the server takes (TenantSettings.MinCreditReminderEveryDays and MaxCreditReminderEveryDays). */
export const MIN_REMINDER_EVERY_DAYS = 1;
export const MAX_REMINDER_EVERY_DAYS = 30;

/** What the days between reminders typed mean, within the range the server reports; null when they can be saved. */
export function reminderEveryDaysProblem(typed: string, min = MIN_REMINDER_EVERY_DAYS, max = MAX_REMINDER_EVERY_DAYS): string | null {
  if (!/^\d{1,3}$/.test(typed.trim())) return 'Escribe cada cuántos días, por ejemplo 7.';
  const days = Number(typed.trim());
  if (days < min || days > max) return `Los recordatorios se repiten cada ${min} a ${max} días.`;
  return null;
}

/** The days before the last day to pay the server takes (TenantSettings.MinCreditDueSoonDaysBefore and MaxCreditDueSoonDaysBefore). */
export const MIN_DUE_SOON_DAYS_BEFORE = 1;
export const MAX_DUE_SOON_DAYS_BEFORE = 7;

/** What the days before the last day to pay typed mean, within the range the server reports (Fase 26E); null when they can be saved. */
export function dueSoonDaysBeforeProblem(typed: string, min = MIN_DUE_SOON_DAYS_BEFORE, max = MAX_DUE_SOON_DAYS_BEFORE): string | null {
  if (!/^\d{1,3}$/.test(typed.trim())) return 'Escribe cuántos días antes, por ejemplo 2.';
  const days = Number(typed.trim());
  if (days < min || days > max) return `El aviso se manda de ${min} a ${max} días antes.`;
  return null;
}

/**
 * The reminder of what is overdue (Fase 26C), or the notice of what comes due (Fase 26E), as a customer would read it, with
 * made-up amounts: the server fills in the real ones of each customer.
 */
export function previewReminder(body: string, businessName: string): string {
  const sample: Record<string, string> = {
    nombre: 'Ana',
    negocio: businessName,
    monto: formatMoney(1160),
    vencido: formatMoney(500),
    porvencer: formatMoney(500),
    fecha: 'lunes 5 de octubre',
  };
  return body.replace(/\{([a-zA-Z_]+)\}/g, (match, name: string) => sample[name.toLowerCase()] ?? match);
}

/**
 * What the account says of the WhatsApp reminders (Fase 26C): when the last one went out, and whether more will while
 * something stays overdue; null when there is nothing to say.
 */
export function describeReminder(account: Pick<CustomerAccount, 'lastReminderAt' | 'creditReminderEveryDays' | 'overdue'>): string | null {
  const every = account.creditReminderEveryDays;
  const repeats = every != null && account.overdue > 0 ? ` Se repite cada ${describeTerm(every)} mientras deba algo vencido.` : '';
  if (account.lastReminderAt) return `Último recordatorio por WhatsApp: ${formatDate(account.lastReminderAt)}.${repeats}`;
  if (repeats) return `Se le recordará lo vencido por WhatsApp.${repeats}`;
  return null;
}

/** What the account says of the notice before something comes due (Fase 26E): when the last one went out; null if none did. */
export function describeDueSoon(account: Pick<CustomerAccount, 'lastDueSoonAt'>): string | null {
  return account.lastDueSoonAt ? `Último aviso antes de vencer por WhatsApp: ${formatDate(account.lastDueSoonAt)}.` : null;
}

/**
 * What Por cobrar says of the business's WhatsApp messages of the fiado: the notice before something comes due (Fase 26E)
 * and the reminders of what is overdue (Fase 26C), each null while off.
 */
export function describeCreditMessages(reminderEveryDays: number | null, dueSoonDaysBefore: number | null): string {
  const notice = dueSoonDaysBefore != null ? `Aviso ${describeTerm(dueSoonDaysBefore)} antes de vencer` : null;
  if (reminderEveryDays == null) return notice ?? 'Sin recordatorios';
  const every = describeTerm(reminderEveryDays);
  return notice ? `${notice} · recordatorios cada ${every}` : `Recordatorios cada ${every}`;
}

/** What the limit typed means, in the owner's words; null when it can be saved. */
export function creditLimitProblem(typed: string): string | null {
  const limit = parseMoneyInput(typed);
  if (typed.trim() === '' || limit === null) return 'Escribe cuánto le fías, por ejemplo 500.';
  if (limit < 1 || limit > MAX_CREDIT_LIMIT) return 'El límite de fiado va de $1 a $1,000,000.';
  return null;
}
