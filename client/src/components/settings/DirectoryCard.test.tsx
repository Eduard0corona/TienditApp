import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { publicDirectoryApi, type DirectoryProfile } from '../../api/publicDirectoryApi';
import { BusinessCategory, DirectoryVisibility, QuoteTime, ResponseTime } from '../../api/generated/enums';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OnlineMenuMode, type TenantProfileDto } from '../../types';
import DirectoryCard from './DirectoryCard';

vi.mock('../../api/publicDirectoryApi', () => ({
  publicDirectoryApi: { getDirectory: vi.fn(), getProfile: vi.fn(), updateProfile: vi.fn(), dismissHomeVisitAddressNotice: vi.fn() },
}));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
// Whether it receives visits to quote (Fase 29A): none unless a test says so.
const visits = vi.hoisted(() => ({ data: undefined as { receivesVisits: boolean } | undefined }));
vi.mock('../../api/homeVisitRequests', () => ({ useHomeVisitRequests: () => ({ data: visits.data }) }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));

const api = vi.mocked(publicDirectoryApi);
const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };

function profile(overrides: Partial<DirectoryProfile> = {}): DirectoryProfile {
  return {
    category: BusinessCategory.Groceries,
    suggestedCategory: BusinessCategory.Groceries,
    description: null,
    listed: true,
    visibility: DirectoryVisibility.Shown,
    homeVisitAddressNotice: false,
    countsRequests: false,
    ...overrides,
  };
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <DirectoryCard />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DirectoryCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useTenant).mockReturnValue({
      tenant: { id: 't1', name: 'Abarrotes Doña Mary', slug: 'dona-mary', theme: 0, onlineMenuMode: OnlineMenuMode.Orders } as TenantProfileDto,
      capabilities: null,
      isLoading: false,
      refreshTenant: vi.fn(),
    });
  });

  it('says it appears and saves the category, the description and the choice to appear', async () => {
    api.getProfile.mockResolvedValue(profile());
    api.updateProfile.mockResolvedValue(profile({ category: BusinessCategory.Food, description: 'Tortillas y abarrotes', listed: false, visibility: DirectoryVisibility.HiddenByOwner }));
    const user = userEvent.setup();
    renderCard();

    expect(await screen.findByText(/Apareces en Explorar/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver cómo te encuentran' })).toHaveAttribute('href', '/explorar/abarrotes');
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();

    await user.selectOptions(screen.getByLabelText('Categoría'), 'Comida');
    expect(screen.getByText('Por tu giro te sugerimos Abarrotes.')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Descripción corta'), '  Tortillas y abarrotes ');
    await user.click(screen.getByRole('switch', { name: 'Aparecer en Explorar' }));
    // The preview shows the card as customers will see it.
    expect(screen.getByText('Tortillas y abarrotes')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(api.updateProfile).toHaveBeenCalledWith({ category: BusinessCategory.Food, description: 'Tortillas y abarrotes', listed: false });
    expect(await screen.findByText('No apareces en Explorar: elegiste no aparecer.')).toBeInTheDocument();
    expect(toast.success).toHaveBeenCalledWith('Guardado.');
  });

  it.each([
    [DirectoryVisibility.MenuOff, /publica tu menú en línea/],
    [DirectoryVisibility.NoProducts, /tu menú todavía no muestra productos/],
  ])('says what is missing to appear (%s)', async (visibility, words) => {
    api.getProfile.mockResolvedValue(profile({ visibility }));
    renderCard();

    expect(await screen.findByText(words)).toBeInTheDocument();
  });

  it('shows its owner how soon it answers on its card, and why it says nothing (Fase 29K)', async () => {
    api.getProfile.mockResolvedValue(profile({ countsRequests: true, responseTime: ResponseTime.WithinAnHour }));
    const { unmount } = renderCard();

    expect(await screen.findByText('Suele responder en menos de una hora')).toBeInTheDocument();
    expect(screen.getByText(/Tus clientes lo leen en tu tarjeta, en tu tienda/)).toBeInTheDocument();
    unmount();

    // A day or more: its card says nothing, its owner reads why.
    api.getProfile.mockResolvedValue(profile({ countsRequests: true, responseTime: ResponseTime.Slower }));
    const slow = renderCard();
    expect(await screen.findByText(/Sueles tardar un día o más/)).toBeInTheDocument();
    expect(screen.queryByText(/Suele responder/)).not.toBeInTheDocument();
    slow.unmount();

    api.getProfile.mockResolvedValue(profile({ countsRequests: true }));
    const newer = renderCard();
    expect(await screen.findByText(/Todavía no contamos solicitudes en línea de 3 clientes/)).toBeInTheDocument();
    newer.unmount();

    // Explorar does not count its requests: nothing of them, whatever came with it.
    api.getProfile.mockResolvedValue(profile({ countsRequests: false, responseTime: ResponseTime.WithinAnHour }));
    renderCard();
    expect(await screen.findByText(/Apareces en Explorar/)).toBeInTheDocument();
    expect(screen.queryByText(/solicitudes en línea|Suele responder/)).not.toBeInTheDocument();
  });

  it('shows a trade how soon it quotes after the visit, and why it says nothing (Fase 29S)', async () => {
    visits.data = { receivesVisits: true };
    api.getProfile.mockResolvedValue(profile({ countsRequests: true, quoteTime: QuoteTime.WithinThreeDays }));
    const { unmount } = renderCard();

    expect(await screen.findByText('Suele mandar la cotización en menos de 3 días')).toBeInTheDocument();
    expect(screen.getByText(/hasta que mandas su cotización/)).toBeInTheDocument();
    unmount();

    api.getProfile.mockResolvedValue(profile({ countsRequests: true, quoteTime: QuoteTime.Slower }));
    const slow = renderCard();
    expect(await screen.findByText(/Sueles tardar una semana o más/)).toBeInTheDocument();
    expect(screen.queryByText(/Suele mandar la cotización/)).not.toBeInTheDocument();
    slow.unmount();

    // A business without visits says nothing of quotes.
    visits.data = undefined;
    api.getProfile.mockResolvedValue(profile({ countsRequests: true, quoteTime: QuoteTime.WithinADay }));
    renderCard();
    expect(await screen.findByText(/Todavía no contamos solicitudes en línea/)).toBeInTheDocument();
    expect(screen.queryByText(/cotización|cotizar/)).not.toBeInTheDocument();
  });

  it('tells a business that goes to homes that its store shows its address, until its owner checks it (Fase 29A)', async () => {
    api.getProfile.mockResolvedValueOnce(profile({ homeVisitAddressNotice: true })).mockResolvedValue(profile());
    api.dismissHomeVisitAddressNotice.mockResolvedValue(undefined as never);
    const user = userEvent.setup();
    renderCard();

    expect(await screen.findByText(/Tu tienda muestra la dirección de tu sucursal principal/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sucursales' })).toHaveAttribute('href', '/branches');
    await user.click(screen.getByRole('button', { name: 'Ya lo revisé' }));

    expect(api.dismissHomeVisitAddressNotice).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.queryByText(/Tu tienda muestra la dirección/)).not.toBeInTheDocument());
    // Its button went away: the keyboard goes to the card's heading, not to the top of the page.
    expect(screen.getByRole('heading', { name: 'Cómo te ven en TienditApp' })).toHaveFocus();
    expect(api.updateProfile).not.toHaveBeenCalled();
  });

  it('puts the notice away once it is saved, even if reading the card again fails', async () => {
    api.getProfile.mockResolvedValueOnce(profile({ homeVisitAddressNotice: true })).mockRejectedValue(new Error('network'));
    api.dismissHomeVisitAddressNotice.mockResolvedValue(undefined as never);
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole('button', { name: 'Ya lo revisé' }));

    // Saved: never stuck on «Guardando…» waiting for a read that did not come back.
    await waitFor(() => expect(screen.queryByText(/Tu tienda muestra la dirección/)).not.toBeInTheDocument());
    expect(screen.getByRole('heading', { name: 'Cómo te ven en TienditApp' })).toHaveFocus();
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('keeps the notice and says why when checking it fails', async () => {
    api.getProfile.mockResolvedValue(profile({ homeVisitAddressNotice: true }));
    api.dismissHomeVisitAddressNotice.mockRejectedValue(new Error('network'));
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole('button', { name: 'Ya lo revisé' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(screen.getByRole('button', { name: 'Ya lo revisé' })).toBeEnabled();
  });

  it('keeps the description on one line', async () => {
    api.getProfile.mockResolvedValue(profile());
    const user = userEvent.setup();
    renderCard();

    const description = await screen.findByLabelText('Descripción corta');
    await user.click(description);
    await user.paste('Tacos\ny gringas');

    expect((description as HTMLInputElement).value).not.toMatch(/[\r\n]/);
  });
});
