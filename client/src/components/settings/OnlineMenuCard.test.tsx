import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OnlineMenuMode, TenantCapability } from '../../types';
import OnlineMenuCard from './OnlineMenuCard';

vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../api/tablesApi', () => ({ tablesApi: { getDiningRoom: vi.fn().mockResolvedValue({ tables: [] }) } }));

const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const refreshTenant = vi.fn();

function renderCard(mode: OnlineMenuMode, assistantOrders?: boolean) {
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Café Luna', slug: 'cafe-luna', onlineMenuMode: mode, assistantOrders },
    capabilities: TenantCapability.Retail,
    refreshTenant,
  } as unknown as ReturnType<typeof useTenant>);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}><OnlineMenuCard /></QueryClientProvider>);
}

/** Orders from AI assistants in the online menu's card (Fase 32B, ADR 0096). */
describe('OnlineMenuCard: pedidos desde asistentes de IA (Fase 32B)', () => {
  beforeEach(() => {
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(apiRequest).mockResolvedValue({} as never);
    refreshTenant.mockResolvedValue(undefined);
  });

  afterEach(() => vi.clearAllMocks());

  it('is on by default for a menu that takes orders, says what it means, and turns off with the mode as it is', async () => {
    renderCard(OnlineMenuMode.Orders);

    const toggle = screen.getByRole('switch', { name: 'Recibir pedidos desde asistentes de IA' });
    expect(toggle).toBeChecked();
    expect(toggle).toHaveAccessibleDescription(/tu cliente lo revisa y lo envía desde tu página/);

    await userEvent.click(toggle);

    await waitFor(() => expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/auth/my-tenant/online-menu', {
      body: { mode: OnlineMenuMode.Orders, assistantOrders: false },
    }));
    expect(refreshTenant).toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledWith('Ya no recibes pedidos desde asistentes de IA.');
  });

  it('turns it on again, and says when it could not', async () => {
    renderCard(OnlineMenuMode.Orders, false);
    const toggle = screen.getByRole('switch', { name: 'Recibir pedidos desde asistentes de IA' });
    expect(toggle).not.toBeChecked();

    await userEvent.click(toggle);
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Recibes pedidos desde asistentes de IA.'));
    expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/auth/my-tenant/online-menu', {
      body: { mode: OnlineMenuMode.Orders, assistantOrders: true },
    });

    vi.mocked(apiRequest).mockRejectedValueOnce(new Error('offline'));
    await userEvent.click(toggle);
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('No se pudo cambiar los pedidos desde asistentes de IA.'));
  });

  it('is not offered while the menu takes no orders', () => {
    renderCard(OnlineMenuMode.MenuOnly);
    expect(screen.queryByRole('switch', { name: 'Recibir pedidos desde asistentes de IA' })).not.toBeInTheDocument();
  });
});
