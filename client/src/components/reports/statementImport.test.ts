import { describe, expect, it } from 'vitest';
import type { PlatformSettlementOrder, PlatformStatementMatch } from '../../api/platformSettlements';
import { OrderStatus, PlatformCollectedBy, PlatformSettlementAdjustmentKind, PlatformStatementMatchStatus } from '../../types';
import { newSettlementForm, type SettlementForm } from './settlementEdits';
import {
  guessColumns,
  kindForConcept,
  parseStatementAmount,
  proposeFromMatch,
  readStatement,
  rememberedColumns,
} from './statementImport';

function order(orderId: string, folio: string): PlatformSettlementOrder {
  return {
    orderId,
    orderNumber: `ORD-${orderId}`,
    externalOrderNumber: folio,
    registeredAt: '2026-10-06T20:00:00Z',
    registeredOn: '2026-10-06',
    inPeriod: true,
    status: OrderStatus.Delivered,
    collectedBy: PlatformCollectedBy.Platform,
    orderTotal: 1000,
    refundedAmount: 0,
    expectedNet: 561.48,
    currentExpectedNet: 561.48,
  };
}

function match(patch: Partial<PlatformStatementMatch> = {}): PlatformStatementMatch {
  return {
    folios: [
      { folio: 'R1', rows: [2, 5], statementAmount: 541.48, status: PlatformStatementMatchStatus.Pending, orders: [order('a', 'R1')], expectedNet: 561.48, difference: -20 },
      { folio: 'R6', rows: [3], statementAmount: 561.48, status: PlatformStatementMatchStatus.InThisSettlement, orders: [order('b', 'R6')], expectedNet: 561.48, difference: 0 },
      { folio: 'R4', rows: [7], statementAmount: 561.48, status: PlatformStatementMatchStatus.AfterPeriod, orders: [order('c', 'R4')] },
      { folio: 'R8', rows: [8], statementAmount: 100, status: PlatformStatementMatchStatus.Unknown, orders: [] },
      { folio: 'R7', rows: [9], statementAmount: 561.48, status: PlatformStatementMatchStatus.Ambiguous, orders: [order('d', 'R7'), order('e', 'R7')] },
    ],
    concepts: [
      { concept: 'Publicidad', rows: [10, 11], amount: -75 },
      { concept: 'Promoción compartida', rows: [12], amount: -30 },
      { concept: '', rows: [13], amount: 0 },
    ],
    unreadable: [{ row: 14, folio: 'R/9', concept: 'Pedido', amount: 5 }],
    statementTotal: 2225.92,
    matchedTotal: 1102.96,
    expectedTotal: 1122.96,
    conceptsTotal: -105,
    unmatchedTotal: 666.48,
    ...patch,
  };
}

function form(patch: Partial<SettlementForm> = {}): SettlementForm {
  return { ...newSettlementForm('2026-10-14'), ...patch };
}

describe('statementImport', () => {
  it('guesses the columns of the platforms\' files by their headers, and remembers the person\'s by name', () => {
    expect(guessColumns(['Fecha', 'ID del pedido', 'Descripción', 'Monto neto'])).toEqual({ folio: 1, amount: 3, concept: 2 });
    expect(guessColumns(['Order ID', 'Type', 'Total', 'Net payout'])).toEqual({ folio: 0, amount: 3, concept: 1 });
    expect(guessColumns(['Folio', 'Importe'])).toEqual({ folio: 0, amount: 1, concept: -1 });
    expect(guessColumns(['A', 'B'])).toEqual({ folio: -1, amount: -1, concept: -1 });

    const remembered = { folio: 'id del pedido', amount: 'Total', concept: '' };
    expect(rememberedColumns(['Total', 'ID DEL PEDIDO'], remembered)).toEqual({ folio: 1, amount: 0, concept: -1 });
    expect(rememberedColumns(['Folio', 'Importe'], remembered)).toBeNull();
    expect(rememberedColumns(['Folio'], null)).toBeNull();
  });

  it('reads the amounts as the statements write them', () => {
    const cases: [string, number | undefined][] = [
      ['561.48', 561.48], ['$1,234.56', 1234.56], ['-1,234.56', -1234.56], ['(1,234.56)', -1234.56], ['1.234,56', 1234.56],
      ['12,5', 12.5], ['1,234', 1234], ['1.234.567', 1234567], ['−80', -80], ['80-', -80], ['1234.5 MXN', 1234.5], ['-$12', -12],
      ['0', 0], ['', undefined], ['12.345', undefined], ['abc', undefined], ['1,23,4', undefined], ['10000000.01', undefined],
    ];
    for (const [text, expected] of cases) expect(parseStatementAmount(text), text).toBe(expected);
  });

  it('reads the lines with the columns chosen, leaving out totals and what cannot be read', () => {
    const rows = [
      { line: 2, cells: ['152390', 'Pedido', '561.48'] },
      { line: 3, cells: ['', 'Publicidad', '-100'] },
      { line: 4, cells: ['', '', ''] },
      { line: 5, cells: ['152001', 'Pedido', 'n/d'] },
      { line: 6, cells: ['9'.repeat(61), 'Pedido', '1'] },
      { line: 7, cells: ['Total', '', '461.48'] },
      { line: 8, cells: ['', 'Total general', '461.48'] },
      { line: 9, cells: ['152002', `  Pedido   con   ${'x'.repeat(250)}`, '10'] },
    ];

    const reading = readStatement(rows, { folio: 0, amount: 2, concept: 1 });

    expect(reading.lines.map((line) => [line.row, line.folio, line.amount, line.concept?.length ?? 0])).toEqual([
      [2, '152390', 561.48, 6], [3, null, -100, 10], [9, '152002', 10, 200],
    ]);
    expect(reading.lines[2].concept?.startsWith('Pedido con xxx')).toBe(true);
    expect(reading.skipped).toEqual([
      { line: 5, reason: 'No se pudo leer el importe «n/d».' },
      { line: 6, reason: 'Su folio es demasiado largo para ser de la plataforma.' },
      { line: 7, reason: 'Es un renglón de totales del archivo.' },
      { line: 8, reason: 'Es un renglón de totales del archivo.' },
    ]);
    expect(reading.totalCents).toBe(47148);
  });

  it('suggests the reason of each concept by its words', () => {
    expect(kindForConcept('Promoción compartida')).toBe(PlatformSettlementAdjustmentKind.Promotion);
    expect(kindForConcept('Reclamo de cliente')).toBe(PlatformSettlementAdjustmentKind.Claim);
    expect(kindForConcept('Cancelación tardía')).toBe(PlatformSettlementAdjustmentKind.Cancellation);
    expect(kindForConcept('Publicidad')).toBe(PlatformSettlementAdjustmentKind.Fee);
    expect(kindForConcept('Ads')).toBe(PlatformSettlementAdjustmentKind.Fee);
    expect(kindForConcept('Saldo anterior')).toBe(PlatformSettlementAdjustmentKind.PreviousBalance);
    expect(kindForConcept('Propinas')).toBe(PlatformSettlementAdjustmentKind.Other);
  });

  it('proposes the orders it takes and one adjustment for each thing the orders do not explain', () => {
    const proposal = proposeFromMatch(match(), form(), 'rappi.csv', 13);

    expect(proposal.orderIds).toEqual(['a', 'b']);
    expect(proposal.orders.map((o) => o.orderId)).toEqual(['a', 'b']);
    expect(proposal.adjustments.map((row) => [row.kind, row.description, row.amountText])).toEqual([
      [PlatformSettlementAdjustmentKind.Other, 'Estado de cuenta: diferencia en 1 pedido', '-20.00'],
      [PlatformSettlementAdjustmentKind.Other, 'Estado de cuenta: folios que no entran en esta liquidación (R4, R8, R/9)', '666.48'],
      [PlatformSettlementAdjustmentKind.Fee, 'Estado de cuenta: Publicidad', '-75.00'],
      [PlatformSettlementAdjustmentKind.Promotion, 'Estado de cuenta: Promoción compartida', '-30.00'],
    ]);
    expect(proposal.importedAdjustments).toBe(4);
    // The ambiguous folio is left for the person; its amount is in no adjustment.
    expect(proposal.ambiguousFolios).toEqual(['R7']);
    expect(proposal.depositedText).toBe('2225.92');
    expect(proposal.notes).toBe('Estado de cuenta: rappi.csv, 13 renglones.');
  });

  it('replaces the adjustments of a statement imported before and keeps those written by hand, the deposit and the notes', () => {
    const before = form({
      depositedText: '2000.00',
      notes: 'Depósito del martes.',
      adjustments: [
        { key: 'k1', kind: PlatformSettlementAdjustmentKind.Claim, description: 'Faltó un refresco', amountText: '-25.00' },
        { key: 'k2', kind: PlatformSettlementAdjustmentKind.Fee, description: 'Estado de cuenta: Publicidad', amountText: '-75.00' },
      ],
    });

    const proposal = proposeFromMatch(match({ folios: [], unreadable: [], concepts: [{ concept: 'Publicidad', rows: [2], amount: -80 }] }), before, 'b.csv', 1);

    expect(proposal.adjustments.map((row) => row.description)).toEqual(['Faltó un refresco', 'Estado de cuenta: Publicidad']);
    expect(proposal.adjustments[1].amountText).toBe('-80.00');
    expect(proposal.depositedText).toBe('2000.00');
    expect(proposal.notes).toBe('Depósito del martes. Estado de cuenta: b.csv, 1 renglón.');
    expect(proposeFromMatch(match(), form({ notes: 'n'.repeat(480) }), 'rappi.csv', 13).notes).toBe('n'.repeat(480));
  });

  it('never passes 100 adjustments: the concepts that do not fit go together', () => {
    const handWritten = Array.from({ length: 97 }, (_, index) => ({
      key: `k${index}`, kind: PlatformSettlementAdjustmentKind.Fee, description: `Cargo ${index}`, amountText: '-1.00',
    }));
    const concepts = [1, 2, 3, 4].map((index) => ({ concept: `Cargo extra ${index}`, rows: [index], amount: -index }));

    const proposal = proposeFromMatch(match({ folios: [], unreadable: [], concepts }), form({ adjustments: handWritten }), 'c.csv', 4);

    expect(proposal.adjustments).toHaveLength(100);
    expect(proposal.adjustments.slice(97).map((row) => [row.description, row.amountText])).toEqual([
      ['Estado de cuenta: Cargo extra 1', '-1.00'],
      ['Estado de cuenta: Cargo extra 2', '-2.00'],
      ['Estado de cuenta: otros 2 conceptos', '-7.00'],
    ]);
  });
});
