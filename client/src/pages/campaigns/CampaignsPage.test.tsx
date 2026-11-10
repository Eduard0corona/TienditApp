import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { messagingApi } from '../../api/messagingApi';
import { apiRequest } from '../../api/typedClient';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import {
  CampaignAudienceKind,
  CampaignStatus,
  MessagePurpose,
  OptOutSource,
  OutgoingMessageStatus,
  TenantCapability,
  type MessageTemplateDto,
  type MessagingOptOutDto,
  type MessagingSettingsDto,
  type OutgoingMessageDto,
} from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import CampaignsPage from './CampaignsPage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../api/messagingApi', () => ({
  messagingApi: {
    getTemplates: vi.fn(), createTemplate: vi.fn(), updateTemplate: vi.fn(), previewCampaign: vi.fn(), getMessages: vi.fn(),
    getOptOuts: vi.fn(), setOptOut: vi.fn(), getSettings: vi.fn(), updateReminders: vi.fn(),
  },
}));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));

const request = vi.mocked(apiRequest);
const api = vi.mocked(messagingApi);
const confirm = vi.fn();
const toast = { success: vi.fn(), error: vi.fn() };

/** A page as the API sends it. */
const apiPage = <T,>(items: T[], pageNumber = 1, totalPages = 1) => ({
  items, pageNumber, totalPages, totalCount: items.length, hasPreviousPage: pageNumber > 1, hasNextPage: pageNumber < totalPages,
});
/** The same page as axios answers it. */
const page = <T,>(items: T[], pageNumber = 1, totalPages = 1) => ({ data: apiPage(items, pageNumber, totalPages) }) as never;

const refused = (message: string, status = 400) => ({ isAxiosError: true, response: { status, data: { error: message } } });

function deferred() {
  let resolve: (value: unknown) => void = () => undefined;
  let reject: (reason: unknown) => void = () => undefined;
  const promise = new Promise((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** The answers in turn: a function throws or returns a promise; the last one repeats. */
const inTurn = (...replies: Array<() => unknown>) => {
  let next = 0;
  return () => replies[Math.min(next++, replies.length - 1)]();
};

/** The typed client answers by method and path: the campaigns and the messages of one read through it. */
function answer(paths: Record<string, (options: { query?: Record<string, unknown>; path?: Record<string, string> }) => unknown>) {
  request.mockImplementation(((method: string, path: string, options: { query?: Record<string, unknown> } = {}) => {
    const reply = paths[`${method} ${path}`];
    return reply ? Promise.resolve().then(() => reply(options)) : Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as never);
}

const campaignReads = () => request.mock.calls.filter(([method, path]) => method === 'get' && path === '/api/v1/messaging/campaigns');

/** Waits past the pause after typing in the search of Bajas. */
const pause = () => act(() => new Promise((resolve) => setTimeout(resolve, 300)));

function renderPage(path = '/campaigns', capabilities: number = TenantCapability.Appointments) {
  vi.mocked(useTenant).mockReturnValue({ capabilities } as unknown as ReturnType<typeof useTenant>);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}><CampaignsPage /></MemoryRouter>
    </QueryClientProvider>,
  );
  return { client };
}

const tab = (name: string) => screen.getByRole('tab', { name });

/** A campaign being sent to those who bought twice in 90 days, and one that already went out to everyone. */
const SENDING = {
  id: 'c1', name: '2x1 de fin de semana', templateId: 't1', templateName: 'Promoción',
  audienceKind: CampaignAudienceKind.Buyers, audienceMinimumOrders: 2, audienceDays: 90, status: CampaignStatus.Sending,
  scheduledAt: '2026-10-09T15:00:00Z', startedAt: '2026-10-09T15:00:00Z', recipientCount: 40, excludedCount: 3,
  createdBy: 'mariana@cafe.mx', createdAt: '2026-10-09T14:00:00Z',
  progress: { pending: 9, sent: 30, delivered: 25, read: 10, failed: 1, skipped: 0 },
};
const DONE = {
  ...SENDING, id: 'c2', name: 'Día de muertos', audienceKind: CampaignAudienceKind.AllCustomers, status: CampaignStatus.Completed,
  excludedCount: 0, progress: { pending: 0, sent: 12, delivered: 0, read: 0, failed: 0, skipped: 2 },
};

const template = (over: Partial<MessageTemplateDto>): MessageTemplateDto => ({
  id: 't1', name: 'Promoción', purpose: MessagePurpose.Campaign, body: 'Hola {nombre}, este fin de semana hay 2x1 en {negocio}.',
  providerTemplateName: 'promo_fin_de_semana', languageCode: 'es_MX', isActive: true, parameters: ['nombre', 'negocio'],
  createdAt: '2026-10-01T00:00:00Z', ...over,
});
const PROMO = template({});
const OLD_REMINDER = template({
  id: 't2', name: 'Recordatorio viejo', purpose: MessagePurpose.AppointmentReminder, body: 'Tu cita es el {fecha}.',
  providerTemplateName: undefined, isActive: false,
});

const message = (over: Partial<OutgoingMessageDto>): OutgoingMessageDto => ({
  id: 'm1', purpose: MessagePurpose.AppointmentReminder, phone: '+529991112233', recipientName: 'Ana López',
  body: 'Hola Ana, te recordamos tu cita de corte mañana a las 10:00.', status: OutgoingMessageStatus.Sent, attempts: 1,
  createdAt: '2026-10-09T15:00:00Z', nextAttemptAt: '2026-10-09T15:00:00Z', sentAt: '2026-10-09T15:00:05Z', ...over,
});

describe('CampaignsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    setBusinessTimeZone('America/Mexico_City');
    vi.mocked(useConfirm).mockReturnValue({ confirm } as unknown as ReturnType<typeof useConfirm>);
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    answer({ 'get /api/v1/messaging/campaigns': () => apiPage([]) });
    api.getTemplates.mockResolvedValue({ data: [] } as never);
  });

  afterEach(() => setBusinessTimeZone(null));

  it('shows the appointment reminders only to a business with appointments and opens the tab in the address', async () => {
    const user = userEvent.setup();
    api.getOptOuts.mockResolvedValue(page([]));
    renderPage('/campaigns?tab=reminders', 0);

    expect(screen.getAllByRole('tab').map((item) => item.textContent))
      .toEqual(['Campañas', 'Plantillas', 'Recordatorios del fiado', 'Bajas']);
    // Without appointments, the tab asked for does not exist: it opens the campaigns.
    expect(tab('Campañas')).toHaveAttribute('aria-selected', 'true');
    expect(await screen.findByText('Todavía no has enviado campañas.')).toBeInTheDocument();

    await user.click(tab('Bajas'));
    expect(tab('Bajas')).toHaveAttribute('aria-selected', 'true');
    expect(tab('Campañas')).toHaveAttribute('aria-selected', 'false');
    expect(await screen.findByText('Nadie ha pedido dejar de recibir mensajes.')).toBeInTheDocument();
  });

  it('opens the appointment reminders from the address when the business has appointments', async () => {
    api.getSettings.mockReturnValue(new Promise(() => undefined) as never);
    renderPage('/campaigns?tab=reminders');

    expect(screen.getAllByRole('tab').map((item) => item.textContent))
      .toEqual(['Campañas', 'Plantillas', 'Recordatorios de citas', 'Recordatorios del fiado', 'Bajas']);
    expect(tab('Recordatorios de citas')).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('status')).toHaveTextContent('Cargando…');
  });

  describe('Campañas', () => {
    it('lists each campaign with whom it reaches and how it went, and cancels one that is being sent', async () => {
      const user = userEvent.setup();
      confirm.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
      answer({
        'get /api/v1/messaging/campaigns': () => apiPage([SENDING, DONE]),
        'post /api/v1/messaging/campaigns/{id}/cancel': () => ({ ...SENDING, status: CampaignStatus.Cancelled }),
      });
      renderPage();

      const sending = (await screen.findByRole('button', { name: '2x1 de fin de semana' })).closest('tr') as HTMLElement;
      expect(within(sending).getByText('Con 2 compras o más en los últimos 90 días')).toBeInTheDocument();
      expect(within(sending).getAllByText('Enviando').length).toBeGreaterThan(0);
      expect(within(sending).getAllByText(/30 enviados · 25 entregados · 10 leídos · 1 fallaron · 9 pendientes/).length).toBeGreaterThan(0);
      expect(within(sending).getByText('3 con baja, excluidos')).toBeInTheDocument();
      const done = screen.getByRole('button', { name: 'Día de muertos' }).closest('tr') as HTMLElement;
      expect(within(done).getByText('Todos los clientes con teléfono')).toBeInTheDocument();
      expect(within(done).getAllByText(/12 enviados · 2 no enviados/).length).toBeGreaterThan(0);
      // What already went out cannot be cancelled.
      expect(within(done).queryByRole('button', { name: 'Cancelar' })).not.toBeInTheDocument();

      // Saying no sends nothing.
      await user.click(within(sending).getByRole('button', { name: 'Cancelar' }));
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Cancelar «2x1 de fin de semana»', danger: true }));
      expect(request).not.toHaveBeenCalledWith('post', '/api/v1/messaging/campaigns/{id}/cancel', expect.anything());

      const readsBefore = campaignReads().length;
      await user.click(within(sending).getByRole('button', { name: 'Cancelar' }));
      await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Campaña cancelada.'));
      expect(request).toHaveBeenCalledWith('post', '/api/v1/messaging/campaigns/{id}/cancel', { path: { id: 'c1' } });
      // The list is read again with what changed.
      await waitFor(() => expect(campaignReads().length).toBeGreaterThan(readsBefore));
    });

    it('says the campaigns could not be read instead of «no campaigns», and reads them again', async () => {
      const user = userEvent.setup();
      answer({
        'get /api/v1/messaging/campaigns': inTurn(
          () => Promise.reject(refused('El servicio de mensajes no responde.')),
          () => apiPage([DONE]),
        ),
      });
      renderPage();

      expect(await screen.findByRole('alert')).toHaveTextContent('El servicio de mensajes no responde.');
      expect(screen.queryByText('Todavía no has enviado campañas.')).not.toBeInTheDocument();
      expect(screen.queryByText('Cargando…')).not.toBeInTheDocument();

      await user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Reintentar' }));
      expect(await screen.findByRole('button', { name: 'Día de muertos' })).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('keeps the campaigns read when reading them again fails, says so and waits for the retry', async () => {
      const user = userEvent.setup();
      const retry = deferred();
      answer({
        'get /api/v1/messaging/campaigns': inTurn(
          () => apiPage([DONE]),
          () => Promise.reject(refused('Se perdió la conexión.')),
          () => retry.promise,
        ),
      });
      const { client } = renderPage();
      await screen.findByRole('button', { name: 'Día de muertos' });

      await act(async () => client.invalidateQueries({ queryKey: ['messaging', 'campaigns'] }));
      expect(await screen.findByRole('alert')).toHaveTextContent('Se perdió la conexión.');
      expect(screen.getByRole('button', { name: 'Día de muertos' })).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Reintentar' }));
      expect(screen.getByRole('button', { name: 'Reintentar' })).toBeDisabled();
      await act(async () => retry.resolve(apiPage([DONE])));
      await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
    });

    it('reads the next page of campaigns', async () => {
      const user = userEvent.setup();
      answer({
        'get /api/v1/messaging/campaigns': ({ query }) => (query?.pageNumber === 2 ? apiPage([DONE], 2, 2) : apiPage([SENDING], 1, 2)),
      });
      renderPage();

      expect(await screen.findByText('Página 1 de 2')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Anterior' })).toBeDisabled();
      await user.click(screen.getByRole('button', { name: 'Siguiente' }));

      expect(await screen.findByRole('button', { name: 'Día de muertos' })).toBeInTheDocument();
      expect(screen.getByText('Página 2 de 2')).toBeInTheDocument();
      expect(campaignReads().at(-1)?.[2]).toEqual(expect.objectContaining({ query: { pageNumber: 2, pageSize: 10 } }));
    });

    it('says when the templates of a new campaign could not be read, and asks for them once while they load', async () => {
      const user = userEvent.setup();
      const second = deferred();
      api.getTemplates
        .mockRejectedValueOnce(refused('No se pudieron leer las plantillas de Meta.'))
        .mockReturnValueOnce(second.promise as never);
      renderPage();
      await screen.findByText('Todavía no has enviado campañas.');

      await user.click(screen.getByRole('button', { name: 'Nueva campaña' }));
      expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron leer las plantillas de Meta.');

      // Trying again takes the notice away, and the button waits for the templates.
      await user.click(screen.getByRole('button', { name: 'Nueva campaña' }));
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Nueva campaña' })).toBeDisabled();
      await user.click(screen.getByRole('button', { name: 'Nueva campaña' }));
      expect(api.getTemplates).toHaveBeenCalledTimes(2);
      expect(api.getTemplates).toHaveBeenLastCalledWith({ purpose: MessagePurpose.Campaign, includeInactive: false });

      await act(async () => second.resolve({ data: [PROMO] }));
      expect(await screen.findByRole('dialog', { name: 'Nueva campaña' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Nueva campaña' })).toBeEnabled();
    });

    it('sends to the templates when there is no campaign template yet', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Todavía no has enviado campañas.');

      await user.click(screen.getByRole('button', { name: 'Nueva campaña' }));
      const dialog = within(await screen.findByRole('dialog', { name: 'Nueva campaña' }));
      expect(dialog.getByText('Primero crea una plantilla de campaña con el mensaje.')).toBeInTheDocument();
      expect(dialog.queryByRole('button', { name: 'Enviar campaña' })).not.toBeInTheDocument();

      await user.click(dialog.getByRole('button', { name: 'Crear plantilla' }));
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
      expect(tab('Plantillas')).toHaveAttribute('aria-selected', 'true');
      expect(await screen.findByText('Todavía no hay plantillas. Crea una para tu primera campaña o tus recordatorios.')).toBeInTheDocument();
    });

    it('previews whom a campaign reaches, forgets it when the audience changes, and sends what it previewed', async () => {
      const user = userEvent.setup();
      const previewing = deferred();
      api.getTemplates.mockResolvedValue({ data: [PROMO, template({ id: 't3', name: 'Bienvenida' })] } as never);
      api.previewCampaign
        .mockReturnValueOnce(previewing.promise as never)
        .mockResolvedValue({
          data: { recipientCount: 80, excludedCount: 0, truncated: false, maxRecipients: 1000, usesApprovedTemplate: true, samples: [] },
        } as never);
      answer({
        'get /api/v1/messaging/campaigns': () => apiPage([]),
        'post /api/v1/messaging/campaigns': () => SENDING,
      });
      confirm.mockResolvedValue(true);
      renderPage();
      await screen.findByText('Todavía no has enviado campañas.');

      await user.click(screen.getByRole('button', { name: 'Nueva campaña' }));
      const dialog = within(await screen.findByRole('dialog', { name: 'Nueva campaña' }));
      await user.type(dialog.getByLabelText('Nombre'), '  Puente de noviembre ');
      await user.click(dialog.getByRole('button', { name: '¿A cuántos llega?' }));
      expect(dialog.getByRole('button', { name: 'Calculando…' })).toBeDisabled();
      expect(api.previewCampaign).toHaveBeenCalledWith({ templateId: 't1', audienceKind: CampaignAudienceKind.Buyers, minimumOrders: 1, days: 90 });

      await act(async () => previewing.resolve({
        data: {
          recipientCount: 1000, excludedCount: 4, truncated: true, maxRecipients: 1000, usesApprovedTemplate: false,
          samples: [{ phone: '+529991112233', recipientName: 'Ana López', text: 'Hola Ana, este fin de semana hay 2x1.' }],
        },
      }));
      expect(dialog.getByText(/Llega a/)).toHaveTextContent('Llega a 1000 clientes · 4 excluidos por baja.');
      expect(dialog.getByText('Coinciden más de 1000 clientes: se enviará a los primeros 1000.')).toBeInTheDocument();
      expect(dialog.getByText(/no tiene nombre de Meta/)).toBeInTheDocument();
      expect(dialog.getByText('Hola Ana, este fin de semana hay 2x1.')).toBeInTheDocument();

      // Another audience no longer reaches those: the preview goes away.
      await user.click(dialog.getByLabelText('Clientes que dejaron de venir'));
      expect(dialog.queryByText(/Llega a/)).not.toBeInTheDocument();
      expect(dialog.getByLabelText('Días sin comprar')).toHaveValue(60);
      await user.clear(dialog.getByLabelText('Compras mínimas'));
      await user.type(dialog.getByLabelText('Compras mínimas'), '3');
      await user.click(dialog.getByRole('button', { name: '¿A cuántos llega?' }));
      expect(await dialog.findByText(/Llega a/)).toHaveTextContent('Llega a 80 clientes.');
      expect(api.previewCampaign).toHaveBeenLastCalledWith({ templateId: 't1', audienceKind: CampaignAudienceKind.Lapsed, minimumOrders: 3, days: 60 });

      await user.click(dialog.getByRole('button', { name: 'Enviar campaña' }));
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({
        title: 'Enviar la campaña ahora',
        message: 'Llegará a 80 clientes. Cada mensaje puede tener costo en WhatsApp.',
        confirmLabel: 'Enviar',
      }));
      await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Campaña en camino: se envía en segundo plano.'));
      expect(request).toHaveBeenCalledWith('post', '/api/v1/messaging/campaigns', {
        body: {
          templateId: 't1', audienceKind: CampaignAudienceKind.Lapsed, minimumOrders: 3, days: 60, name: 'Puente de noviembre',
          scheduledAt: null,
        },
      });
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('does not say it is calculating while it sends, keeps the form open and says what the server refused', async () => {
      const user = userEvent.setup();
      const sending = deferred();
      api.getTemplates.mockResolvedValue({ data: [PROMO] } as never);
      answer({
        'get /api/v1/messaging/campaigns': () => apiPage([]),
        'post /api/v1/messaging/campaigns': () => sending.promise,
      });
      confirm.mockResolvedValue(true);
      renderPage();
      await screen.findByText('Todavía no has enviado campañas.');

      await user.click(screen.getByRole('button', { name: 'Nueva campaña' }));
      const dialog = within(await screen.findByRole('dialog', { name: 'Nueva campaña' }));
      await user.type(dialog.getByLabelText('Nombre'), 'A todos');
      await user.click(dialog.getByLabelText('Todos los clientes con teléfono'));
      // Everyone has no purchases or days to ask for.
      expect(dialog.queryByLabelText('Compras mínimas')).not.toBeInTheDocument();
      await user.click(dialog.getByRole('button', { name: 'Enviar campaña' }));
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({
        message: 'Los clientes se eligen cuando empiece. Cada mensaje puede tener costo en WhatsApp.',
      }));

      await waitFor(() => expect(dialog.getByRole('button', { name: 'Enviar campaña' })).toBeDisabled());
      expect(dialog.getByRole('button', { name: '¿A cuántos llega?' })).toBeDisabled();
      expect(dialog.queryByRole('button', { name: 'Calculando…' })).not.toBeInTheDocument();
      expect(dialog.getByRole('button', { name: 'Cancelar' })).toBeDisabled();
      await user.keyboard('{Escape}');
      expect(screen.getByRole('dialog', { name: 'Nueva campaña' })).toBeInTheDocument();
      expect(request).toHaveBeenCalledWith('post', '/api/v1/messaging/campaigns', {
        body: {
          templateId: 't1', audienceKind: CampaignAudienceKind.AllCustomers, minimumOrders: 1, days: undefined, name: 'A todos',
          scheduledAt: null,
        },
      });

      await act(async () => sending.reject(refused('Conecta WhatsApp para enviar campañas.')));
      expect(await dialog.findByRole('alert')).toHaveTextContent('Conecta WhatsApp para enviar campañas.');
      expect(dialog.getByLabelText('Nombre')).toHaveValue('A todos');
      expect(dialog.getByRole('button', { name: 'Enviar campaña' })).toBeEnabled();
      expect(toast.success).not.toHaveBeenCalled();
    });

    it('shows the messages of a campaign and says when they could not be read, without loading forever', async () => {
      const user = userEvent.setup();
      answer({
        'get /api/v1/messaging/campaigns': () => apiPage([SENDING]),
        'get /api/v1/messaging/messages': inTurn(
          () => Promise.reject(refused('No se pudieron leer los mensajes de la campaña.')),
          () => apiPage([
            message({ id: 'm1', purpose: MessagePurpose.Campaign, readAt: '2026-10-09T16:00:00Z' }),
            message({
              id: 'm2', purpose: MessagePurpose.Campaign, recipientName: undefined, phone: '+529998887766', sentAt: undefined,
              status: OutgoingMessageStatus.Failed, lastError: 'El número no tiene WhatsApp.',
            }),
          ]),
        ),
      });
      renderPage();

      await user.click(await screen.findByRole('button', { name: '2x1 de fin de semana' }));
      const dialog = within(await screen.findByRole('dialog', { name: '2x1 de fin de semana' }));
      expect(await dialog.findByRole('alert')).toHaveTextContent('No se pudieron leer los mensajes de la campaña.');
      expect(dialog.queryByText('Cargando…')).not.toBeInTheDocument();
      expect(dialog.queryByText('No hubo clientes que cumplieran el filtro.')).not.toBeInTheDocument();
      expect(request).toHaveBeenCalledWith('get', '/api/v1/messaging/messages', expect.objectContaining({
        query: { campaignId: 'c1', pageNumber: 1, pageSize: 20 },
      }));

      await user.click(dialog.getByRole('button', { name: 'Reintentar' }));
      const read = (await dialog.findByText('Ana López')).closest('tr') as HTMLElement;
      expect(within(read).getByText('Enviado')).toBeInTheDocument();
      expect(within(read).getByText(/^Leído /)).toBeInTheDocument();
      const failed = dialog.getByText('+529998887766').closest('tr') as HTMLElement;
      expect(within(failed).getByText('Cliente')).toBeInTheDocument();
      expect(within(failed).getByText('Falló')).toBeInTheDocument();
      expect(within(failed).getByText('El número no tiene WhatsApp.')).toBeInTheDocument();
      expect(dialog.queryByRole('alert')).not.toBeInTheDocument();
    });
  });

  describe('Plantillas', () => {
    it('says the templates could not be read instead of «no templates», and lists them on retrying', async () => {
      const user = userEvent.setup();
      const retry = deferred();
      api.getTemplates
        .mockRejectedValueOnce(refused('No se pudieron leer las plantillas.'))
        .mockReturnValueOnce(retry.promise as never);
      renderPage('/campaigns?tab=templates');

      expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron leer las plantillas.');
      expect(screen.queryByText(/Todavía no hay plantillas/)).not.toBeInTheDocument();
      expect(screen.queryByText('Cargando…')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Reintentar' }));
      expect(screen.getByRole('button', { name: 'Reintentar' })).toBeDisabled();
      expect(screen.getByRole('status')).toHaveTextContent('Cargando…');
      await act(async () => retry.resolve({ data: [PROMO, OLD_REMINDER] }));
      const promo = (await screen.findByText('Promoción')).closest('tr') as HTMLElement;
      expect(within(promo).getByText('Campaña')).toBeInTheDocument();
      expect(within(promo).getByText('promo_fin_de_semana')).toBeInTheDocument();
      const old = screen.getByText('Recordatorio viejo').closest('tr') as HTMLElement;
      expect(within(old).getByText('Inactiva')).toBeInTheDocument();
      expect(within(old).getByText('Recordatorio de cita')).toBeInTheDocument();
      expect(within(old).getByText('Solo 24 h')).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('creates a template with the variables at the cursor and tells their order in Meta', async () => {
      const user = userEvent.setup();
      api.getTemplates.mockResolvedValueOnce({ data: [] } as never).mockResolvedValue({ data: [PROMO] } as never);
      api.createTemplate.mockResolvedValue({ data: PROMO } as never);
      renderPage('/campaigns?tab=templates');
      await screen.findByText(/Todavía no hay plantillas/);

      await user.click(screen.getByRole('button', { name: 'Nueva plantilla' }));
      const dialog = within(await screen.findByRole('dialog', { name: 'Nueva plantilla' }));
      await user.type(dialog.getByLabelText('Nombre'), ' Promo ');
      const body = dialog.getByLabelText('Mensaje') as HTMLTextAreaElement;
      await user.type(body, 'Hola , ven a  ');
      body.setSelectionRange(5, 5);
      await user.click(dialog.getByRole('button', { name: '{nombre}' }));
      expect(body).toHaveValue('Hola {nombre}, ven a  ');
      expect(dialog.getByText('En Meta, las variables van en este orden: {{1}} = {nombre}.')).toBeInTheDocument();
      // Each purpose offers its own variables.
      await user.selectOptions(dialog.getByLabelText('Uso'), 'Recordatorio del fiado');
      expect(dialog.getByRole('button', { name: '{vencido}' })).toBeInTheDocument();
      await user.selectOptions(dialog.getByLabelText('Uso'), 'Campaña');
      expect(dialog.queryByRole('button', { name: '{vencido}' })).not.toBeInTheDocument();
      await user.type(dialog.getByLabelText('Plantilla aprobada en Meta (opcional)'), 'promo_semana');

      await user.click(dialog.getByRole('button', { name: 'Guardar' }));
      await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Plantilla creada.'));
      expect(api.createTemplate).toHaveBeenCalledWith({
        name: 'Promo', purpose: MessagePurpose.Campaign, body: 'Hola {nombre}, ven a', providerTemplateName: 'promo_semana',
        languageCode: 'es_MX', isActive: true,
      });
      expect(await screen.findByText('Promoción')).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('edits a template without changing its use, stays open while saving and says what the server refused', async () => {
      const user = userEvent.setup();
      const saving = deferred();
      api.getTemplates.mockResolvedValue({ data: [PROMO] } as never);
      api.updateTemplate.mockReturnValue(saving.promise as never);
      renderPage('/campaigns?tab=templates');

      await user.click(within((await screen.findByText('Promoción')).closest('tr') as HTMLElement).getByRole('button', { name: 'Editar' }));
      const dialog = within(await screen.findByRole('dialog', { name: 'Editar plantilla' }));
      expect(dialog.getByLabelText('Uso')).toBeDisabled();
      expect(dialog.getByLabelText('Mensaje')).toHaveValue(PROMO.body);
      await user.click(dialog.getByRole('switch', { name: /Activa/ }));
      await user.click(dialog.getByRole('button', { name: 'Guardar' }));

      expect(dialog.getByRole('button', { name: 'Guardando…' })).toBeDisabled();
      expect(dialog.getByRole('button', { name: 'Cancelar' })).toBeDisabled();
      await user.keyboard('{Escape}');
      expect(screen.getByRole('dialog', { name: 'Editar plantilla' })).toBeInTheDocument();
      expect(api.updateTemplate).toHaveBeenCalledWith('t1', expect.objectContaining({ name: 'Promoción', isActive: false }));

      await act(async () => saving.reject(refused('Ya existe una plantilla con ese nombre.')));
      expect(await dialog.findByRole('alert')).toHaveTextContent('Ya existe una plantilla con ese nombre.');
      expect(dialog.getByRole('button', { name: 'Guardar' })).toBeEnabled();
      expect(toast.success).not.toHaveBeenCalled();
    });

    it('shows the list read after saving even if the one of opening answers later', async () => {
      const user = userEvent.setup();
      const opening = deferred();
      const created = template({ id: 't9', name: 'Bienvenida' });
      api.getTemplates.mockReturnValueOnce(opening.promise as never).mockResolvedValue({ data: [PROMO, created] } as never);
      api.createTemplate.mockResolvedValue({ data: created } as never);
      renderPage('/campaigns?tab=templates');
      expect(screen.getByRole('status')).toHaveTextContent('Cargando…');

      await user.click(screen.getByRole('button', { name: 'Nueva plantilla' }));
      const dialog = within(await screen.findByRole('dialog', { name: 'Nueva plantilla' }));
      await user.type(dialog.getByLabelText('Nombre'), 'Bienvenida');
      await user.type(dialog.getByLabelText('Mensaje'), 'Hola, bienvenido.');
      await user.click(dialog.getByRole('button', { name: 'Guardar' }));
      expect(await screen.findByText('Bienvenida')).toBeInTheDocument();

      await act(async () => opening.resolve({ data: [PROMO] }));
      expect(screen.getByText('Bienvenida')).toBeInTheDocument();
      expect(screen.queryByText('Cargando…')).not.toBeInTheDocument();
    });

    it('keeps loading while the list after a save is read, though the one of opening answers first', async () => {
      const user = userEvent.setup();
      const opening = deferred();
      const afterSave = deferred();
      const created = template({ id: 't9', name: 'Bienvenida' });
      api.getTemplates.mockReturnValueOnce(opening.promise as never).mockReturnValueOnce(afterSave.promise as never);
      api.createTemplate.mockResolvedValue({ data: created } as never);
      renderPage('/campaigns?tab=templates');

      await user.click(screen.getByRole('button', { name: 'Nueva plantilla' }));
      const dialog = within(await screen.findByRole('dialog', { name: 'Nueva plantilla' }));
      await user.type(dialog.getByLabelText('Nombre'), 'Bienvenida');
      await user.type(dialog.getByLabelText('Mensaje'), 'Hola, bienvenido.');
      await user.click(dialog.getByRole('button', { name: 'Guardar' }));
      await waitFor(() => expect(api.getTemplates).toHaveBeenCalledTimes(2));

      await act(async () => opening.resolve({ data: [PROMO] }));
      expect(screen.getByRole('status')).toHaveTextContent('Cargando…');
      expect(screen.queryByText('Promoción')).not.toBeInTheDocument();

      await act(async () => afterSave.resolve({ data: [PROMO, created] }));
      expect(screen.getByText('Bienvenida')).toBeInTheDocument();
      expect(screen.queryByRole('status')).not.toBeInTheDocument();
    });
  });

  describe('Recordatorios de citas', () => {
    const SETTINGS: MessagingSettingsDto = {
      whatsAppConnected: false, appointmentsEnabled: true, appointmentRemindersEnabled: false, appointmentReminderHoursBefore: 24,
      appointmentReminderTemplateId: undefined, defaultReminderBody: 'Hola {nombre}, te recordamos tu cita del {fecha}.',
      minReminderHoursBefore: 1, maxReminderHoursBefore: 72, campaignPlaceholders: [], reminderPlaceholders: [],
      creditReminderPlaceholders: [], creditDueSoonPlaceholders: [],
    };
    const TOMORROW = template({
      id: 'r1', name: 'Cita mañana', purpose: MessagePurpose.AppointmentReminder, body: 'Hola {nombre}, mañana es tu cita.',
      providerTemplateName: 'cita_manana',
    });

    beforeEach(() => {
      api.getSettings.mockResolvedValue({ data: SETTINGS } as never);
      api.getTemplates.mockResolvedValue({ data: [TOMORROW] } as never);
      api.getMessages.mockImplementation(((params: { purpose: MessagePurpose }) =>
        Promise.resolve(page(params.purpose === MessagePurpose.AppointmentReminder ? [message({})] : [])))as never);
    });

    it('shows when the reminders go out with which text and saves them, without changing them while saving', async () => {
      const user = userEvent.setup();
      const saving = deferred();
      api.updateReminders.mockReturnValue(saving.promise as never);
      renderPage('/campaigns?tab=reminders');

      await screen.findByRole('heading', { name: 'Recordatorios de citas' });
      expect(screen.getByRole('link', { name: 'Ir a WhatsApp' })).toHaveAttribute('href', '/settings/whatsapp');
      expect(screen.getByLabelText('Horas antes')).toHaveValue(24);
      expect(screen.getByText(SETTINGS.defaultReminderBody)).toBeInTheDocument();
      expect(screen.getByText(/Sin plantilla aprobada en Meta/)).toBeInTheDocument();
      expect(screen.queryByText(/La plantilla elegida se desactivó/)).not.toBeInTheDocument();
      expect(api.getTemplates).toHaveBeenCalledWith({ purpose: MessagePurpose.AppointmentReminder, includeInactive: false });

      await user.selectOptions(screen.getByLabelText('Mensaje'), 'Cita mañana');
      expect(screen.getByText('Hola {nombre}, mañana es tu cita.')).toBeInTheDocument();
      expect(screen.queryByText(/Sin plantilla aprobada en Meta/)).not.toBeInTheDocument();
      await user.click(screen.getByRole('switch', { name: 'Enviar recordatorios' }));
      await user.clear(screen.getByLabelText('Horas antes'));
      await user.type(screen.getByLabelText('Horas antes'), '12');
      await user.click(screen.getByRole('button', { name: 'Guardar' }));

      expect(screen.getByRole('button', { name: 'Guardando…' })).toBeDisabled();
      expect(screen.getByRole('switch', { name: 'Enviar recordatorios' })).toBeDisabled();
      expect(screen.getByLabelText('Horas antes')).toBeDisabled();
      expect(screen.getByLabelText('Mensaje')).toBeDisabled();
      expect(api.updateReminders).toHaveBeenCalledWith({ enabled: true, hoursBefore: 12, templateId: 'r1' });

      await act(async () => saving.resolve({
        data: { ...SETTINGS, appointmentRemindersEnabled: true, appointmentReminderHoursBefore: 12, appointmentReminderTemplateId: 'r1' },
      }));
      expect(toast.success).toHaveBeenCalledWith('Recordatorios activados.');
      expect(screen.getByRole('switch', { name: 'Enviar recordatorios' })).toBeEnabled();
      expect(screen.getByRole('switch', { name: 'Enviar recordatorios' })).toBeChecked();
      expect(screen.getByLabelText('Horas antes')).toHaveValue(12);
      expect(screen.getByLabelText('Mensaje')).toHaveValue('r1');
    });

    it('lists the last reminders and the notices of appointments', async () => {
      renderPage('/campaigns?tab=reminders');

      const reminders = (await screen.findByRole('heading', { name: 'Últimos recordatorios' })).closest('.card') as HTMLElement;
      const row = within(reminders).getByText('Ana López').closest('tr') as HTMLElement;
      expect(within(row).getByText('Enviado')).toBeInTheDocument();
      expect(within(row).getByText('Hola Ana, te recordamos tu cita de corte mañana a las 10:00.')).toBeInTheDocument();
      const notices = screen.getByRole('heading', { name: 'Avisos de citas' }).closest('.card') as HTMLElement;
      expect(within(notices).getByText('Todavía no se ha enviado ningún aviso.')).toBeInTheDocument();
      expect(api.getMessages).toHaveBeenCalledWith({ purpose: MessagePurpose.AppointmentNotice, pageNumber: 1, pageSize: 20 });
    });

    it('says the hours go from the least to the most instead of saving others', async () => {
      const user = userEvent.setup();
      api.updateReminders.mockResolvedValue({ data: { ...SETTINGS, appointmentReminderHoursBefore: 72 } } as never);
      renderPage('/campaigns?tab=reminders');
      const hours = await screen.findByLabelText('Horas antes');

      for (const typed of ['', '100', '0', '2.5']) {
        await user.clear(hours);
        if (typed) await user.type(hours, typed);
        await user.click(screen.getByRole('button', { name: 'Guardar' }));
        expect(screen.getByRole('alert')).toHaveTextContent('El recordatorio se envía de 1 a 72 horas antes de la cita.');
      }
      expect(api.updateReminders).not.toHaveBeenCalled();

      await user.clear(hours);
      await user.type(hours, '72');
      await user.click(screen.getByRole('button', { name: 'Guardar' }));
      await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Recordatorios guardados.'));
      expect(api.updateReminders).toHaveBeenCalledWith({ enabled: false, hoursBefore: 72, templateId: undefined });
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('says the template chosen was deactivated and saves the default text that is being sent', async () => {
      const user = userEvent.setup();
      api.getSettings.mockResolvedValue({ data: { ...SETTINGS, appointmentReminderTemplateId: 'gone' } } as never);
      api.updateReminders.mockResolvedValue({ data: SETTINGS } as never);
      renderPage('/campaigns?tab=reminders');

      expect(await screen.findByText('La plantilla elegida se desactivó: se envía el texto predeterminado hasta que elijas otra.'))
        .toBeInTheDocument();
      expect(screen.getByLabelText('Mensaje')).toHaveValue('');
      expect(screen.getByText(SETTINGS.defaultReminderBody)).toBeInTheDocument();
      // Choosing another one says it no more.
      await user.selectOptions(screen.getByLabelText('Mensaje'), 'Cita mañana');
      expect(screen.queryByText(/La plantilla elegida se desactivó/)).not.toBeInTheDocument();
      await user.selectOptions(screen.getByLabelText('Mensaje'), 'Texto predeterminado');

      await user.click(screen.getByRole('button', { name: 'Guardar' }));
      await waitFor(() => expect(toast.success).toHaveBeenCalled());
      expect(api.updateReminders).toHaveBeenCalledWith({ enabled: false, hoursBefore: 24, templateId: undefined });
      expect(screen.queryByText(/La plantilla elegida se desactivó/)).not.toBeInTheDocument();
    });

    it('says the reminders could not be read and reads them again', async () => {
      const user = userEvent.setup();
      const retry = deferred();
      api.getSettings
        .mockRejectedValueOnce(refused('No se pudo leer la configuración de mensajes.'))
        .mockReturnValueOnce(retry.promise as never);
      renderPage('/campaigns?tab=reminders');

      expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo leer la configuración de mensajes.');
      await user.click(screen.getByRole('button', { name: 'Reintentar' }));
      // Reading again takes the notice away while it loads.
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(screen.getByRole('status')).toHaveTextContent('Cargando…');
      await act(async () => retry.resolve({ data: SETTINGS }));
      expect(await screen.findByRole('heading', { name: 'Recordatorios de citas' })).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(api.getSettings).toHaveBeenCalledTimes(2);
    });
  });

  describe('Recordatorios del fiado', () => {
    it('lists the notices before something comes due and the reminders of what is overdue', async () => {
      api.getMessages.mockImplementation(((params: { purpose: MessagePurpose }) => Promise.resolve(page(
        params.purpose === MessagePurpose.CreditReminder
          ? [message({
            id: 'm7', purpose: MessagePurpose.CreditReminder, recipientName: 'Luis Pech', status: OutgoingMessageStatus.Failed,
            sentAt: undefined, lastError: 'Fuera de las 24 horas sin plantilla aprobada.', body: 'Hola Luis, tienes $350.00 vencidos.',
          })]
          : [],
      ))) as never);
      renderPage('/campaigns?tab=creditReminders');

      const notices = (await screen.findByRole('heading', { name: 'Avisos antes de vencer' })).closest('.card') as HTMLElement;
      expect(within(notices).getByText('Todavía no se ha enviado ningún aviso antes de vencer.')).toBeInTheDocument();
      const reminders = screen.getByRole('heading', { name: 'Recordatorios del fiado' }).closest('.card') as HTMLElement;
      const row = within(reminders).getByText('Luis Pech').closest('tr') as HTMLElement;
      expect(within(row).getByText('Falló')).toBeInTheDocument();
      expect(within(row).getByText('Fuera de las 24 horas sin plantilla aprobada.')).toBeInTheDocument();
      expect(screen.getAllByRole('link', { name: 'Clientes › Por cobrar' }).map((link) => link.getAttribute('href')))
        .toEqual(['/customers/receivables', '/customers/receivables']);
      expect(api.getMessages).toHaveBeenCalledWith({ purpose: MessagePurpose.CreditDueSoon, pageNumber: 1, pageSize: 20 });
    });

    it('says they could not be read and reads them again', async () => {
      const user = userEvent.setup();
      api.getMessages
        .mockRejectedValueOnce(refused('No se pudieron leer los mensajes.'))
        .mockResolvedValue(page([]));
      renderPage('/campaigns?tab=creditReminders');

      expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron leer los mensajes.');
      await user.click(screen.getByRole('button', { name: 'Reintentar' }));
      expect(await screen.findByText('Todavía no se ha enviado ningún recordatorio del fiado.')).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
  });

  describe('Bajas', () => {
    const ANA: MessagingOptOutDto = {
      id: 'o1', phone: '9991112233', source: OptOutSource.CustomerKeyword, optedOutAt: '2026-10-01T15:00:00Z', isActive: true,
    } as MessagingOptOutDto;
    const LUIS: MessagingOptOutDto = { ...ANA, id: 'o2', phone: '9988776655', source: OptOutSource.Staff };
    const lastSearch = () => api.getOptOuts.mock.calls.at(-1)?.[0];

    it('lists who asked not to receive messages and finds them by phone', async () => {
      const user = userEvent.setup();
      api.getOptOuts.mockImplementation(((params: { search?: string }) => Promise.resolve(page(
        params.search === '999' ? [ANA] : params.search ? [] : [ANA, LUIS],
      ))) as never);
      renderPage('/campaigns?tab=optOuts');

      const ana = (await screen.findByText('9991112233')).closest('tr') as HTMLElement;
      expect(within(ana).getByText('Escribió BAJA')).toBeInTheDocument();
      expect(within(screen.getByText('9988776655').closest('tr') as HTMLElement).getByText('Registrada por el negocio')).toBeInTheDocument();
      expect(lastSearch()).toEqual({ search: undefined, pageNumber: 1, pageSize: 20 });

      await user.type(screen.getByLabelText('Buscar'), ' 999 ');
      await waitFor(() => expect(screen.queryByText('9988776655')).not.toBeInTheDocument());
      expect(screen.getByText('9991112233')).toBeInTheDocument();
      expect(lastSearch()).toEqual({ search: '999', pageNumber: 1, pageSize: 20 });

      await user.clear(screen.getByLabelText('Buscar'));
      await user.type(screen.getByLabelText('Buscar'), '000');
      expect(await screen.findByText('Ningún teléfono coincide con la búsqueda.')).toBeInTheDocument();
      expect(screen.queryByText('Nadie ha pedido dejar de recibir mensajes.')).not.toBeInTheDocument();
    });

    it('shows only the answer to what was typed last', async () => {
      const user = userEvent.setup();
      const slow = deferred();
      api.getOptOuts.mockImplementation(((params: { search?: string }) => (
        params.search === '9' ? slow.promise : Promise.resolve(page(params.search === '98' ? [LUIS] : [ANA, LUIS]))
      )) as never);
      renderPage('/campaigns?tab=optOuts');
      await screen.findByText('9991112233');

      await user.type(screen.getByLabelText('Buscar'), '9');
      await pause();
      await user.type(screen.getByLabelText('Buscar'), '8');
      await waitFor(() => expect(screen.queryByText('9991112233')).not.toBeInTheDocument());

      // The answer to «9» arrives after the one to «98»: what is shown stays the one to «98».
      await act(async () => slow.resolve(page([ANA])));
      expect(screen.queryByText('9991112233')).not.toBeInTheDocument();
      expect(screen.getByText('9988776655')).toBeInTheDocument();
    });

    it('says they could not be read instead of loading forever, and reads them again', async () => {
      const user = userEvent.setup();
      const retry = deferred();
      api.getOptOuts.mockRejectedValueOnce(refused('No se pudieron leer las bajas del negocio.')).mockReturnValueOnce(retry.promise as never);
      renderPage('/campaigns?tab=optOuts');

      expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron leer las bajas del negocio.');
      expect(screen.queryByText('Cargando…')).not.toBeInTheDocument();
      expect(screen.queryByText('Nadie ha pedido dejar de recibir mensajes.')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Reintentar' }));
      expect(screen.getByRole('status')).toHaveTextContent('Cargando…');
      await act(async () => retry.resolve(page([])));
      expect(await screen.findByText('Nadie ha pedido dejar de recibir mensajes.')).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('drops what was shown when a new search cannot be read', async () => {
      const user = userEvent.setup();
      api.getOptOuts.mockImplementation(((params: { search?: string }) => (
        params.search ? Promise.reject(refused('Se perdió la conexión.')) : Promise.resolve(page([ANA, LUIS]))
      )) as never);
      renderPage('/campaigns?tab=optOuts');
      await screen.findByText('9991112233');

      await user.type(screen.getByLabelText('Buscar'), '55');
      expect(await screen.findByRole('alert')).toHaveTextContent('Se perdió la conexión.');
      expect(screen.queryByText('9991112233')).not.toBeInTheDocument();

      // A search that is read takes the notice away.
      await user.clear(screen.getByLabelText('Buscar'));
      expect(await screen.findByText('9991112233')).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('records a baja with its button and not with Enter in the search', async () => {
      const user = userEvent.setup();
      api.getOptOuts.mockResolvedValue(page([ANA]));
      api.setOptOut.mockRejectedValueOnce(refused('El teléfono no es válido.')).mockResolvedValue({ data: undefined } as never);
      renderPage('/campaigns?tab=optOuts');
      await screen.findByText('9991112233');

      await user.type(screen.getByLabelText('Teléfono'), '55 1234 5678');
      await user.type(screen.getByLabelText('Buscar'), '55{Enter}');
      expect(api.setOptOut).not.toHaveBeenCalled();
      await user.clear(screen.getByLabelText('Buscar'));

      await user.click(screen.getByRole('button', { name: 'Registrar baja' }));
      const refusal = await screen.findByRole('alert');
      expect(refusal).toHaveTextContent('El teléfono no es válido.');
      // What the server refused is about the phone typed: there is nothing to read again.
      expect(within(refusal).queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument();
      expect(screen.getByLabelText('Teléfono')).toHaveValue('55 1234 5678');

      const reads = api.getOptOuts.mock.calls.length;
      await user.click(screen.getByRole('button', { name: 'Registrar baja' }));
      await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Baja registrada: ya no recibirá campañas ni recordatorios.'));
      expect(api.setOptOut).toHaveBeenLastCalledWith('55 1234 5678', true);
      expect(screen.getByLabelText('Teléfono')).toHaveValue('');
      expect(screen.getByRole('button', { name: 'Registrar baja' })).toBeDisabled();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(api.getOptOuts.mock.calls.length).toBeGreaterThan(reads);
    });

    it('reactivates a customer only after asking, and goes to the page before when the last one leaves its page', async () => {
      const user = userEvent.setup();
      let luisOut = true;
      api.getOptOuts.mockImplementation(((params: { pageNumber: number }) => Promise.resolve(
        params.pageNumber === 2
          ? page(luisOut ? [LUIS] : [], 2, luisOut ? 2 : 1)
          : page([ANA], 1, luisOut ? 2 : 1),
      )) as never);
      api.setOptOut.mockImplementation((async () => {
        luisOut = false;
        return { data: undefined };
      }) as never);
      confirm.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
      renderPage('/campaigns?tab=optOuts');

      await screen.findByText('9991112233');
      expect(screen.getByText('1 / 2')).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Siguiente' }));
      const luis = (await screen.findByText('9988776655')).closest('tr') as HTMLElement;

      await user.click(within(luis).getByRole('button', { name: 'Reactivar' }));
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ confirmLabel: 'Reactivar', message: expect.stringContaining('9988776655') }));
      expect(api.setOptOut).not.toHaveBeenCalled();

      await user.click(within(luis).getByRole('button', { name: 'Reactivar' }));
      await waitFor(() => expect(toast.success).toHaveBeenCalledWith('El cliente volverá a recibir mensajes.'));
      expect(api.setOptOut).toHaveBeenCalledWith('9988776655', false);
      expect(await screen.findByText('9991112233')).toBeInTheDocument();
      expect(lastSearch()).toEqual({ search: undefined, pageNumber: 1, pageSize: 20 });
      expect(screen.queryByText('Nadie ha pedido dejar de recibir mensajes.')).not.toBeInTheDocument();
    });
  });
});
