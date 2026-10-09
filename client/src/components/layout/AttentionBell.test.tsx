import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SubscriptionStatusValue } from '../../api/platformApi';
import { useAttention } from '../../api/queries';
import { useMySubscription } from '../../hooks/useMySubscription';
import AttentionBell from './AttentionBell';

vi.mock('../../api/queries', () => ({ useAttention: vi.fn() }));
vi.mock('../../hooks/useMySubscription', () => ({ useMySubscription: vi.fn() }));

function withAttention(items: Array<{ key: string; count: number; urgentCount: number }>) {
  vi.mocked(useAttention).mockReturnValue({ data: { items }, isLoading: false, isError: false, isSuccess: true } as unknown as ReturnType<typeof useAttention>);
}

function withSubscription(data: unknown) {
  vi.mocked(useMySubscription).mockReturnValue({ data } as unknown as ReturnType<typeof useMySubscription>);
}

const renderBell = () => render(<MemoryRouter><AttentionBell /></MemoryRouter>);

describe('AttentionBell', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('puts the plan past due first and counts it, for the owners and admins who read it', () => {
    withAttention([{ key: 'unreadChats', count: 3, urgentCount: 0 }]);
    withSubscription({ hasPlan: true, status: SubscriptionStatusValue.PastDue, suspendsAt: '2026-10-09T12:00:00Z' });
    renderBell();

    expect(screen.getByRole('button', { name: 'Avisos: 4 pendientes' })).toBeInTheDocument();
    const links = screen.getAllByRole('link');
    expect(links[0]).toHaveTextContent(/^Pago del plan vencido/);
    expect(links[0]).toHaveAttribute('href', '/settings/business');
    expect(links[1]).toHaveTextContent('3 conversaciones sin leer');
  });

  it('shows only what the API reports when the plan is in order or the person does not read it', () => {
    withAttention([{ key: 'unreadChats', count: 1, urgentCount: 0 }]);
    withSubscription(undefined);
    renderBell();

    expect(screen.getByRole('button', { name: 'Avisos: 1 pendiente' })).toBeInTheDocument();
    expect(screen.queryByText(/Pago del plan vencido/)).not.toBeInTheDocument();
  });
});
