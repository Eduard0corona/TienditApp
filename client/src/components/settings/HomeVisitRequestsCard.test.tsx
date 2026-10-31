import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { appointmentsApi, type HomeVisitRequestsSettings } from '../../api/appointmentsApi';
import { HomeVisitChargePolicy } from '../../api/generated/enums';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OnlineMenuMode, TenantCapability, type TenantProfileDto } from '../../types';
import HomeVisitRequestsCard from './HomeVisitRequestsCard';

vi.mock('../../api/appointmentsApi', () => ({
  appointmentsApi: { getHomeVisitRequests: vi.fn(), updateHomeVisitRequests: vi.fn() },
}));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));

const api = vi.mocked(appointmentsApi);
const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const NO_ZONES = 'Da de alta tus zonas en Configuración › Zonas de entrega: a qué códigos postales vas y cuánto cuesta ir.';
const visit = { id: 's1', name: 'Visita y diagnóstico', price: 200, durationMinutes: 60 };
const drain = { id: 's2', name: 'Destapar drenaje', price: 600, durationMinutes: 90 };

function settings(overrides: Partial<HomeVisitRequestsSettings> = {}): HomeVisitRequestsSettings {
  return {
    enabled: false,
    serviceId: null,
    service: undefined,
    chargePolicy: HomeVisitChargePolicy.Charged,
    price: undefined,
    pricesIncludeTax: true,
    receivesVisits: false,
    missing: [],
    services: [drain, visit],
    addressNotice: false,
    slug: 'plomeria-hernandez',
    ...overrides,
  };
}

function withCapabilities(capabilities: number) {
  vi.mocked(useTenant).mockReturnValue({
    tenant: {
      id: 't1', name: 'Plomería Hernández', slug: 'plomeria-hernandez', onlineMenuMode: OnlineMenuMode.MenuOnly, openingHours: [],
    } as unknown as TenantProfileDto,
    capabilities,
    isLoading: false,
    refreshTenant: vi.fn(),
  });
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
  return {
    invalidate,
    ...render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <HomeVisitRequestsCard />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  };
}

describe('HomeVisitRequestsCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    withCapabilities(TenantCapability.Service | TenantCapability.Appointments);
  });

  it('turns visits on with the visit service and how it is charged, and says what is still missing', async () => {
    const user = userEvent.setup();
    api.getHomeVisitRequests.mockResolvedValue(settings({ missing: [NO_ZONES] }) as never);
    // The server answers with what it saved: the form shows that, not what was picked (here, another policy).
    api.updateHomeVisitRequests.mockResolvedValue(settings({
      enabled: true, serviceId: 's1', service: visit, chargePolicy: HomeVisitChargePolicy.Free, missing: [NO_ZONES],
    }) as never);
    const { invalidate } = renderCard();

    expect(await screen.findByText(NO_ZONES)).toBeInTheDocument();
    expect(screen.getByText(/Para recibir visitas a domicilio, falta también/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
    await user.click(screen.getByRole('switch', { name: 'Recibir visitas a domicilio para cotizar' }));
    await user.selectOptions(screen.getByLabelText('Servicio de la visita'), 'Visita y diagnóstico · $200.00');
    await user.click(screen.getByRole('radio', { name: 'Se descuenta si contrata' }));
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(api.updateHomeVisitRequests).toHaveBeenCalledWith({
      enabled: true, serviceId: 's1', chargePolicy: HomeVisitChargePolicy.CreditedIfHired,
    }));
    expect(toast.success).toHaveBeenCalledWith('Guardado.');
    expect(await screen.findByText(/Tus clientes todavía no ven tus visitas a domicilio/)).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Sin costo' })).toBeChecked();
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
    // «Cómo te ven» and the notices of Inicio are asked again: receiving visits changes both.
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['directory-profile'] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['attention'] });
  });

  it('says what customers would read before they see the visits, and what they read today while something changes', async () => {
    const user = userEvent.setup();
    api.getHomeVisitRequests.mockResolvedValue(settings({ price: { amount: 300, isStarting: false }, missing: [NO_ZONES] }) as never);
    renderCard();

    expect(await screen.findByText(/Cuando recibas visitas, tus clientes leerán/)).toHaveTextContent('Visita $300.00');
    await user.click(screen.getByRole('radio', { name: 'Sin costo' }));
    expect(screen.getByText(/Guarda para ver cómo queda/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Descartar' }));
    expect(screen.getByRole('radio', { name: 'Se cobra siempre' })).toBeChecked();
    expect(screen.queryByRole('button', { name: 'Descartar' })).not.toBeInTheDocument();
    expect(screen.queryByText(/Guarda para ver cómo queda/)).not.toBeInTheDocument();
    expect(api.updateHomeVisitRequests).not.toHaveBeenCalled();
  });

  it('says what customers read of a visit while they see them, and links the store', async () => {
    api.getHomeVisitRequests.mockResolvedValue(settings({
      enabled: true,
      receivesVisits: true,
      serviceId: 's1',
      service: visit,
      price: { amount: 250, isStarting: true },
      pricesIncludeTax: false,
    }) as never);
    renderCard();

    expect(await screen.findByText(/Tus clientes ya ven tus visitas a domicilio en tu tienda y en Explorar/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver tu tienda' })).toHaveAttribute('href', '/m/plomeria-hernandez');
    expect(screen.getByText('Visita desde $250.00 más IVA')).toBeInTheDocument();
    // Written like its prices: the services without IVA say so.
    expect(screen.getByRole('option', { name: 'Destapar drenaje · $600.00 más IVA' })).toBeInTheDocument();
  });

  it('keeps a visit service that cannot be booked anymore until the owner picks another, and says it adds nothing', async () => {
    const user = userEvent.setup();
    api.getHomeVisitRequests.mockResolvedValue(settings({ enabled: true, serviceId: 'old', service: undefined }) as never);
    api.updateHomeVisitRequests.mockResolvedValue(settings({ enabled: false, serviceId: 'old' }) as never);
    renderCard();

    const service = await screen.findByLabelText('Servicio de la visita');
    expect(service).toHaveValue('old');
    expect(screen.getByText('No suma nada a la visita hasta que lo vuelvas a activar o elijas otro.')).toBeInTheDocument();
    await user.click(screen.getByRole('switch', { name: 'Recibir visitas a domicilio para cotizar' }));
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(api.updateHomeVisitRequests).toHaveBeenCalledWith({
      enabled: false, serviceId: 'old', chargePolicy: HomeVisitChargePolicy.Charged,
    }));
  });

  it('shows the picked service even past the first services the list brings', async () => {
    const far = { id: 's9', name: 'Visita nocturna', price: 500, durationMinutes: 60 };
    api.getHomeVisitRequests.mockResolvedValue(settings({ enabled: true, serviceId: 's9', service: far }) as never);
    renderCard();

    expect(await screen.findByLabelText('Servicio de la visita')).toHaveValue('s9');
    expect(screen.queryByText(/No suma nada a la visita/)).not.toBeInTheDocument();
  });

  it('says why saving failed', async () => {
    const user = userEvent.setup();
    api.getHomeVisitRequests.mockResolvedValue(settings() as never);
    api.updateHomeVisitRequests.mockRejectedValue(new Error('network'));
    renderCard();

    await user.click(await screen.findByRole('radio', { name: 'Sin costo' }));
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(screen.getByRole('radio', { name: 'Sin costo' })).toBeChecked();
  });

  it('does not show without the agenda module', () => {
    withCapabilities(TenantCapability.Service);
    const { container } = renderCard();

    expect(container).toBeEmptyDOMElement();
    expect(api.getHomeVisitRequests).not.toHaveBeenCalled();
  });
});
