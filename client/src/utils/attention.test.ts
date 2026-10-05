import { describe, expect, it } from 'vitest';
import { describeAttention, describeAttentionItems } from './attention';

describe('attention', () => {
  it('says what waits and where to handle it', () => {
    expect(describeAttention({ key: 'onlineOrdersToConfirm', count: 1, urgentCount: 0 })).toMatchObject({
      text: '1 pedido en línea por confirmar',
      path: '/orders',
      tone: 'warning',
    });
    expect(describeAttention({ key: 'drawersOpenSinceYesterday', count: 2, urgentCount: 0 })?.text).toBe('2 cajas abiertas desde otro día');
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
