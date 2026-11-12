import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { chatApi } from '../../api/chatApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import type { ConversationDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import WhatsAppChat from './WhatsAppChat';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../components/customers/CustomerCardPanel', () => ({ default: () => null }));
vi.mock('../../api/chatApi', () => ({ chatApi: { getConversations: vi.fn(), getMessages: vi.fn(), markAsRead: vi.fn() } }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useChatSignalR', () => ({
  useChatSignalR: () => ({
    onNewMessage: () => () => undefined,
    onConversationUpdated: () => () => undefined,
    onMessageStatus: () => () => undefined,
    onMessageMedia: () => () => undefined,
  }),
}));

const conversation = (overrides: Partial<ConversationDto>): ConversationDto => ({
  id: 'c1', customerPhone: '5512345678', customerName: 'Ana Ruiz', lastMessageBody: 'Hola', lastMessageAt: '2026-10-14T16:45:00Z', unreadCount: 0,
  ...overrides,
});

// Intl may separate "a. m." with narrow spaces; compare without them.
const plain = (text: string | null) => (text ?? '').replace(/[\u00a0\u202f]/g, ' ');

beforeAll(() => {
  // jsdom has no matchMedia, and the customer sheet (Offcanvas) asks it for the screen size.
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList,
  });
});

describe('WhatsAppChat conversation list', () => {
  // The business is in Baja California (UTC-7 in October); the device is on Mexico City time (UTC-6).
  beforeEach(() => {
    vi.stubEnv('TZ', 'America/Mexico_City');
    setBusinessTimeZone('America/Tijuana');
    vi.useFakeTimers({ toFake: ['Date'] });
    // 18:00 UTC on the 14th: 11:00 in Tijuana, 12:00 on the device; the same day for both.
    vi.setSystemTime(new Date('2026-10-14T18:00:00.000Z'));
    vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'OWNER', permissions: [] } } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({ capabilities: 0 } as unknown as ReturnType<typeof useTenant>);
  });

  afterEach(() => {
    setBusinessTimeZone(null);
    vi.unstubAllEnvs();
    vi.useRealTimers();
  });

  it('shows the hour of the messages of today in the business and the date of older ones', async () => {
    vi.mocked(chatApi.getConversations).mockResolvedValue({
      data: {
        items: [
          // 9:45 in Tijuana today.
          conversation({ id: 'c1', customerName: 'Ana Ruiz', lastMessageAt: '2026-10-14T16:45:00Z' }),
          // 23:10 of the 13th in Tijuana: yesterday for the business, already today (00:10) on the device.
          conversation({ id: 'c2', customerName: 'Luis Paz', lastMessageAt: '2026-10-14T06:10:00Z' }),
        ],
        pageNumber: 1, totalPages: 1, totalCount: 2, hasPreviousPage: false, hasNextPage: false,
      },
    } as never);
    render(<MemoryRouter><WhatsAppChat /></MemoryRouter>);

    const today = (await screen.findByText('Ana Ruiz')).closest('[role="button"], button, li, div.d-flex')!;
    expect(plain(today.textContent)).toMatch(/09:45 a\. ?m\./);
    const yesterday = screen.getByText('Luis Paz').closest('[role="button"], button, li, div.d-flex')!;
    expect(plain(yesterday.textContent)).toMatch(/13-oct/);
    expect(plain(yesterday.textContent)).not.toMatch(/11:10/);
  });
});
