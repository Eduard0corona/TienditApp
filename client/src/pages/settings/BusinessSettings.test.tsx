import { render, screen, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { tenantApi } from '../../api/tenantApi';
import { TenantCapability, type TenantProfileDto } from '../../types';
import BusinessSettings from './BusinessSettings';

// Each card stands for itself: this test is about which ones the page shows and where.
vi.mock('../../components/settings/SubscriptionCard', () => ({ default: () => <section aria-label="Tu plan" /> }));
vi.mock('../../components/settings/SalesPolicyCard', () => ({ default: () => <section aria-label="Impuestos y ventas" /> }));
vi.mock('../../components/settings/OnlineMenuCard', () => ({ default: () => <section aria-label="Menú en línea" /> }));
vi.mock('../../components/settings/MenuCoverCard', () => ({ default: () => <section aria-label="Portada del menú" /> }));
vi.mock('../../components/settings/DirectoryCard', () => ({ default: () => <section aria-label="Cómo te ven en TienditApp" /> }));
vi.mock('../../components/settings/OpeningHoursCard', () => ({ default: () => <section aria-label="Horario" /> }));
vi.mock('../../components/settings/SpecialDaysCard', () => ({ default: () => <section aria-label="Días especiales" /> }));
vi.mock('../../components/settings/OnlineBookingCard', () => ({ default: () => <section aria-label="Citas en línea" /> }));
vi.mock('../../components/settings/OnlineBookingStaffCard', () => ({ default: () => <section aria-label="Quién atiende" /> }));
vi.mock('../../components/settings/HomeVisitRequestsCard', () => ({ default: () => <section aria-label="Visitas a domicilio" /> }));
vi.mock('../../components/settings/DeliveryPlatformsCard', () => ({ default: () => <section aria-label="Plataformas de reparto" /> }));
vi.mock('../../components/settings/ThemeCard', () => ({ default: () => <section aria-label="Apariencia" /> }));
vi.mock('../../components/layout/SectionTabs', () => ({ default: () => null }));
vi.mock('../../hooks/useMySubscription', () => ({ useMySubscription: () => ({ data: undefined }) }));
vi.mock('../../api/tenantApi', () => ({ tenantApi: { getMyTenant: vi.fn() } }));

// What the business already uses, as the requests of the cards answer it.
const answers: { platformActive?: boolean; bookingEnabled?: boolean; visitsEnabled?: boolean } = {};
vi.mock('../../api/queries', () => ({
  queryKeys: { specialDays: ['special-days'] },
  useSpecialDays: () => ({ data: undefined }),
  useDeliveryPlatforms: () => ({
    data: answers.platformActive === undefined ? undefined : { platforms: [{ isActive: answers.platformActive }] },
  }),
}));
vi.mock('../../api/onlineBookingSettings', () => ({
  useOnlineBookingSettings: () => ({ data: answers.bookingEnabled === undefined ? undefined : { enabled: answers.bookingEnabled } }),
}));
vi.mock('../../api/homeVisitRequests', () => ({
  useHomeVisitRequests: () => ({ data: answers.visitsEnabled === undefined ? undefined : { enabled: answers.visitsEnabled } }),
}));

const business = { industry: '', capabilities: 0 };
vi.mock('../../hooks/useTenant', () => ({
  useTenant: () => ({ tenant: { name: 'Mi negocio' }, capabilities: business.capabilities, refreshTenant: vi.fn() }),
}));

const WITH_AGENDA = TenantCapability.Service | TenantCapability.Retail | TenantCapability.Inventory | TenantCapability.Appointments;
const FOOD = TenantCapability.Food | TenantCapability.Inventory | TenantCapability.Kitchen;

function renderPage(industry: string, capabilities: number) {
  business.industry = industry;
  business.capabilities = capabilities;
  vi.mocked(tenantApi.getMyTenant).mockResolvedValue({
    data: { name: 'Mi negocio', contactEmail: 'hola@minegocio.mx', businessType: 0, industry, capabilities } as TenantProfileDto,
  } as Awaited<ReturnType<typeof tenantApi.getMyTenant>>);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  // A new element each time: the same one again would not render the page again.
  const page = () => (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/settings/business']}>
        <BusinessSettings />
      </MemoryRouter>
    </QueryClientProvider>
  );
  const view = render(page());
  return { ...view, rerenderPage: () => view.rerender(page()) };
}

const moreOptions = () => screen.queryByText('Más opciones')?.closest('details') ?? null;
const inMoreOptions = (card: string) => !!moreOptions() && within(moreOptions()!).queryByRole('region', { name: card }) !== null;
const groups = () => screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent);

describe('BusinessSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    answers.platformActive = false;
    answers.bookingEnabled = false;
    answers.visitsEnabled = false;
  });

  it('puts the data «Editar negocio» edits first, and groups the rest of the cards', async () => {
    renderPage('Otro', WITH_AGENDA);
    await screen.findByRole('heading', { name: 'Datos del negocio' });
    expect(groups()).toEqual(['Datos del negocio', 'Ventas', 'Tienda en línea', 'Horario', 'Agenda']);
  });

  it('keeps every option in its group for a giro that says nothing', async () => {
    renderPage('Otro', WITH_AGENDA);
    await screen.findByRole('heading', { name: 'Datos del negocio' });
    for (const card of ['Plataformas de reparto', 'Citas en línea', 'Quién atiende', 'Visitas a domicilio']) {
      expect(screen.getByRole('region', { name: card })).toBeInTheDocument();
    }
    expect(moreOptions()).toBeNull();
  });

  it('gives a kitchen that only delivers its delivery apps and leaves the agenda under «Más opciones»', async () => {
    renderPage('Comida a domicilio', FOOD | TenantCapability.Appointments);
    await screen.findByRole('heading', { name: 'Datos del negocio' });
    expect(screen.getByRole('region', { name: 'Plataformas de reparto' })).toBeInTheDocument();
    expect(inMoreOptions('Plataformas de reparto')).toBe(false);
    for (const card of ['Citas en línea', 'Quién atiende', 'Visitas a domicilio']) expect(inMoreOptions(card)).toBe(true);
    expect(groups()).not.toContain('Agenda');
    expect(moreOptions()).toHaveTextContent('Más opciones · Citas en línea y Visitas a domicilio');
    expect(moreOptions()).toHaveTextContent('Por tu giro (Comida a domicilio) no suelen hacer falta.');
  });

  it('shows no agenda option without the module, not even under «Más opciones»', async () => {
    renderPage('Comida a domicilio', FOOD);
    await screen.findByRole('heading', { name: 'Datos del negocio' });
    expect(screen.queryByRole('region', { name: 'Visitas a domicilio' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Citas en línea' })).not.toBeInTheDocument();
    expect(moreOptions()).toBeNull();
  });

  it('gives a dentist appointments online and leaves the visits and the delivery apps under «Más opciones»', async () => {
    renderPage('Consultorio dental', WITH_AGENDA);
    await screen.findByRole('heading', { name: 'Datos del negocio' });
    expect(inMoreOptions('Citas en línea')).toBe(false);
    expect(inMoreOptions('Quién atiende')).toBe(false);
    expect(inMoreOptions('Visitas a domicilio')).toBe(true);
    expect(inMoreOptions('Plataformas de reparto')).toBe(true);
    expect(groups()).toContain('Agenda');
  });

  it('gives a trade its visits to quote and leaves appointments online under «Más opciones»', async () => {
    renderPage('Plomería', WITH_AGENDA);
    await screen.findByRole('heading', { name: 'Datos del negocio' });
    expect(inMoreOptions('Visitas a domicilio')).toBe(false);
    expect(inMoreOptions('Citas en línea')).toBe(true);
    expect(inMoreOptions('Plataformas de reparto')).toBe(true);
  });

  it('keeps in its group what the business already uses, whatever its giro', async () => {
    answers.platformActive = true;
    answers.visitsEnabled = true;
    renderPage('Consultorio dental', WITH_AGENDA);
    await screen.findByRole('heading', { name: 'Datos del negocio' });
    expect(inMoreOptions('Plataformas de reparto')).toBe(false);
    expect(inMoreOptions('Visitas a domicilio')).toBe(false);
    expect(moreOptions()).toBeNull();
  });

  it('does not move a card away when the business turns it on under «Más opciones»', async () => {
    const { rerenderPage } = renderPage('Consultorio dental', WITH_AGENDA);
    await screen.findByRole('heading', { name: 'Datos del negocio' });
    expect(inMoreOptions('Visitas a domicilio')).toBe(true);

    answers.visitsEnabled = true;
    rerenderPage();
    expect(inMoreOptions('Visitas a domicilio')).toBe(true);
  });

  it('places what is already in use once its answer arrives', async () => {
    answers.platformActive = undefined;
    const { rerenderPage } = renderPage('Consultorio dental', WITH_AGENDA);
    await screen.findByRole('heading', { name: 'Datos del negocio' });
    expect(inMoreOptions('Plataformas de reparto')).toBe(true);

    answers.platformActive = true;
    rerenderPage();
    expect(inMoreOptions('Plataformas de reparto')).toBe(false);
  });
});
