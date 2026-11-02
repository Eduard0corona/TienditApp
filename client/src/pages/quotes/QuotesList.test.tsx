import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { quotesApi } from '../../api/quotesApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { QuoteListFilter, QuoteStatus, TenantCapability } from '../../types';
import QuotesList from './QuotesList';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../api/quotesApi', () => ({
  quotesApi: { list: vi.fn(), settings: vi.fn(), updateSettings: vi.fn(), setWhatsAppTemplate: vi.fn(), create: vi.fn() },
}));

const toast = { success: vi.fn(), error: vi.fn() };

function page(items: unknown[]) {
  return { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false };
}

function renderList(role = 'TENANT_ADMIN') {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role, permissions: ['ManageOrders'] } } as unknown as ReturnType<typeof useAuth>);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/quotes']}>
        <Routes>
          <Route path="/quotes" element={<QuotesList />} />
          <Route path="/quotes/:id" element={<p>Detalle de la cotización</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('QuotesList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useTenant).mockReturnValue({ capabilities: TenantCapability.Service | TenantCapability.Appointments } as unknown as ReturnType<typeof useTenant>);
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(quotesApi.settings).mockResolvedValue({ validityDays: 15, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: false, whatsAppTemplates: [] });
    vi.mocked(quotesApi.list).mockResolvedValue(page([
      { id: 'q1', number: 'COT-000007', revision: 2, status: QuoteStatus.Expired, customerName: 'Valeria Ruiz', customerPhone: '5522107765', total: 1160, validUntil: '2026-10-19', updatedAt: '2026-10-04T17:00:00Z', fromVisit: true },
    ]) as never);
  });

  it('lists the open quotes with where each stands, and filters them', async () => {
    renderList();

    expect(await screen.findByRole('link', { name: 'COT-000007 · versión 2' })).toHaveAttribute('href', '/quotes/q1');
    expect(screen.getByText('Vencida')).toBeInTheDocument();
    expect(screen.getByText('De una visita')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Abrir la cotización COT-000007 · versión 2 de Valeria Ruiz' })).toBeInTheDocument();
    expect(quotesApi.list).toHaveBeenCalledWith(QuoteListFilter.Open, '', 1, expect.anything());

    await userEvent.selectOptions(screen.getByLabelText('Estado'), 'Vencidas');

    await waitFor(() => expect(quotesApi.list).toHaveBeenLastCalledWith(QuoteListFilter.Expired, '', 1, expect.anything()));
  });

  it('starts a quote for a customer and opens it', async () => {
    vi.mocked(quotesApi.create).mockResolvedValue({ id: 'q9' } as never);
    renderList();

    await userEvent.click(await screen.findByRole('button', { name: 'Nueva cotización' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nueva cotización' });
    await userEvent.type(within(dialog).getByLabelText('Cliente *'), 'Ana López');
    await userEvent.type(within(dialog).getByLabelText('Teléfono *'), '5511112222');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Empezar cotización' }));

    await waitFor(() => expect(quotesApi.create).toHaveBeenCalledWith({ customerName: 'Ana López', customerPhone: '5511112222', customerEmail: null }));
    expect(await screen.findByText('Detalle de la cotización')).toBeInTheDocument();
  });

  it('lets owners change how long quotes are valid, and only tells the rest', async () => {
    vi.mocked(quotesApi.updateSettings).mockResolvedValue({ validityDays: 30, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: false, whatsAppTemplates: [] });
    renderList();

    const days = await screen.findByLabelText('Días de vigencia');
    await userEvent.clear(days);
    await userEvent.type(days, '30');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar vigencia' }));

    await waitFor(() => expect(quotesApi.updateSettings).toHaveBeenCalledWith(30));
    expect(toast.success).toHaveBeenCalledWith('Tus cotizaciones valen 30 días desde que las envías.');
  });

  it('only tells staff how long quotes are valid', async () => {
    renderList('STAFF');

    expect(await screen.findByText(/Una cotización vale 15 días desde que la envías/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Días de vigencia')).not.toBeInTheDocument();
  });

  it('lets owners pick the WhatsApp template of the quotes once the business connected its WhatsApp', async () => {
    const templates = [{ id: 't1', name: 'Cotización lista', body: 'Tu cotización {folio}: {enlace}', providerTemplateName: 'cotizacion_lista' }];
    vi.mocked(quotesApi.settings).mockResolvedValue({ validityDays: 15, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: true, whatsAppTemplates: templates });
    vi.mocked(quotesApi.setWhatsAppTemplate).mockResolvedValue({
      validityDays: 15, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: true, whatsAppTemplateId: 't1', whatsAppTemplates: templates,
    });
    renderList();

    expect(await screen.findByText(/salen del WhatsApp de tu negocio con el texto de TienditApp/)).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Plantilla'), 'Cotización lista (aprobada en Meta)');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar plantilla' }));

    await waitFor(() => expect(quotesApi.setWhatsAppTemplate).toHaveBeenCalledWith('t1'));
    expect(await screen.findByText(/con la plantilla «Cotización lista»/)).toBeInTheDocument();
  });

  it('tells staff how the quotes go by WhatsApp, without letting them change it', async () => {
    vi.mocked(quotesApi.settings).mockResolvedValue({ validityDays: 15, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: true, whatsAppTemplates: [] });
    renderList('STAFF');

    expect(await screen.findByText(/salen del WhatsApp de tu negocio con el texto de TienditApp/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Plantilla')).not.toBeInTheDocument();
  });

  it('says nothing of WhatsApp templates while the business has not connected its WhatsApp', async () => {
    renderList();

    await screen.findByLabelText('Días de vigencia');
    expect(screen.queryByLabelText('Plantilla')).not.toBeInTheDocument();
  });
});
