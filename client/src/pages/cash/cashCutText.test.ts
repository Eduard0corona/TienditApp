import { describe, expect, it } from 'vitest';
import type { CashSessionDto, DailyCashCutDto } from '../../types';
import { CashSessionStatus } from '../../types';
import { buildDailyCutText, buildShiftCutText, getOtherMethodsTotal } from './cashCutText';

const plain = (text: string) => text.replace(/\u00a0/g, ' ');

const cut = (patch: Partial<DailyCashCutDto> = {}): DailyCashCutDto => ({
  date: '2026-10-05',
  salesPaymentsTotal: 1250,
  salesPaymentsCount: 12,
  cashTotal: 800,
  cardTotal: 400,
  transferTotal: 49.9,
  walletTotal: 0.1,
  otherTotal: 0.2,
  manualIncomeTotal: 0,
  manualExpenseTotal: 20,
  cashManualIncomeTotal: 0,
  cashManualExpenseTotal: 20,
  netCashFlow: 1230,
  netCashOnlyFlow: 780,
  tipsTotal: 0,
  cashTipsTotal: 0,
  byUser: [{ userName: 'Ana', paymentsCount: 8, totalAmount: 900 }],
  byLocation: [],
  byBranch: [],
  ...patch,
});

const session = (patch: Partial<CashSessionDto> = {}): CashSessionDto => ({
  id: 's-1',
  openedAt: '2026-10-05T14:00:00Z',
  openedBy: 'Ana',
  openingAmount: 500,
  status: CashSessionStatus.Closed,
  closedAt: '2026-10-05T22:00:00Z',
  closedBy: 'Ana',
  closingAmount: 1284,
  inflows: 800,
  outflows: 0,
  nonCashInflows: 400,
  expectedBalance: 1300,
  expectedClosingAmount: 1300,
  closingDifference: -16,
  closingNotes: 'Faltó cambio',
  isMine: true,
  branchName: 'Centro',
  cashCount: [],
  ...patch,
});

describe('cash cut text', () => {
  it('adds wallet and other methods in cents', () => {
    expect(getOtherMethodsTotal(cut())).toBe(0.3);
  });

  it('writes the day cut with the same figures as the printed one', () => {
    const text = plain(buildDailyCutText(cut(), {
      businessName: 'La Esquina',
      dateLabel: '5 de octubre de 2026',
      scopeLabel: 'Todas las sucursales',
    }));

    expect(text).toContain('*La Esquina*\n*Corte del día* · 5 de octubre de 2026\nTodas las sucursales');
    expect(text).toContain('*Cobrado: $1,250.00* (12 pagos)');
    expect(text).toContain('Otros: $0.30');
    expect(text).toContain('Neto en efectivo: $780.00');
    expect(text).not.toContain('Propinas');
    expect(text).toContain('• Ana: 8 pagos / $900.00');
  });

  it('names each user of the day cut, or keeps the user when there is no name', () => {
    const text = plain(buildDailyCutText(cut({
      byUser: [
        { userName: 'ana.lopez@laesquina.mx', displayName: 'Ana López', paymentsCount: 8, totalAmount: 900 },
        { userName: 'beto@laesquina.mx', displayName: null, paymentsCount: 3, totalAmount: 300 },
        { userName: 'Sistema', paymentsCount: 2, totalAmount: 50 },
      ],
    }), { dateLabel: 'Hoy', scopeLabel: 'Centro' }));

    expect(text).toContain('• Ana López: 8 pagos / $900.00\n• beto@laesquina.mx: 3 pagos / $300.00\n• Sistema: 2 pagos / $50.00');
    expect(text).not.toContain('ana.lopez@');
  });

  it('shows the tips apart when there are any', () => {
    const text = plain(buildDailyCutText(cut({ tipsTotal: 60, cashTipsTotal: 20 }), { dateLabel: 'Hoy', scopeLabel: 'Centro' }));

    expect(text).toContain('Propinas: $60.00\nPropinas en efectivo: $20.00');
  });

  it('writes a closed shift with what it should have held, what was counted and the difference', () => {
    const text = plain(buildShiftCutText(session(), 'La Esquina'));

    expect(text).toContain('*Corte de caja*\nAna · Centro');
    expect(text).toContain('*Debía haber: $1,300.00*\n*Contado: $1,284.00*\n*Diferencia: Faltan $16.00*');
    expect(text).toContain('Nota: Faltó cambio');
  });

  it('writes who opened and closed a shift by name, never with their email', () => {
    const text = plain(buildShiftCutText(session({
      openedBy: 'ana.lopez@laesquina.mx',
      openedByName: 'Ana López',
      closedBy: 'beto@laesquina.mx',
      closedByName: 'Beto Ruiz',
    })));

    expect(text).toContain('*Corte de caja*\nAna López · Centro');
    expect(text).toMatch(/^Cerró \(Beto Ruiz\): /m);
    expect(text).not.toContain('@');
  });

  it('marks an open drawer as a partial cut without a count', () => {
    const text = plain(buildShiftCutText(session({
      status: CashSessionStatus.Open,
      closedAt: undefined,
      closingAmount: undefined,
      expectedClosingAmount: null,
      closingDifference: null,
      closingNotes: null,
    })));

    expect(text).toContain('*Corte parcial (caja abierta)*');
    expect(text).toContain('*Debía haber: $1,300.00*');
    expect(text).not.toContain('Contado');
  });
});
