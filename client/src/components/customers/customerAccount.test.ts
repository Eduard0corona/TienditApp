import { describe, expect, it } from 'vitest';
import type { CustomerAccount } from '../../api/customerCredit';
import { OrderPaymentMethod } from '../../types';
import {
  accountSummaryText, creditLimitProblem, creditTermProblem, daysSince, describeBalanceEntry, describeCreditMessages, describeDays,
  describeDue, describeDueSoon, describeReminder, describeTerm, dueSoonDaysBeforeProblem, previewReminder, reminderEveryDaysProblem,
  writeOffProblem,
} from './customerAccount';

function account(overrides: Partial<CustomerAccount> = {}): CustomerAccount {
  return {
    phone: '+525512345678',
    name: 'Ana López',
    customerId: 'c1',
    creditLimit: 500,
    sellsOnCredit: true,
    owed: 150,
    available: 350,
    overLimit: false,
    orders: [
      { orderId: 'o1', orderNumber: 'ORD-000010', createdAt: '2026-10-10T18:00:00Z', status: 6, soldOnCredit: true, total: 100, paid: 0, balance: 100 },
      { orderId: 'o2', orderNumber: 'ORD-000014', createdAt: '2026-10-15T18:00:00Z', status: 6, soldOnCredit: false, total: 80, paid: 30, balance: 50 },
    ],
    ordersCount: 2,
    recentPayments: [],
    ...overrides,
  } as CustomerAccount;
}

describe('accountSummaryText', () => {
  it('lists what they owe order by order and what they may still owe', () => {
    const text = accountSummaryText(account(), 'Tiendita Lupita');

    expect(text).toContain('*Estado de cuenta en Tiendita Lupita*');
    expect(text).toContain('Debes *$150.00*:');
    expect(text).toContain('• ORD-000010 del ');
    expect(text).toContain(': $100.00');
    expect(text).toContain('Tu límite de fiado es $500.00: te quedan $350.00.');
  });

  it('counts the orders it leaves out, and says nothing of a limit they do not have', () => {
    const text = accountSummaryText(account({ ordersCount: 13, creditLimit: null, available: null }), 'Tiendita');

    expect(text).toContain('• y 11 pedidos más');
    expect(text).not.toContain('límite');
  });

  it('thanks a customer who owes nothing', () => {
    expect(accountSummaryText(account({ owed: 0, orders: [], ordersCount: 0 }), 'Tiendita')).toContain('No debes nada. ¡Gracias!');
  });
});

describe('creditLimitProblem', () => {
  it.each([
    ['', 'Escribe cuánto le fías, por ejemplo 500.'],
    ['abc', 'Escribe cuánto le fías, por ejemplo 500.'],
    ['0.50', 'El límite de fiado va de $1 a $1,000,000.'],
    ['1,000,000.01', 'El límite de fiado va de $1 a $1,000,000.'],
  ])('refuses %j', (typed, problem) => {
    expect(creditLimitProblem(typed)).toBe(problem);
  });

  it.each(['1', '500', '$1,500.50', '1000000'])('accepts %j', (typed) => {
    expect(creditLimitProblem(typed)).toBeNull();
  });
});

describe('days since', () => {
  it('counts whole days and says them as people do', () => {
    const now = new Date('2026-10-19T12:00:00Z');

    expect(describeDays(daysSince('2026-10-19T08:00:00Z', now))).toBe('hoy');
    expect(describeDays(daysSince('2026-10-18T08:00:00Z', now))).toBe('hace 1 día');
    expect(describeDays(daysSince('2026-10-09T12:00:00Z', now))).toBe('hace 10 días');
  });
});

describe('term and overdue', () => {
  it('says when each order is due, and nothing without a term', () => {
    expect(describeDue({ dueOn: '2026-10-17', isOverdue: true })).toBe('Venció el 17 oct 2026');
    expect(describeDue({ dueOn: '2026-10-22', isOverdue: false })).toBe('Vence el 22 oct 2026');
    expect(describeDue({ dueOn: null, isOverdue: false })).toBeNull();
    expect([describeTerm(1), describeTerm(15)]).toEqual(['1 día', '15 días']);
  });

  it('tells the customer what is overdue in the WhatsApp summary', () => {
    const text = accountSummaryText(account({
      creditTermDays: 7,
      overdue: 100,
      orders: [
        { orderId: 'o1', orderNumber: 'ORD-000010', createdAt: '2026-10-10T18:00:00Z', status: 6, soldOnCredit: true, total: 100, paid: 0, writtenOff: 0, balance: 100, dueOn: '2026-10-17', isOverdue: true },
      ],
    }), 'Tiendita');

    expect(text).toContain('• ORD-000010 del ');
    expect(text).toContain(': $100.00 (venció el 17 oct 2026)');
    expect(text).toContain('De eso, *$100.00* ya venció: el plazo para pagar es de 7 días.');
  });

  it.each([
    ['', 'Escribe en cuántos días te pagan, por ejemplo 15.'],
    ['7.5', 'Escribe en cuántos días te pagan, por ejemplo 15.'],
    // Number() would take these as 10, 31 and 15: only digits are a term.
    ['1e1', 'Escribe en cuántos días te pagan, por ejemplo 15.'],
    ['0x1F', 'Escribe en cuántos días te pagan, por ejemplo 15.'],
    ['15.0', 'Escribe en cuántos días te pagan, por ejemplo 15.'],
    ['0', 'El plazo del fiado va de 1 a 120 días.'],
    ['121', 'El plazo del fiado va de 1 a 120 días.'],
  ])('refuses the term %j', (typed, problem) => {
    expect(creditTermProblem(typed)).toBe(problem);
  });

  it.each(['1', '15', '120'])('accepts the term %j', (typed) => {
    expect(creditTermProblem(typed)).toBeNull();
  });

  it('refuses to forgive nothing or more than is owed', () => {
    expect(writeOffProblem('', 5000)).toBe('Escribe cuánto condonas, por ejemplo 50.');
    expect(writeOffProblem('0', 5000)).toBe('Escribe cuánto condonas, por ejemplo 50.');
    expect(writeOffProblem('10.005', 5000)).toBe('Escribe cuánto condonas, por ejemplo 50.');
    expect(writeOffProblem('50.01', 5000)).toBe('No puedes condonar más de $50.00.');
    expect(writeOffProblem('$50', 5000)).toBeNull();
  });
});

describe('credit reminders', () => {
  it('says when the last one went out, and whether more will while something stays overdue', () => {
    expect(describeReminder({ lastReminderAt: '2026-10-12T17:00:00Z', creditReminderEveryDays: 7, overdue: 100 }))
      .toBe('Último recordatorio por WhatsApp: 12 oct 2026. Se repite cada 7 días mientras deba algo vencido.');
    expect(describeReminder({ lastReminderAt: '2026-10-12T17:00:00Z', creditReminderEveryDays: null, overdue: 100 }))
      .toBe('Último recordatorio por WhatsApp: 12 oct 2026.');
    expect(describeReminder({ lastReminderAt: null, creditReminderEveryDays: 1, overdue: 100 }))
      .toBe('Se le recordará lo vencido por WhatsApp. Se repite cada 1 día mientras deba algo vencido.');
    // Nothing overdue, or no reminders and none sent: nothing to say.
    expect(describeReminder({ lastReminderAt: null, creditReminderEveryDays: 7, overdue: 0 })).toBeNull();
    expect(describeReminder({ lastReminderAt: null, creditReminderEveryDays: null, overdue: 100 })).toBeNull();
  });

  it.each([
    ['', 'Escribe cada cuántos días, por ejemplo 7.'],
    ['7.5', 'Escribe cada cuántos días, por ejemplo 7.'],
    ['0', 'Los recordatorios se repiten cada 1 a 30 días.'],
    ['31', 'Los recordatorios se repiten cada 1 a 30 días.'],
  ])('refuses reminders every %j days', (typed, problem) => {
    expect(reminderEveryDaysProblem(typed)).toBe(problem);
  });

  it.each(['1', '7', '30'])('accepts reminders every %j days', (typed) => {
    expect(reminderEveryDaysProblem(typed)).toBeNull();
  });

  it('follows the range the server reports', () => {
    expect(reminderEveryDaysProblem('45', 1, 60)).toBeNull();
    expect(reminderEveryDaysProblem('3', 5, 60)).toBe('Los recordatorios se repiten cada 5 a 60 días.');
  });

  it('shows the message with made-up amounts and the business name, and leaves what it does not know', () => {
    expect(previewReminder('Hola {nombre}, en {Negocio} debes {monto}; {vencido} venció el {fecha}. {cupon}', 'Tiendita'))
      .toBe('Hola Ana, en Tiendita debes $1,160.00; $500.00 venció el lunes 5 de octubre. {cupon}');
  });
});

describe('the notice before something comes due (Fase 26E)', () => {
  it.each([
    ['', 'Escribe cuántos días antes, por ejemplo 2.'],
    ['1.5', 'Escribe cuántos días antes, por ejemplo 2.'],
    ['0', 'El aviso se manda de 1 a 7 días antes.'],
    ['8', 'El aviso se manda de 1 a 7 días antes.'],
  ])('refuses a notice %j days before', (typed, problem) => {
    expect(dueSoonDaysBeforeProblem(typed)).toBe(problem);
  });

  it.each(['1', '2', ' 7 '])('accepts a notice %j days before', (typed) => {
    expect(dueSoonDaysBeforeProblem(typed)).toBeNull();
  });

  it('follows the range the server reports', () => {
    expect(dueSoonDaysBeforeProblem('10', 1, 14)).toBeNull();
    expect(dueSoonDaysBeforeProblem('2', 3, 14)).toBe('El aviso se manda de 3 a 14 días antes.');
  });

  it('shows what comes due with made-up amounts', () => {
    expect(previewReminder('Hola {nombre}, en {negocio} debes {monto}; {porvencer} vence el {fecha}.', 'Tiendita'))
      .toBe('Hola Ana, en Tiendita debes $1,160.00; $500.00 vence el lunes 5 de octubre.');
  });

  it('says in Por cobrar which messages go out, and in the account when the last notice did', () => {
    expect(describeCreditMessages(null, null)).toBe('Sin recordatorios');
    expect(describeCreditMessages(7, null)).toBe('Recordatorios cada 7 días');
    expect(describeCreditMessages(null, 1)).toBe('Aviso 1 día antes de vencer');
    expect(describeCreditMessages(7, 2)).toBe('Aviso 2 días antes de vencer · recordatorios cada 7 días');
    expect(describeDueSoon({ lastDueSoonAt: '2026-10-12T17:00:00Z' })).toBe('Último aviso antes de vencer por WhatsApp: 12 oct 2026.');
    expect(describeDueSoon({ lastDueSoonAt: null })).toBeNull();
  });
});

describe('the balance in their favor (Fase 26D)', () => {
  it('says what they have in their favor, whether they owe or not', () => {
    expect(accountSummaryText(account({ creditBalance: 80 }), 'Tiendita')).toContain('Tienes *$80.00* a tu favor.');
    const nothingOwed = accountSummaryText(account({ owed: 0, orders: [], ordersCount: 0, creditBalance: 80 }), 'Tiendita');
    expect(nothingOwed).toContain('Tienes *$80.00* a tu favor.');
    expect(nothingOwed).toContain('No debes nada. ¡Gracias!');
    expect(accountSummaryText(account({ creditBalance: 0 }), 'Tiendita')).not.toContain('a tu favor');
  });

  it('describes each movement by what happened, how the money moved or the order', () => {
    expect(describeBalanceEntry({ kind: 0, method: OrderPaymentMethod.Cash, orderNumber: null })).toBe('Dejó a favor · Efectivo');
    expect(describeBalanceEntry({ kind: 1, method: undefined, orderNumber: 'ORD-000020' })).toBe('Reembolso a su favor · ORD-000020');
    expect(describeBalanceEntry({ kind: 2, method: undefined, orderNumber: 'ORD-000021' })).toBe('Pagó con saldo a favor · ORD-000021');
    expect(describeBalanceEntry({ kind: 3, method: OrderPaymentMethod.Transfer, orderNumber: null })).toBe('Se le devolvió · Transferencia');
    expect(describeBalanceEntry({ kind: 2, method: undefined, orderNumber: null })).toBe('Pagó con saldo a favor');
  });
});
