import { describe, expect, it } from 'vitest';
import { SubscriptionStatusValue, type SubscriptionDetail } from '../api/platformApi';
import { describeAttention, describeAttentionItems, describePlanNotice } from './attention';

describe('attention', () => {
  it('says what waits and where to handle it', () => {
    expect(describeAttention({ key: 'onlineOrdersToConfirm', count: 1, urgentCount: 0 })).toMatchObject({
      text: '1 pedido en línea por confirmar',
      path: '/orders',
      tone: 'warning',
    });
    expect(describeAttention({ key: 'drawersOpenSinceYesterday', count: 2, urgentCount: 0 })?.text).toBe('2 cajas abiertas desde otro día');
  });

  it('leads the requests booked online to the agenda, filtered on them', () => {
    expect(describeAttention({ key: 'appointmentRequests', count: 2, urgentCount: 1 })).toMatchObject({
      text: '2 solicitudes de cita por confirmar (1 de hoy)',
      path: '/appointments?estado=por-confirmar',
      tone: 'warning',
    });
    expect(describeAttention({ key: 'appointmentRequests', count: 1, urgentCount: 0 })?.text).toBe('1 solicitud de cita por confirmar');
  });

  it('marks stock as urgent when something ran out', () => {
    expect(describeAttention({ key: 'lowStock', count: 3, urgentCount: 1 })).toMatchObject({
      text: '3 insumos por reabastecer (1 agotado o crítico)',
      tone: 'danger',
    });
    expect(describeAttention({ key: 'lowStock', count: 3, urgentCount: 0 })?.tone).toBe('warning');
  });

  it('leaves out keys it does not know and puts the urgent first', () => {
    const notices = describeAttentionItems([
      { key: 'unreadChats', count: 4, urgentCount: 0 },
      { key: 'somethingNew', count: 1, urgentCount: 0 },
      { key: 'failedMessages', count: 2, urgentCount: 2 },
    ]);

    expect(notices.map((notice) => notice.key)).toEqual(['failedMessages', 'unreadChats']);
  });
});

describe('plan notice', () => {
  const plan = (status: SubscriptionDetail['status'], hasPlan = true) =>
    ({ hasPlan, status, suspendsAt: '2026-10-09T12:00:00Z' }) as SubscriptionDetail;

  it('tells the owner the plan is past due and leads to it', () => {
    expect(describePlanNotice(plan(SubscriptionStatusValue.PastDue))).toMatchObject({
      key: 'planPastDue',
      path: '/settings/business',
      tone: 'warning',
    });
    expect(describePlanNotice(plan(SubscriptionStatusValue.PastDue))?.text).toMatch(/^Pago del plan vencido: el servicio se suspende el /);
  });

  it('says nothing for a plan in order, a business without one, or someone who does not read it', () => {
    expect(describePlanNotice(plan(SubscriptionStatusValue.Active))).toBeNull();
    expect(describePlanNotice(plan(SubscriptionStatusValue.Trial))).toBeNull();
    expect(describePlanNotice(plan(SubscriptionStatusValue.PastDue, false))).toBeNull();
    expect(describePlanNotice(undefined)).toBeNull();
  });
});
