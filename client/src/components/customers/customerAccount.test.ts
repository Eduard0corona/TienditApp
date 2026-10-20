import { describe, expect, it } from 'vitest';
import type { CustomerAccount } from '../../api/customerCredit';
import { accountSummaryText, creditLimitProblem, daysSince, describeDays } from './customerAccount';

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
