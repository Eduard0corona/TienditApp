import { describe, expect, it } from 'vitest';
import { buildSellLink } from '../../utils/sellLink';
import { readChatOrigin, readCustomerPrefill } from './sellModel';

function paramsOf(link: string): URLSearchParams {
  return new URLSearchParams(link.split('?')[1] ?? '');
}

describe('readCustomerPrefill', () => {
  it('fills in the customer from the phone and the name', () => {
    expect(readCustomerPrefill(new URLSearchParams('phone=%205512345678%20&name=%20Ana%20'))).toEqual({ phone: '5512345678', name: 'Ana' });
    expect(readCustomerPrefill(new URLSearchParams('phone=5512345678&name=%20'))).toEqual({ phone: '5512345678', name: undefined });
  });

  it('fills in nothing without a phone', () => {
    expect(readCustomerPrefill(new URLSearchParams('name=Ana'))).toBeNull();
    expect(readCustomerPrefill(new URLSearchParams('phone=%20'))).toBeNull();
  });
});

describe('readChatOrigin', () => {
  it('is a chat only with the conversation it was started from', () => {
    expect(readChatOrigin(new URLSearchParams('phone=5512345678&name=Ana&conversation=c1')))
      .toEqual({ phone: '5512345678', name: 'Ana', conversationId: 'c1' });
  });

  it('is not a chat with only the customer (the customer card)', () => {
    expect(readChatOrigin(new URLSearchParams('phone=5512345678&name=Ana'))).toBeNull();
    expect(readChatOrigin(new URLSearchParams('phone=5512345678&conversation=%20'))).toBeNull();
  });

  it('is not a chat without the customer phone', () => {
    expect(readChatOrigin(new URLSearchParams('conversation=c1'))).toBeNull();
  });

  it('tells the customer card link from the chat link', () => {
    const fromCard = paramsOf(buildSellLink({ phone: '5512345678', name: 'Ana' }));
    expect(readCustomerPrefill(fromCard)).toEqual({ phone: '5512345678', name: 'Ana' });
    expect(readChatOrigin(fromCard)).toBeNull();

    const fromChat = paramsOf(buildSellLink({ phone: '5512345678', name: 'Ana', conversationId: 'c1' }));
    expect(readCustomerPrefill(fromChat)).toEqual({ phone: '5512345678', name: 'Ana' });
    expect(readChatOrigin(fromChat)).toEqual({ phone: '5512345678', name: 'Ana', conversationId: 'c1' });
  });
});
