import { describe, expect, it } from 'vitest';
import type { PlatformSettlementDetail, PlatformSettlementOrder } from '../../api/platformSettlements';
import { OrderChannel, OrderStatus, PlatformCollectedBy, PlatformSettlementAdjustmentKind, PlatformSettlementStatus } from '../../types';
import {
  ADJUSTMENT_AMOUNT_MESSAGE,
  ADJUSTMENT_DESCRIPTION_REQUIRED_MESSAGE,
  ADJUSTMENT_DESCRIPTION_TOO_LONG_MESSAGE,
  ADJUSTMENT_KIND_MESSAGE,
  CHOOSE_THE_DEPOSIT_DATE_MESSAGE,
  CHOOSE_THE_PERIOD_MESSAGE,
  DEPOSIT_BEFORE_PERIOD_MESSAGE,
  DEPOSIT_IN_THE_FUTURE_MESSAGE,
  DEPOSITED_AMOUNT_MESSAGE,
  DIFFERENCE_DESCRIPTION,
  MAX_ADJUSTMENT_AMOUNT,
  MAX_DEPOSITED_AMOUNT,
  PERIOD_IN_THE_FUTURE_MESSAGE,
  PERIOD_ORDER_MESSAGE,
  PERIOD_TOO_LONG_MESSAGE,
  PERIOD_TOO_OLD_MESSAGE,
  TOO_MANY_ORDERS_MESSAGE,
  buildBody,
  changedSinceSaved,
  checkAdjustment,
  depositDateProblem,
  differenceAdjustment,
  editedSince,
  firstProblem,
  formOf,
  isPicked,
  mergeOrders,
  newSettlementForm,
  parseSignedAmount,
  periodProblem,
  placementOf,
  sameBody,
  savedBody,
  savedTotals,
  totalsOf,
  type AdjustmentRow,
  type SettlementForm,
} from './settlementEdits';

const TODAY = '2026-10-14';

function order(orderId: string, registeredOn: string, patch: Partial<PlatformSettlementOrder> = {}): PlatformSettlementOrder {
  return {
    orderId,
    orderNumber: `ORD-${orderId}`,
    externalOrderNumber: `R-${orderId}`,
    registeredAt: `${registeredOn}T20:00:00Z`,
    registeredOn,
    inPeriod: true,
    status: OrderStatus.Delivered,
    collectedBy: PlatformCollectedBy.Platform,
    orderTotal: 1000,
    refundedAmount: 0,
    expectedNet: 561.48,
    currentExpectedNet: 561.48,
    ...patch,
  };
}

function form(patch: Partial<SettlementForm> = {}): SettlementForm {
  return {
    from: '2026-10-05',
    to: '2026-10-11',
    depositedOn: '2026-10-13',
    depositedText: '1122.96',
    reference: '',
    notes: '',
    adjustments: [],
    picks: {},
    ...patch,
  };
}

function adjustment(patch: Partial<AdjustmentRow> = {}): AdjustmentRow {
  return { key: 'a1', kind: PlatformSettlementAdjustmentKind.Promotion, description: '', amountText: '-50', ...patch };
}

function detail(patch: Partial<PlatformSettlementDetail> = {}): PlatformSettlementDetail {
  return {
    settlement: {
      id: 's1',
      channel: OrderChannel.Rappi,
      status: PlatformSettlementStatus.Open,
      periodFrom: '2026-10-05',
      periodTo: '2026-10-11',
      depositedOn: '2026-10-13',
      depositedAmount: 1072.96,
      reference: 'SPEI 4471',
      notes: null,
      ordersTotal: 1122.96,
      adjustmentsTotal: -50,
      expectedDeposit: 1072.96,
      difference: 0,
      orderCount: 2,
      revision: 3,
      createdAt: '2026-10-13T18:00:00Z',
    },
    orders: [order('a', '2026-10-06'), order('b', '2026-10-09')],
    adjustments: [{ kind: PlatformSettlementAdjustmentKind.Promotion, description: 'Cupón 2x1', amount: -50 }],
    candidates: [],
    candidatesTruncated: false,
    ...patch,
  };
}

describe('settlementEdits', () => {
  it('reads an amount with its sign as a person types it, as the API would take it', () => {
    expect(parseSignedAmount('1,250.50', MAX_DEPOSITED_AMOUNT)).toBe(1250.5);
    expect(parseSignedAmount(' $300 ', MAX_DEPOSITED_AMOUNT)).toBe(300);
    expect(parseSignedAmount('-80', MAX_DEPOSITED_AMOUNT)).toBe(-80);
    // The minus of a phone keyboard or a pasted statement.
    expect(parseSignedAmount('−$1,000.10', MAX_DEPOSITED_AMOUNT)).toBe(-1000.1);
    expect(parseSignedAmount('-0', MAX_DEPOSITED_AMOUNT)).toBe(0);
    expect(parseSignedAmount('10,000,000', MAX_DEPOSITED_AMOUNT)).toBe(10_000_000);

    expect(parseSignedAmount('', MAX_DEPOSITED_AMOUNT)).toBeUndefined();
    // A comma only groups thousands: «32,50» is not read as $3,250.
    expect(parseSignedAmount('32,50', MAX_DEPOSITED_AMOUNT)).toBeUndefined();
    expect(parseSignedAmount('12.345', MAX_DEPOSITED_AMOUNT)).toBeUndefined();
    expect(parseSignedAmount('10,000,000.01', MAX_DEPOSITED_AMOUNT)).toBeUndefined();
    expect(parseSignedAmount('-1000000.01', MAX_ADJUSTMENT_AMOUNT)).toBeUndefined();
    expect(parseSignedAmount('abc', MAX_DEPOSITED_AMOUNT)).toBeUndefined();
  });

  it('says what is wrong with the period and the date of the deposit, as the server', () => {
    expect(periodProblem('2026-10-05', '2026-10-11', TODAY)).toBe('');
    expect(periodProblem('', '2026-10-11', TODAY)).toBe(CHOOSE_THE_PERIOD_MESSAGE);
    expect(periodProblem('2026-10-11', '2026-10-05', TODAY)).toBe(PERIOD_ORDER_MESSAGE);
    // Up to 31 days, both ends counted.
    expect(periodProblem('2026-09-01', '2026-10-01', TODAY)).toBe('');
    expect(periodProblem('2026-09-01', '2026-10-02', TODAY)).toBe(PERIOD_TOO_LONG_MESSAGE);
    expect(periodProblem('2026-10-12', '2026-10-15', TODAY)).toBe(PERIOD_IN_THE_FUTURE_MESSAGE);
    // 0026 for 2026: the year half typed in a date box.
    expect(periodProblem('0026-10-05', '0026-10-11', TODAY)).toBe(PERIOD_TOO_OLD_MESSAGE);
    expect(periodProblem('2000-01-01', '2000-01-07', TODAY)).toBe('');

    expect(depositDateProblem('2026-10-13', '2026-10-05', TODAY)).toBe('');
    // A platform that pays every day may deposit inside the period.
    expect(depositDateProblem('2026-10-05', '2026-10-05', TODAY)).toBe('');
    expect(depositDateProblem('', '2026-10-05', TODAY)).toBe(CHOOSE_THE_DEPOSIT_DATE_MESSAGE);
    expect(depositDateProblem('2026-10-04', '2026-10-05', TODAY)).toBe(DEPOSIT_BEFORE_PERIOD_MESSAGE);
    expect(depositDateProblem('2026-10-15', '2026-10-05', TODAY)).toBe(DEPOSIT_IN_THE_FUTURE_MESSAGE);
  });

  it('places an order before, in or after the period by its date of the business', () => {
    expect(placementOf('2026-10-04', '2026-10-05', '2026-10-11')).toBe('before');
    expect(placementOf('2026-10-05', '2026-10-05', '2026-10-11')).toBe('in');
    expect(placementOf('2026-10-11', '2026-10-05', '2026-10-11')).toBe('in');
    expect(placementOf('2026-10-12', '2026-10-05', '2026-10-11')).toBe('after');
  });

  it('checks each adjustment as the API: its reason, its amount and «Otro» with a description', () => {
    expect(checkAdjustment(adjustment())).toEqual({ amount: -50, kindProblem: '', descriptionProblem: '', amountProblem: '' });
    expect(checkAdjustment(adjustment({ kind: '' })).kindProblem).toBe(ADJUSTMENT_KIND_MESSAGE);
    expect(checkAdjustment(adjustment({ amountText: '0' }))).toMatchObject({ amount: undefined, amountProblem: ADJUSTMENT_AMOUNT_MESSAGE });
    expect(checkAdjustment(adjustment({ amountText: '' })).amountProblem).toBe(ADJUSTMENT_AMOUNT_MESSAGE);
    expect(checkAdjustment(adjustment({ amountText: '1000000.01' })).amountProblem).toBe(ADJUSTMENT_AMOUNT_MESSAGE);
    expect(checkAdjustment(adjustment({ kind: PlatformSettlementAdjustmentKind.Other, description: '   ' })).descriptionProblem)
      .toBe(ADJUSTMENT_DESCRIPTION_REQUIRED_MESSAGE);
    expect(checkAdjustment(adjustment({ kind: PlatformSettlementAdjustmentKind.Other, description: 'Bono' })).descriptionProblem).toBe('');
    expect(checkAdjustment(adjustment({ description: 'x'.repeat(201) })).descriptionProblem).toBe(ADJUSTMENT_DESCRIPTION_TOO_LONG_MESSAGE);
  });

  it('proposes the orders of the period in a new settlement and the saved ones in an existing one, never one after it', () => {
    const before = order('before', '2026-10-02');
    const inPeriod = order('in', '2026-10-07');
    const after = order('after', '2026-10-12');
    const none = new Set<string>();

    expect([before, inPeriod, after].map((each) => isPicked(each, form(), none, true))).toEqual([false, true, false]);
    // What the person chose wins, except an order after the period.
    const picks = { before: true, in: false, after: true };
    expect([before, inPeriod, after].map((each) => isPicked(each, form({ picks }), none, true))).toEqual([true, false, false]);
    // A saved settlement keeps its own orders and proposes nothing else.
    expect([before, inPeriod].map((each) => isPicked(each, form(), new Set(['before']), false))).toEqual([true, false]);
  });

  it('lists the orders of the settlement and the pending ones once each, the oldest first', () => {
    const saved = [order('b', '2026-10-09')];
    const candidates = [order('c', '2026-10-10'), order('b', '2026-10-09', { currentExpectedNet: 0 }), order('a', '2026-10-06')];

    const merged = mergeOrders(saved, candidates);

    expect(merged.map((each) => each.orderId)).toEqual(['a', 'b', 'c']);
    expect(merged[1].currentExpectedNet).toBe(561.48);
  });

  it('adds the accounts in cents, as the server, and leaves the difference out while the deposit cannot be read', () => {
    const orders = [order('a', '2026-10-06', { currentExpectedNet: 0.1 }), order('b', '2026-10-07', { currentExpectedNet: 0.2 })];
    const adjustments = [adjustment({ amountText: '-0.05' }), adjustment({ key: 'a2', amountText: '0' })];

    expect(totalsOf(orders, adjustments, '0.25')).toEqual({
      pickedCount: 2,
      ordersCents: 30,
      adjustmentsCents: -5,
      expectedCents: 25,
      depositedCents: 25,
      differenceCents: 0,
    });
    expect(totalsOf(orders, adjustments, '32,50')).toMatchObject({ depositedCents: undefined, differenceCents: undefined });
    // What an order brings now, not what was saved: a save counts it again.
    expect(totalsOf([order('a', '2026-10-06', { expectedNet: 561.48, currentExpectedNet: 280.74 })], [], '').ordersCents).toBe(28074);
  });

  it('takes the sums of a saved settlement from the server', () => {
    expect(savedTotals(detail().settlement)).toEqual({
      pickedCount: 2,
      ordersCents: 112296,
      adjustmentsCents: -5000,
      expectedCents: 107296,
      depositedCents: 107296,
      differenceCents: 0,
    });
  });

  it('says the first thing that keeps the settlement from being saved', () => {
    expect(firstProblem(form(), 2, TODAY)).toBe('');
    expect(firstProblem(form({ to: '2026-10-15' }), 2, TODAY)).toBe(PERIOD_IN_THE_FUTURE_MESSAGE);
    expect(firstProblem(form({ depositedText: '' }), 2, TODAY)).toBe(DEPOSITED_AMOUNT_MESSAGE);
    expect(firstProblem(form(), 2001, TODAY)).toBe(TOO_MANY_ORDERS_MESSAGE);
    expect(firstProblem(form({ adjustments: [adjustment({ kind: '' })] }), 2, TODAY)).toBe(ADJUSTMENT_KIND_MESSAGE);
  });

  it('builds the body the API takes, without blank texts', () => {
    const body = buildBody(
      form({
        reference: '  SPEI 4471 ',
        notes: '   ',
        adjustments: [adjustment({ description: '  Cupón 2x1 ' }), adjustment({ key: 'a2', kind: PlatformSettlementAdjustmentKind.Fee, amountText: '−12.50' })],
      }),
      ['a', 'b'],
    );

    expect(body).toEqual({
      periodFrom: '2026-10-05',
      periodTo: '2026-10-11',
      depositedOn: '2026-10-13',
      depositedAmount: 1122.96,
      reference: 'SPEI 4471',
      notes: null,
      orderIds: ['a', 'b'],
      adjustments: [
        { kind: PlatformSettlementAdjustmentKind.Promotion, description: 'Cupón 2x1', amount: -50 },
        { kind: PlatformSettlementAdjustmentKind.Fee, description: null, amount: -12.5 },
      ],
    });
  });

  it('knows whether the screen changed the saved settlement: not by the order of the orders nor by how an amount is written', () => {
    const saved = detail();
    const shown = formOf(saved);
    const edited = (patch: Partial<SettlementForm>, orderIds = ['a', 'b']) => {
      const changed = { ...shown, ...patch };
      return editedSince(saved, changed, buildBody(changed, orderIds));
    };

    expect(shown).toMatchObject({ depositedText: '1072.96', reference: 'SPEI 4471', notes: '' });
    expect(sameBody(buildBody(shown, ['b', 'a']), savedBody(saved))).toBe(true);
    expect(edited({}, ['b', 'a'])).toBe(false);
    expect(edited({ depositedText: '1,072.96' })).toBe(false);
    expect(edited({ depositedText: ' $1072.96 ' })).toBe(false);
    expect(edited({ depositedText: '1072.9' })).toBe(true);
    expect(edited({}, ['a'])).toBe(true);
    expect(edited({ notes: 'Llegó tarde' })).toBe(true);
  });

  it('takes a deposit box the API would refuse as a change, also over a saved $0', () => {
    const saved = detail({
      settlement: { ...detail().settlement, depositedAmount: 0, difference: -1072.96 },
    });
    const shown = formOf(saved);

    expect(shown.depositedText).toBe('0.00');
    expect(editedSince(saved, shown, buildBody(shown, ['a', 'b']))).toBe(false);
    for (const depositedText of ['', '  ', '1,072.960', 'abc']) {
      const typed = { ...shown, depositedText };
      expect(editedSince(saved, typed, buildBody(typed, ['a', 'b']))).toBe(true);
    }
  });

  it('finds the saved orders whose expected net changed since the save', () => {
    const changed = order('b', '2026-10-09', { currentExpectedNet: 280.74 });

    expect(changedSinceSaved(detail({ orders: [order('a', '2026-10-06'), changed] }))).toEqual([changed]);
    expect(changedSinceSaved(detail())).toEqual([]);
  });

  it('explains the difference with an «Otro» of the same amount', () => {
    expect(differenceAdjustment(-5000)).toMatchObject({
      kind: PlatformSettlementAdjustmentKind.Other,
      description: DIFFERENCE_DESCRIPTION,
      amountText: '-50.00',
    });
    expect(differenceAdjustment(1).amountText).toBe('0.01');
  });

  it('opens a new settlement on last week, deposited today', () => {
    expect(newSettlementForm(TODAY)).toMatchObject({ from: '2026-10-05', to: '2026-10-11', depositedOn: TODAY, depositedText: '' });
  });
});
