import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
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

function renderList(role = 'TENANT_ADMIN', url = '/quotes') {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role, permissions: ['ManageOrders'] } } as unknown as ReturnType<typeof useAuth>);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route path="/quotes" element={<><Link to="/quotes?estado=por-convertir">Campana</Link><QuotesList /></>} />
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
    vi.mocked(quotesApi.settings).mockResolvedValue({ validityDays: 15, onlineReplies: true, depositAvailable: false, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: false, whatsAppTemplates: [] });
    vi.mocked(quotesApi.list).mockResolvedValue(page([
      { id: 'q1', number: 'COT-000007', revision: 2, status: QuoteStatus.Expired, customerName: 'Valeria Ruiz', customerPhone: '5522107765', total: 1160, validUntil: '2026-10-19', updatedAt: '2026-10-04T17:00:00Z', fromVisit: true },
    ]) as never);
  });

  it('turns on in this browser the sound of the answers to quotes, and says when it needs a touch (Fase 29Z)', async () => {
    window.localStorage.removeItem('tienditapp:quotes-sound');
    const user = userEvent.setup();
    renderList();

    const sound = await screen.findByRole('button', { name: 'Sonido para respuestas' });
    expect(sound).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('button', { name: 'Tono y volumen de los sonidos' })).toBeInTheDocument();
    await user.click(sound);

    expect(sound).toHaveAttribute('aria-pressed', 'true');
    expect(window.localStorage.getItem('tienditapp:quotes-sound')).toBe('on');
    // This test browser has no sound to start, so the touch it asks for never arrives.
    expect(screen.getByRole('status')).toHaveTextContent('Toca la pantalla o presiona una tecla para que suenen las respuestas.');
    window.localStorage.removeItem('tienditapp:quotes-sound');
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
    vi.mocked(quotesApi.updateSettings).mockResolvedValue({ validityDays: 30, onlineReplies: true, depositAvailable: false, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: false, whatsAppTemplates: [] });
    renderList();

    const days = await screen.findByLabelText('Días de vigencia');
    await userEvent.clear(days);
    await userEvent.type(days, '30');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar vigencia' }));

    await waitFor(() => expect(quotesApi.updateSettings).toHaveBeenCalledWith(30));
    expect(toast.success).toHaveBeenCalledWith('Tus cotizaciones valen 30 días desde que las envías.');
  });

  it('lets owners turn off the answers from the link, and only tells the rest how customers answer (Fase 29G)', async () => {
    vi.mocked(quotesApi.updateSettings).mockResolvedValue({ validityDays: 15, onlineReplies: false, depositAvailable: false, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: false, whatsAppTemplates: [] });
    renderList();

    const answers = await screen.findByRole('switch', { name: 'Tus clientes responden desde su enlace' });
    expect(answers).toBeChecked();
    expect(screen.getByText(/Tus clientes aceptan o rechazan la cotización desde su enlace/)).toBeInTheDocument();
    await userEvent.click(answers);

    await waitFor(() => expect(quotesApi.updateSettings).toHaveBeenCalledWith(15, false, undefined));
    expect(toast.success).toHaveBeenCalledWith('Listo: tus clientes te responden por mensaje o en persona, y tú registras su respuesta.');
    expect(await screen.findByText(/te responden por mensaje o en persona; tú registras su respuesta/)).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Tus clientes responden desde su enlace' })).not.toBeChecked();
  });

  it('lets owners ask a deposit once Mercado Pago collects it, and turning the answers off drops it too (Fase 31N)', async () => {
    const settings = { validityDays: 15, onlineReplies: true, depositAvailable: true, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: false, whatsAppTemplates: [] };
    vi.mocked(quotesApi.settings).mockResolvedValue(settings);
    vi.mocked(quotesApi.updateSettings).mockResolvedValueOnce({ ...settings, depositPercent: 30 });
    renderList();

    const deposit = await screen.findByLabelText('Anticipo para aceptar');
    expect(deposit).toHaveValue('0');
    await userEvent.selectOptions(deposit, '30');

    await waitFor(() => expect(quotesApi.updateSettings).toHaveBeenCalledWith(15, undefined, 30));
    expect(toast.success).toHaveBeenCalledWith('Listo: para aceptar desde su enlace, tus clientes pagan en línea el 30 % de lo que queda por pagar.');
    expect(await screen.findByText(/tu cliente paga en línea el 30 % de lo que queda por pagar/)).toBeInTheDocument();

    vi.mocked(quotesApi.updateSettings).mockResolvedValueOnce({ ...settings, onlineReplies: false, depositPercent: null });
    await userEvent.click(screen.getByRole('switch', { name: 'Tus clientes responden desde su enlace' }));
    await waitFor(() => expect(quotesApi.updateSettings).toHaveBeenCalledWith(15, false, 0));
    expect(toast.success).toHaveBeenCalledWith('Listo: tus clientes te responden por mensaje o en persona, y tú registras su respuesta; ya no pides anticipo.');
    // Without the answers on the link there is no deposit to ask.
    expect(screen.queryByLabelText('Anticipo para aceptar')).not.toBeInTheDocument();
  });

  it('keeps the share already asked while Mercado Pago cannot collect, offering only to turn it off (Fase 31N)', async () => {
    vi.mocked(quotesApi.settings).mockResolvedValue({
      validityDays: 15, onlineReplies: true, depositPercent: 30, depositAvailable: false, pricesIncludeTax: true, defaultTaxRate: 16,
      whatsAppConnected: false, whatsAppTemplates: [],
    });
    renderList();

    const deposit = await screen.findByLabelText('Anticipo para aceptar');
    expect(deposit).toBeEnabled();
    expect(deposit).toHaveValue('30');
    expect(screen.getByRole('option', { name: 'Sin anticipo' })).toBeEnabled();
    expect(screen.getByRole('option', { name: '30 % de lo que queda por pagar' })).toBeEnabled();
    expect(screen.getByRole('option', { name: '50 % de lo que queda por pagar' })).toBeDisabled();
    expect(screen.getByText(/Ahora tu cuenta de Mercado Pago no puede cobrar: revísala en/)).toBeInTheDocument();
  });

  it('says where to connect Mercado Pago before asking a deposit', async () => {
    renderList();

    expect(await screen.findByLabelText('Anticipo para aceptar')).toBeDisabled();
    expect(screen.getByText(/Para pedir anticipo, conecta tu cuenta de Mercado Pago en/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Pagos en línea' })).toHaveAttribute('href', '/settings/online-payments');
  });

  it('opens those still to convert from «Requiere atención», also when the bell is followed from this screen', async () => {
    renderList('TENANT_ADMIN', '/quotes?estado=por-convertir');

    expect(await screen.findByLabelText('Estado')).toHaveValue(String(QuoteListFilter.ToConvert));
    await waitFor(() => expect(quotesApi.list).toHaveBeenCalledWith(QuoteListFilter.ToConvert, '', 1, expect.anything()));
  });

  it('sets the filter the bell names when followed while the list is open', async () => {
    renderList();
    const filter = await screen.findByLabelText('Estado');
    expect(filter).toHaveValue(String(QuoteListFilter.Open));

    await userEvent.click(screen.getByRole('link', { name: 'Campana' }));

    await waitFor(() => expect(filter).toHaveValue(String(QuoteListFilter.ToConvert)));
    await waitFor(() => expect(quotesApi.list).toHaveBeenLastCalledWith(QuoteListFilter.ToConvert, '', 1, expect.anything()));
  });

  it('sets the filter the bell names again after it was changed by hand', async () => {
    renderList('TENANT_ADMIN', '/quotes?estado=por-convertir');
    const filter = await screen.findByLabelText('Estado');
    expect(filter).toHaveValue(String(QuoteListFilter.ToConvert));

    await userEvent.selectOptions(filter, String(QuoteListFilter.Open));
    expect(filter).toHaveValue(String(QuoteListFilter.Open));
    await userEvent.click(screen.getByRole('link', { name: 'Campana' }));

    await waitFor(() => expect(filter).toHaveValue(String(QuoteListFilter.ToConvert)));
  });

  it('marks the quotes the customer answered from the link', async () => {
    vi.mocked(quotesApi.list).mockResolvedValue(page([
      { id: 'q2', number: 'COT-000008', revision: 1, status: QuoteStatus.Accepted, customerName: 'Ana López', customerPhone: '5511112222', total: 990, validUntil: '2026-10-19', updatedAt: '2026-10-05T17:00:00Z', fromVisit: false, repliedFromLink: true },
    ]) as never);
    renderList();

    expect(await screen.findByText('Desde su enlace')).toBeInTheDocument();
  });

  it('only tells staff how long quotes are valid', async () => {
    renderList('STAFF');

    expect(await screen.findByText(/Una cotización vale 15 días desde que la envías/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Días de vigencia')).not.toBeInTheDocument();
    expect(screen.getByText(/Tus clientes aceptan o rechazan la cotización desde su enlace/)).toBeInTheDocument();
    expect(screen.queryByRole('switch', { name: 'Tus clientes responden desde su enlace' })).not.toBeInTheDocument();
  });

  it('lets owners pick the WhatsApp template of the quotes once the business connected its WhatsApp', async () => {
    const templates = [{ id: 't1', name: 'Cotización lista', body: 'Tu cotización {folio}: {enlace}', providerTemplateName: 'cotizacion_lista' }];
    vi.mocked(quotesApi.settings).mockResolvedValue({ validityDays: 15, onlineReplies: true, depositAvailable: false, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: true, whatsAppTemplates: templates });
    vi.mocked(quotesApi.setWhatsAppTemplate).mockResolvedValue({
      validityDays: 15, onlineReplies: true, depositAvailable: false, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: true, whatsAppTemplateId: 't1', whatsAppTemplates: templates,
    });
    renderList();

    expect(await screen.findByText(/salen del WhatsApp de tu negocio con el texto de TienditApp/)).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Plantilla'), 'Cotización lista (aprobada en Meta)');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar plantilla' }));

    await waitFor(() => expect(quotesApi.setWhatsAppTemplate).toHaveBeenCalledWith('t1'));
    expect(await screen.findByText(/con la plantilla «Cotización lista»/)).toBeInTheDocument();
  });

  it('tells staff how the quotes go by WhatsApp, without letting them change it', async () => {
    vi.mocked(quotesApi.settings).mockResolvedValue({ validityDays: 15, onlineReplies: true, depositAvailable: false, pricesIncludeTax: true, defaultTaxRate: 16, whatsAppConnected: true, whatsAppTemplates: [] });
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
