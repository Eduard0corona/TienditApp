import { act, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { TenantCapability } from '../../types';
import Topbar from './Topbar';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: vi.fn() }) }));
vi.mock('../../hooks/usePendingSales', () => ({ usePendingSales: () => ({ sales: [] }) }));
vi.mock('../../api/authApi', () => ({ authApi: { getMyTenants: vi.fn(async () => ({ data: [] })) } }));
vi.mock('./AttentionBell', () => ({ default: () => null }));
vi.mock('./BusinessSwitcher', () => ({ default: () => null }));
vi.mock('./CashStatusChip', () => ({ default: () => null }));
vi.mock('./ThemeToggle', () => ({ default: () => null }));
vi.mock('../business/CreateBusinessModal', () => ({ default: () => null }));
vi.mock('./AppointmentRequestsLive', () => ({
  default: ({ tenantId }: { tenantId: string }) => <span>Escucha las solicitudes de {tenantId}</span>,
}));
vi.mock('./QuoteRepliesLive', () => ({
  default: ({ tenantId }: { tenantId: string }) => <span>Escucha las cotizaciones de {tenantId}</span>,
}));

const APPOINTMENTS = TenantCapability.Service | TenantCapability.Appointments;

function renderAs(role: string, permissions: string[], tenant: { id: string; status: number }, capabilities = APPOINTMENTS) {
  vi.mocked(useAuth).mockReturnValue({
    user: { id: 'u1', firstName: 'Mariana', lastName: 'Ortiz', role, permissions, tenantId: tenant.id },
    logout: vi.fn(),
    switchTenant: vi.fn(),
  } as unknown as ReturnType<typeof useAuth>);
  vi.mocked(useTenant).mockReturnValue({
    tenant: { name: 'Estética Luna', ...tenant },
    capabilities,
    refreshTenant: vi.fn(),
  } as unknown as ReturnType<typeof useTenant>);
  return render(
    <MemoryRouter>
      <Topbar onToggleSidebar={vi.fn()} isMobileMenuOpen={false} onOpenSearch={vi.fn()} />
    </MemoryRouter>,
  );
}

describe('Topbar: the live requests online (Fase 29N)', () => {
  beforeEach(() => vi.clearAllMocks());

  it('listens for whoever answers the requests of an active business with appointments', async () => {
    renderAs('STAFF', ['ManageAppointments'], { id: 't1', status: 0 });

    expect(await screen.findByText('Escucha las solicitudes de t1')).toBeInTheDocument();
  });

  it('does not listen in a suspended business, without the agenda, without appointments, nor for the platform admin', async () => {
    const cases = [
      () => renderAs('TENANT_ADMIN', [], { id: 'suspendido', status: 1 }),
      () => renderAs('STAFF', ['ManageOrders'], { id: 'sin-agenda', status: 0 }),
      () => renderAs('TENANT_ADMIN', [], { id: 'sin-citas', status: 0 }, TenantCapability.Retail),
      () => renderAs('SUPER_ADMIN', [], { id: 'plataforma', status: 0 }),
    ];
    for (const render of cases) {
      const { unmount } = render();
      // Long enough for the lazy component to arrive, had it been asked for.
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 30));
      });
      expect(screen.queryByText(/Escucha las solicitudes/)).not.toBeInTheDocument();
      unmount();
    }
  });
});

describe('Topbar: the live answers to quotes (Fase 29R)', () => {
  beforeEach(() => vi.clearAllMocks());

  it('listens for whoever works the quotes of an active business with services or appointments', async () => {
    renderAs('STAFF', ['ManageOrders'], { id: 't1', status: 0 }, TenantCapability.Service);

    expect(await screen.findByText('Escucha las cotizaciones de t1')).toBeInTheDocument();
  });

  it('does not listen in a suspended business, without the orders, without quotes, nor for the platform admin', async () => {
    const cases = [
      () => renderAs('TENANT_ADMIN', [], { id: 'suspendido', status: 1 }),
      () => renderAs('STAFF', ['ManageAppointments'], { id: 'sin-pedidos', status: 0 }),
      () => renderAs('TENANT_ADMIN', [], { id: 'sin-cotizaciones', status: 0 }, TenantCapability.Food),
      () => renderAs('SUPER_ADMIN', [], { id: 'plataforma', status: 0 }),
    ];
    for (const render of cases) {
      const { unmount } = render();
      // Long enough for the lazy component to arrive, had it been asked for.
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 30));
      });
      expect(screen.queryByText(/Escucha las cotizaciones/)).not.toBeInTheDocument();
      unmount();
    }
  });
});
