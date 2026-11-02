import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, onTestFinished, vi } from 'vitest';
import { productsApi } from '../../api/productsApi';
import { quotesApi, type Quote } from '../../api/quotesApi';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OutgoingMessageStatus, QuoteEmailFailure, QuoteReplyChannel, QuoteStatus, TenantCapability } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import QuoteDetail from './QuoteDetail';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));
vi.mock('../../api/productsApi', () => ({ productsApi: { getAll: vi.fn() } }));
vi.mock('../../api/quotesApi', () => ({
  quotesApi: {
    get: vi.fn(), updateDraft: vi.fn(), send: vi.fn(), revise: vi.fn(), discard: vi.fn(), reply: vi.fn(),
    sendByEmail: vi.fn(), sendByWhatsApp: vi.fn(), convertToOrder: vi.fn(),
  },
}));

const TOKEN = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';

function quote(overrides: Partial<Quote> = {}): Quote {
  return {
    id: 'q1',
    number: 'COT-000007',
    revision: 1,
    status: QuoteStatus.Draft,
    customerId: null,
    customerName: 'Valeria Ruiz',
    customerPhone: '5522107765',
    customerEmail: 'valeria@correo.mx',
    pricesIncludeTax: true,
    validityDays: 15,
    validUntil: null,
    note: null,
    lines: [{ position: 1, productId: null, description: 'Cambio de calentador', quantity: 1, unitPrice: 1160, taxRate: 16, isTaxExempt: false, total: 1160 }],
    subTotal: 1000,
    tax: 160,
    taxes: [],
    total: 1160,
    toPay: 1160,
    versions: [{ id: 'q1', revision: 1, status: QuoteStatus.Draft, total: 1160 }],
    createdAt: '2026-10-04T17:00:00Z',
    ...overrides,
  } as Quote;
}

const sent = (overrides: Partial<Quote> = {}) => quote({
  status: QuoteStatus.Sent, validUntil: '2026-10-19', sentAt: '2026-10-04T17:00:00Z', trackingToken: TOKEN, ...overrides,
});

const toast = { success: vi.fn(), error: vi.fn() };

function refusal(detail: string) {
  const headers = new AxiosHeaders();
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
    status: 400, data: { detail }, statusText: '', headers, config: { headers },
  } as AxiosResponse);
}

/** A promise the test settles when it wants. */
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => { resolve = done; });
  return { promise, resolve };
}

/** Who is signed in: an owner by default, or staff who take orders (and charge them, when said). */
function signIn(role: string, permissions: string[] = ['ManageOrders', 'ChargeOrders']) {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role, permissions } } as unknown as ReturnType<typeof useAuth>);
}

function renderAt(id = 'q1') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/quotes/${id}`]}>
        <Routes>
          <Route path="/quotes/:id" element={<QuoteDetail />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('QuoteDetail', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useTenant).mockReturnValue({
      tenant: { name: 'Plomería Ruiz' }, capabilities: TenantCapability.Service | TenantCapability.Appointments,
    } as unknown as ReturnType<typeof useTenant>);
    signIn('TENANT_ADMIN');
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useConfirm).mockReturnValue({ confirm: vi.fn().mockResolvedValue(true) } as unknown as ReturnType<typeof useConfirm>);
  });

  it('saves the lines of a draft and shows the total the server worked out', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(quote());
    vi.mocked(quotesApi.updateDraft).mockResolvedValue(quote({
      lines: [
        { position: 1, productId: null, description: 'Cambio de calentador', quantity: 1, unitPrice: 1160, taxRate: 16, isTaxExempt: false, total: 1160 },
        { position: 2, productId: null, description: 'Mano de obra', quantity: 2, unitPrice: 232, taxRate: 16, isTaxExempt: false, total: 464 },
      ],
      subTotal: 1400, tax: 224, total: 1624, toPay: 1624,
    }));
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Agregar renglón' }));
    await userEvent.type(screen.getByLabelText('Qué incluye', { selector: '#quote-line-1-description' }), 'Mano de obra');
    await userEvent.clear(screen.getByLabelText('Cantidad', { selector: '#quote-line-1-quantity' }));
    await userEvent.type(screen.getByLabelText('Cantidad', { selector: '#quote-line-1-quantity' }), '2');
    await userEvent.clear(screen.getByLabelText('Precio', { selector: '#quote-line-1-price' }));
    await userEvent.type(screen.getByLabelText('Precio', { selector: '#quote-line-1-price' }), '232');
    // Until it is saved, the page does not total the lines itself.
    expect(screen.getByText(/Guarda los cambios para ver el total/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Guardar borrador' }));

    await waitFor(() => expect(quotesApi.updateDraft).toHaveBeenCalledWith('q1', expect.objectContaining({
      customerName: 'Valeria Ruiz',
      validityDays: 15,
      lines: [
        { productId: null, description: 'Cambio de calentador', quantity: 1, unitPrice: 1160 },
        { productId: null, description: 'Mano de obra', quantity: 2, unitPrice: 232 },
      ],
    })));
    expect(await screen.findByText('$1,624.00')).toBeInTheDocument();
    expect(JSON.stringify(vi.mocked(quotesApi.updateDraft).mock.calls[0][1])).not.toContain('total');
  });

  it('adds a line from the catalog with its price', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(quote({ lines: [] }));
    vi.mocked(productsApi.getAll).mockResolvedValue({ data: { items: [{ id: 'p1', name: 'Visita y diagnóstico', priceAmount: 350 }] } } as never);
    renderAt();

    await userEvent.type(await screen.findByLabelText('Del catálogo'), 'Visita');
    await userEvent.click(await screen.findByRole('button', { name: 'Agregar Visita y diagnóstico' }));

    expect(screen.getByLabelText('Qué incluye', { selector: '#quote-line-0-description' })).toHaveValue('Visita y diagnóstico');
    expect(screen.getByLabelText('Precio', { selector: '#quote-line-0-price' })).toHaveValue(350);
  });

  it('saves what changed before sending it, and then offers its link', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(quote());
    vi.mocked(quotesApi.updateDraft).mockResolvedValue(quote({ note: 'Incluye material' }));
    vi.mocked(quotesApi.send).mockResolvedValue(sent());
    renderAt();

    await userEvent.type(await screen.findByLabelText('Nota para el cliente'), 'Incluye material');
    await userEvent.click(screen.getByRole('button', { name: 'Terminar y obtener enlace' }));

    await waitFor(() => expect(quotesApi.send).toHaveBeenCalledWith('q1'));
    expect(vi.mocked(quotesApi.updateDraft).mock.invocationCallOrder[0]).toBeLessThan(vi.mocked(quotesApi.send).mock.invocationCallOrder[0]);
    const whatsApp = await screen.findByRole('link', { name: 'Enviar por WhatsApp' });
    expect(whatsApp.getAttribute('href')).toContain('https://wa.me/525522107765?text=');
    expect(decodeURIComponent(whatsApp.getAttribute('href')!)).toContain(`/cotizacion#${TOKEN}`);
    // The email goes from TienditApp, in the name of the business.
    expect(screen.getByRole('button', { name: 'Enviar por correo' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Guardar borrador' })).not.toBeInTheDocument();
  });

  it('records what the customer answered and how', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent());
    vi.mocked(quotesApi.reply).mockResolvedValue(sent({
      status: QuoteStatus.Accepted, replyChannel: QuoteReplyChannel.Phone, customerResponse: 'Sí, el jueves', closedAt: '2026-10-05T17:00:00Z',
    }));
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Registrar respuesta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Respuesta del cliente' });
    await userEvent.selectOptions(within(dialog).getByLabelText('Por dónde'), 'Teléfono');
    await userEvent.type(within(dialog).getByLabelText('Lo que dijo'), 'Sí, el jueves');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar respuesta' }));

    await waitFor(() => expect(quotesApi.reply).toHaveBeenCalledWith('q1', { accepted: true, channel: QuoteReplyChannel.Phone, response: 'Sí, el jueves' }));
    expect(await screen.findByText(/Aceptó por teléfono/)).toBeInTheDocument();
    expect(screen.getByText('«Sí, el jueves»')).toBeInTheDocument();
  });

  it('corrects an expired quote as its one main action', async () => {
    const correction = quote({ id: 'q2', revision: 2 });
    vi.mocked(quotesApi.get).mockImplementation(async (id: string) => (id === 'q2' ? correction : sent({ status: QuoteStatus.Expired, trackingToken: TOKEN })));
    vi.mocked(quotesApi.revise).mockResolvedValue(correction);
    renderAt();

    const correct = await screen.findByRole('button', { name: 'Corregir' });
    expect(correct).toHaveClass('btn-primary');
    expect(screen.queryByRole('button', { name: 'Registrar respuesta' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Enviar por WhatsApp' })).not.toBeInTheDocument();
    await userEvent.click(correct);

    await waitFor(() => expect(quotesApi.revise).toHaveBeenCalledWith('q1'));
    // The correction opens on its own, fresh.
    expect(await screen.findByRole('heading', { name: 'Cotización COT-000007 · versión 2' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Terminar y obtener enlace' })).toBeInTheDocument();
  });

  it('leads to its correction in progress instead of starting another', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent({ draftRevisionId: 'q2' }));
    renderAt();

    expect(await screen.findByRole('link', { name: 'Abrir la corrección' })).toHaveAttribute('href', '/quotes/q2');
    expect(screen.queryByRole('button', { name: 'Corregir' })).not.toBeInTheDocument();
  });

  it('says the visit stopped coming off once its sale was cancelled', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent({ visitCredit: { amount: 440.8, applies: false }, toPay: 1160 }));
    renderAt();

    expect(await screen.findByText('Ya no aplica')).toBeInTheDocument();
    expect(screen.getByText('La venta de la visita se canceló o se reembolsó: ya no se descuenta.')).toBeInTheDocument();
  });

  it('shows what the server said when it refuses to finish it', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(quote({ lines: [] }));
    vi.mocked(quotesApi.send).mockRejectedValue(refusal('Agrega al menos un renglón con precio antes de enviarla.'));
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Terminar y obtener enlace' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Agrega al menos un renglón con precio antes de enviarla.');
  });

  it('finishes it once however many times it is clicked, and waits with every action', async () => {
    const answer = deferred<Quote>();
    vi.mocked(quotesApi.get).mockResolvedValue(quote());
    vi.mocked(quotesApi.send).mockReturnValue(answer.promise);
    renderAt();

    const finish = await screen.findByRole('button', { name: 'Terminar y obtener enlace' });
    await userEvent.click(finish);
    await userEvent.click(screen.getByRole('button', { name: 'Terminando…' }));

    expect(quotesApi.send).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Descartar borrador' })).toBeDisabled();
    expect(screen.getByLabelText('Nota para el cliente')).toBeDisabled();
    answer.resolve(sent());
    expect(await screen.findByRole('button', { name: 'Registrar respuesta' })).toBeEnabled();
  });

  it('keeps the fields still while saving, so nothing typed is lost', async () => {
    const saved = deferred<Quote>();
    vi.mocked(quotesApi.get).mockResolvedValue(quote());
    vi.mocked(quotesApi.updateDraft).mockReturnValue(saved.promise);
    renderAt();

    await userEvent.type(await screen.findByLabelText('Nota para el cliente'), 'Incluye material');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar borrador' }));

    expect(screen.getByLabelText('Nota para el cliente')).toBeDisabled();
    saved.resolve(quote({ note: 'Incluye material' }));
    await waitFor(() => expect(screen.getByLabelText('Nota para el cliente')).toBeEnabled());
    expect(screen.getByLabelText('Nota para el cliente')).toHaveValue('Incluye material');
  });

  it('keeps the answer open with what the server said when it is refused', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent());
    vi.mocked(quotesApi.reply).mockRejectedValue(refusal('La cotización ya venció: corrígela para mandarle una versión nueva con otra vigencia.'));
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Registrar respuesta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Respuesta del cliente' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar respuesta' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('La cotización ya venció');
    expect(screen.getByRole('dialog', { name: 'Respuesta del cliente' })).toBeInTheDocument();
  });

  it('says how a rejection came, in words', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent({
      status: QuoteStatus.Rejected, replyChannel: QuoteReplyChannel.InPerson, customerResponse: null, closedAt: '2026-10-05T17:00:00Z',
    }));
    renderAt();

    expect(await screen.findByText(/Rechazó en persona/)).toBeInTheDocument();
    expect(screen.getByText(/Valía hasta el 19 de octubre de 2026/)).toBeInTheDocument();
  });

  it('answers a sent quote as its one main action, and offers the link to copy by hand where the browser does not copy', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent());
    Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
    renderAt();

    expect(await screen.findByRole('button', { name: 'Registrar respuesta' })).toHaveClass('btn-primary');
    expect(screen.getByRole('button', { name: 'Corregir' })).toHaveClass('btn-light');
    await userEvent.click(screen.getByRole('button', { name: 'Copiar enlace' }));

    expect(await screen.findByText(new RegExp(`Cópialo de aquí: .*/cotizacion#${TOKEN}`))).toBeInTheDocument();
  });

  it('does not finish a correction once the customer accepted the version they had', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(quote({
      id: 'q2', revision: 2,
      versions: [
        { id: 'q1', revision: 1, status: QuoteStatus.Accepted, total: 1160 },
        { id: 'q2', revision: 2, status: QuoteStatus.Draft, total: 1160 },
      ],
    } as Partial<Quote>));
    renderAt('q2');

    expect(await screen.findByText(/El cliente ya aceptó la versión 1/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Terminar y obtener enlace' })).toBeDisabled();
  });

  it('sends it by email from TienditApp to the address it starts with, tells it is on its way and shows it in the history', async () => {
    const email = { id: 'e1', to: 'valeria@correo.mx', requestedAt: '2026-10-04T17:00:00Z', failedAttempts: 0 };
    vi.mocked(quotesApi.get).mockResolvedValueOnce(sent()).mockResolvedValue(sent({ emails: [email] }));
    vi.mocked(quotesApi.sendByEmail).mockResolvedValue(email);
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Enviar por correo' }));
    const dialog = await screen.findByRole('dialog', { name: 'Enviar por correo' });
    expect(within(dialog).getByLabelText('Correo del cliente')).toHaveValue('valeria@correo.mx');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar' }));

    await waitFor(() => expect(quotesApi.sendByEmail).toHaveBeenCalledWith('q1', 'valeria@correo.mx'));
    expect(toast.success).toHaveBeenCalledWith('La cotización COT-000007 va en camino a valeria@correo.mx.');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Enviar por correo' })).not.toBeInTheDocument());
    // The detail asks again, and the send appears on its way.
    expect(await screen.findByText('Enviándose por correo a valeria@correo.mx…')).toBeInTheDocument();
  });

  it('keeps the dialog still while it sends, and names the number as the server normalized it', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent({ whatsAppConnected: true }));
    const answer = deferred<Awaited<ReturnType<typeof quotesApi.sendByWhatsApp>>>();
    vi.mocked(quotesApi.sendByWhatsApp).mockReturnValue(answer.promise);
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Enviar por WhatsApp' }));
    const dialog = await screen.findByRole('dialog', { name: 'Enviar por WhatsApp' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar' }));

    expect(within(dialog).getByLabelText('WhatsApp del cliente')).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Enviando…' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    answer.resolve({ id: 'w1', phone: '+525522107765', requestedAt: '2026-10-04T17:00:00Z', status: OutgoingMessageStatus.Pending, attempts: 0 });
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('La cotización COT-000007 va en camino a +525522107765.'));
  });

  it('keeps showing the quote when a refresh fails, and says so', async () => {
    vi.mocked(quotesApi.get).mockResolvedValueOnce(sent()).mockRejectedValue(new Error('Network Error'));
    vi.mocked(quotesApi.sendByEmail).mockResolvedValue({ id: 'e1', to: 'valeria@correo.mx', requestedAt: '2026-10-04T17:00:00Z', failedAttempts: 0 });
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Enviar por correo' }));
    await userEvent.click(within(await screen.findByRole('dialog', { name: 'Enviar por correo' })).getByRole('button', { name: 'Enviar' }));

    expect(await screen.findByText('No se pudo actualizar la cotización: TienditApp lo vuelve a intentar.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Cotización COT-000007' })).toBeInTheDocument();
    expect(screen.queryByText('No encontramos esa cotización.')).not.toBeInTheDocument();
  });

  it('sends it by WhatsApp from the business once it connected its own, and keeps the refusal in the dialog', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent({ whatsAppConnected: true }));
    vi.mocked(quotesApi.sendByWhatsApp).mockRejectedValueOnce(refusal('Esta versión ya se mandó 5 veces por ese medio en las últimas 24 horas.'));
    renderAt();

    // Not a link to the phone's WhatsApp anymore.
    await userEvent.click(await screen.findByRole('button', { name: 'Enviar por WhatsApp' }));
    expect(screen.queryByRole('link', { name: 'Enviar por WhatsApp' })).not.toBeInTheDocument();
    const dialog = await screen.findByRole('dialog', { name: 'Enviar por WhatsApp' });
    const phone = within(dialog).getByLabelText('WhatsApp del cliente');
    await userEvent.clear(phone);
    await userEvent.type(phone, '5511112222');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Esta versión ya se mandó 5 veces por ese medio en las últimas 24 horas.');
    expect(quotesApi.sendByWhatsApp).toHaveBeenCalledWith('q1', '5511112222');
  });

  it('lists the sends of the version and where each one is', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent({
      status: QuoteStatus.Accepted,
      emails: [
        { id: 'e2', to: 'valeria@correo.mx', requestedAt: '2026-10-04T18:00:00Z', failedAttempts: 2 },
        { id: 'e1', to: 'vale@correo', requestedAt: '2026-10-04T17:05:00Z', failedAt: '2026-10-04T17:06:00Z', failure: QuoteEmailFailure.Rejected, failedAttempts: 0 },
      ],
      whatsApps: [
        { id: 'w1', phone: '+525522107765', requestedAt: '2026-10-04T17:30:00Z', status: OutgoingMessageStatus.Sent, sentAt: '2026-10-04T17:30:05Z', readAt: '2026-10-04T17:40:00Z', attempts: 0 },
      ],
    }));
    renderAt();

    const sends = await screen.findByRole('heading', { name: 'Envíos de esta versión' });
    const items = within(sends.parentElement!).getAllByRole('listitem').map((item) => item.textContent);
    expect(items[0]).toBe('Enviándose por correo a valeria@correo.mx… El servidor de correo no lo ha tomado; TienditApp lo vuelve a intentar.');
    expect(items[1]).toMatch(/^Lo leyó en WhatsApp \(\+525522107765\) el /);
    expect(items[2]).toBe('No se envió por correo a vale@correo: el servidor de correo rechazó la dirección.');
  });

  it('turns an accepted quote into its service order, with what was paid for the visit as its payment', async () => {
    const accepted = sent({
      status: QuoteStatus.Accepted, replyChannel: QuoteReplyChannel.WhatsApp, closedAt: '2026-10-05T17:00:00Z', canConvertToOrder: true,
      visitCredit: { amount: 440.8, applies: true }, toPay: 719.2,
    });
    vi.mocked(quotesApi.get).mockResolvedValue(accepted);
    vi.mocked(quotesApi.convertToOrder).mockResolvedValue({
      ...accepted, canConvertToOrder: false, convertedAt: '2026-10-05T18:00:00Z',
      order: { id: 'o1', orderNumber: 'ORD-000031', status: 4 },
    } as Quote);
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Convertir en orden de servicio' }));
    const dialog = await screen.findByRole('dialog', { name: 'Convertir en orden de servicio' });
    expect(dialog).toHaveTextContent('La orden lleva su renglón a los precios que el cliente aceptó: $1,160.00.');
    expect(dialog).toHaveTextContent('Lo que pagó por la visita ($440.80) queda como pago de la orden: falta cobrar $719.20.');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Convertir' }));

    await waitFor(() => expect(quotesApi.convertToOrder).toHaveBeenCalledWith('q1', null));
    expect(toast.success).toHaveBeenCalledWith('Se creó la orden de servicio ORD-000031.');
    const orderLink = await screen.findByRole('link', { name: 'Ver la orden' });
    expect(orderLink).toHaveAttribute('href', '/orders?search=ORD-000031');
    // The link takes the focus of the button it replaced.
    await waitFor(() => expect(orderLink).toHaveFocus());
    expect(screen.getByText('Lo que pagó por la visita quedó como pago de la orden.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Convertir en orden de servicio' })).not.toBeInTheDocument();
  });

  it('sends when the job is promised in the business\'s time, and after a refusal shows the quote as it is now', async () => {
    setBusinessTimeZone('America/Tijuana');
    onTestFinished(() => setBusinessTimeZone(null));
    const accepted = sent({ status: QuoteStatus.Accepted, canConvertToOrder: true });
    vi.mocked(quotesApi.get).mockResolvedValueOnce(accepted).mockResolvedValue({
      ...accepted, canConvertToOrder: false, order: { id: 'o1', orderNumber: 'ORD-000031', status: 4 },
    } as Quote);
    vi.mocked(quotesApi.convertToOrder).mockRejectedValueOnce(refusal('Esta cotización ya se convirtió en una orden de servicio.'));
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Convertir en orden de servicio' }));
    const dialog = await screen.findByRole('dialog', { name: 'Convertir en orden de servicio' });
    const promised = within(dialog).getByLabelText('Para cuándo es el trabajo');
    expect(promised).toHaveAccessibleDescription('Opcional: la orden sale con esa fecha en el tablero de servicios.');
    await userEvent.type(promised, '2026-10-08T10:30');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Convertir' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Esta cotización ya se convirtió en una orden de servicio.');
    // 10:30 in Tijuana (UTC−7 in October), whatever the device's zone.
    expect(quotesApi.convertToOrder).toHaveBeenCalledWith('q1', '2026-10-08T17:30:00.000Z');
    // Someone else converted it: behind the dialog, the page already says so.
    expect(await screen.findByRole('link', { name: 'Ver la orden', hidden: true })).toBeInTheDocument();
  });

  it('holds still while it converts', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent({ status: QuoteStatus.Accepted, canConvertToOrder: true }));
    const pending = deferred<Quote>();
    vi.mocked(quotesApi.convertToOrder).mockReturnValue(pending.promise);
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Convertir en orden de servicio' }));
    const dialog = await screen.findByRole('dialog', { name: 'Convertir en orden de servicio' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Convertir' }));

    expect(await within(dialog).findByRole('button', { name: 'Convirtiendo…' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(within(dialog).getByLabelText('Para cuándo es el trabajo')).toBeDisabled();
    await userEvent.keyboard('{Escape}');
    expect(screen.getByRole('dialog', { name: 'Convertir en orden de servicio' })).toBeInTheDocument();
    pending.resolve(sent({ status: QuoteStatus.Accepted, order: { id: 'o1', orderNumber: 'ORD-000031', status: 4 } }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('tells who cannot charge that applying the visit needs who can', async () => {
    signIn('STAFF', ['ManageOrders']);
    vi.mocked(quotesApi.get).mockResolvedValue(sent({
      status: QuoteStatus.Accepted, canConvertToOrder: true, visitCredit: { amount: 440.8, applies: true }, toPay: 719.2,
    }));
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Convertir en orden de servicio' }));
    const dialog = await screen.findByRole('dialog', { name: 'Convertir en orden de servicio' });

    expect(within(dialog).getByRole('status')).toHaveTextContent('Aplicar lo que pagó por la visita es cobrar');
    expect(within(dialog).getByRole('button', { name: 'Convertir' })).toBeDisabled();
  });

  it('says the visit covers at most the total of the job', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent({
      status: QuoteStatus.Accepted, canConvertToOrder: true, total: 300, visitCredit: { amount: 464, applies: true }, toPay: 0,
    }));
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: 'Convertir en orden de servicio' }));

    expect(await screen.findByRole('dialog', { name: 'Convertir en orden de servicio' }))
      .toHaveTextContent('Lo que pagó por la visita ($300.00) queda como pago de la orden: falta cobrar $0.00.');
  });

  it('says how to turn it into an order when the business has no service orders', async () => {
    vi.mocked(quotesApi.get).mockResolvedValue(sent({ status: QuoteStatus.Accepted, canConvertToOrder: false }));
    renderAt();

    expect(await screen.findByText(/activa el módulo «Servicios» en/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Configuración › Negocio' })).toHaveAttribute('href', '/settings/business');
    expect(screen.queryByRole('button', { name: 'Convertir en orden de servicio' })).not.toBeInTheDocument();
  });

  it('tells the staff to ask the owner for service orders', async () => {
    signIn('STAFF', ['ManageOrders']);
    vi.mocked(quotesApi.get).mockResolvedValue(sent({ status: QuoteStatus.Accepted, canConvertToOrder: false }));
    renderAt();

    expect(await screen.findByText(/pídele al dueño que active el módulo «Servicios»/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Configuración › Negocio' })).not.toBeInTheDocument();
  });
});
