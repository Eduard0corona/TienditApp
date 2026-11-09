import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SubscriptionStatusValue, type Plan, type PlatformTenant, type SubscriptionDetail } from '../../api/platformApi';
import { apiRequest } from '../../api/typedClient';
import TenantBillingModal from './TenantBillingModal';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: vi.fn() }) }));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const PLAN = { id: 'p1', name: 'Básico', monthlyPrice: 299, maxBranches: 1, maxUsers: 3, trialDays: 14, isActive: true } as unknown as Plan;
const PRO = { id: 'p2', name: 'Pro', monthlyPrice: 599, maxBranches: 3, maxUsers: 10, trialDays: 14, isActive: true } as unknown as Plan;
const TENANT = { tenantId: 't1', name: 'Café Luna', planId: 'p1', tenantStatus: 0 } as unknown as PlatformTenant;
const DETAIL = {
  hasPlan: true, status: SubscriptionStatusValue.Active, plan: PLAN, branchesUsed: 1, usersUsed: 2, trialEndsAt: null,
  paidThrough: '2026-12-01', suspendsAt: null, statusReason: null, suspendedByPlatform: false, payments: [],
} as unknown as SubscriptionDetail;

describe('TenantBillingModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('stays open while a payment is being registered, and waits for the business and the list read again before another', async () => {
    const user = userEvent.setup();
    let registered: (value: unknown) => void = () => undefined;
    let reread: (value: unknown) => void = () => undefined;
    let listed: (value: unknown) => void = () => undefined;
    let reads = 0;
    let lists = 0;
    vi.mocked(apiRequest).mockImplementation(((method: string, path: string) => {
      if (method === 'get') return ++reads === 1 ? Promise.resolve(DETAIL) : new Promise((resolve) => { reread = resolve; });
      if (path === '/api/v1/platform/tenants/{tenantId}/payments') return new Promise((resolve) => { registered = resolve; });
      return Promise.reject(new Error(`${method} ${path}`));
    }) as unknown as typeof apiRequest);
    // The console's list of businesses, which gives the dialog the business's plan and status.
    function TenantList() {
      useQuery({
        queryKey: ['platform', 'tenants', '', null, null, 1],
        queryFn: () => (++lists === 1 ? Promise.resolve([]) : new Promise((resolve) => { listed = resolve; })),
      });
      return null;
    }
    const onClose = vi.fn();
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <TenantList />
        <TenantBillingModal tenant={TENANT} plans={[PLAN, PRO]} onClose={onClose} />
      </QueryClientProvider>,
    );

    const dialog = screen.getByRole('dialog', { name: 'Café Luna' });
    const actions = ['Cambiar plan', 'Registrar $299.00', 'Extender', 'Suspender', 'Cancelar plan'];
    const expectActions = (enabled: boolean) => {
      for (const name of actions) {
        const button = within(dialog).getByRole('button', { name });
        if (enabled) expect(button).toBeEnabled();
        else expect(button).toBeDisabled();
      }
    };
    // Another plan chosen, so «Cambiar plan» is offered too.
    await user.selectOptions(await within(dialog).findByRole('combobox', { name: 'Plan' }), 'p2');
    await user.click(within(dialog).getByRole('button', { name: 'Registrar $299.00' }));

    expectActions(false);
    expect(within(dialog).getByRole('button', { name: 'Cerrar' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(onClose).not.toHaveBeenCalled();

    // Registered, the dialog can close; another payment waits for the business and the list read again, so it is not
    // registered twice from what they said before.
    registered({});
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Pago registrado.'));
    expect(within(dialog).getByRole('button', { name: 'Cerrar' })).toBeEnabled();
    expect(within(dialog).getByRole('button', { name: 'Close' })).toBeInTheDocument();
    expectActions(false);
    reread(DETAIL);
    await waitFor(() => expect(reads).toBe(2));
    await act(() => new Promise((resolve) => { setTimeout(resolve, 20); }));
    expect(lists).toBe(2);
    expectActions(false);
    listed([]);
    await waitFor(() => expectActions(true));

    // Another payment, with the list back first: it still waits for the business.
    await user.click(within(dialog).getByRole('button', { name: 'Registrar $299.00' }));
    registered({});
    await waitFor(() => expect(toast.success).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(lists).toBe(3));
    listed([]);
    await act(() => new Promise((resolve) => { setTimeout(resolve, 20); }));
    expect(reads).toBe(3);
    expectActions(false);
    reread(DETAIL);
    await waitFor(() => expectActions(true));
  });
});
