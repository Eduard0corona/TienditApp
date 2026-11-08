import { describe, expect, it } from 'vitest';
import { SubscriptionStatusValue, type SubscriptionDetail } from '../api/platformApi';
import { describeAttention, describeAttentionItems, describePlanNotice } from './attention';

describe('attention', () => {
  it('says what waits and where to handle it', () => {
    expect(describeAttention({ key: 'onlineOrdersToConfirm', count: 1, urgentCount: 0 })).toMatchObject({
      text: '1 pedido en línea por confirmar',
      path: '/orders?estado=por-confirmar',
      tone: 'warning',
    });
    expect(describeAttention({ key: 'drawersOpenSinceYesterday', count: 2, urgentCount: 0 })?.text).toBe('2 cajas abiertas desde otro día');
  });

  it('says how many online orders have waited 15 minutes, and turns the bell urgent (Fase 29X)', () => {
    expect(describeAttention({ key: 'onlineOrdersToConfirm', count: 3, urgentCount: 1 })).toMatchObject({
      text: '3 pedidos en línea por confirmar (1 lleva 15 minutos o más)',
      tone: 'danger',
    });
    expect(describeAttention({ key: 'onlineOrdersToConfirm', count: 3, urgentCount: 2 })?.text)
      .toBe('3 pedidos en línea por confirmar (2 llevan 15 minutos o más)');
  });

  it('leads the requests booked online to the agenda, filtered on them', () => {
    expect(describeAttention({ key: 'appointmentRequests', count: 2, urgentCount: 1 })).toMatchObject({
      text: '2 solicitudes de cita por confirmar (1 por responder hoy)',
      path: '/appointments?estado=por-confirmar',
      tone: 'warning',
    });
    expect(describeAttention({ key: 'appointmentRequests', count: 1, urgentCount: 0 })?.text).toBe('1 solicitud de cita por confirmar');
  });

  it('leads the CFDI that stayed «Timbrando» past the PAC\'s window to CFDI emitidos, as urgent (Fase 28K)', () => {
    expect(describeAttention({ key: 'cfdisToReview', count: 2, urgentCount: 2 })).toMatchObject({
      text: '2 CFDI por revisar en el portal del SAT',
      path: '/reports/issued-cfdis',
      tone: 'danger',
    });
    expect(describeAttention({ key: 'cfdisToReview', count: 1, urgentCount: 1 })?.text).toBe('1 CFDI por revisar en el portal del SAT');
  });

  it('leads a business that goes to homes and publishes an address to «Cómo te ven» (Fase 29A)', () => {
    expect(describeAttention({ key: 'homeVisitAddress', count: 1, urgentCount: 0 })).toMatchObject({
      text: 'Tu tienda muestra la dirección de un local: revisa si atiendes ahí',
      path: '/settings/business',
      tone: 'info',
    });
  });

  it('leads the accepted quotes waiting for their order to those still to convert, the ones from the link urgent (Fase 29G)', () => {
    expect(describeAttention({ key: 'quotesToConvert', count: 3, urgentCount: 1 })).toMatchObject({
      text: '3 cotizaciones aceptadas por convertir en orden (1 desde su enlace)',
      path: '/quotes?estado=por-convertir',
      tone: 'warning',
    });
    expect(describeAttention({ key: 'quotesToConvert', count: 1, urgentCount: 0 })).toMatchObject({
      text: '1 cotización aceptada por convertir en orden',
      tone: 'info',
    });
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
