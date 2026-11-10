import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import NoticeEmailsCard from './NoticeEmailsCard';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const REQUESTS = '/api/v1/appointments/request-emails';
const QUOTES = '/api/v1/quotes/reply-emails';
const ORDERS = '/api/v1/orders/cancellation-emails';

function apiError(status: number, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code: 'business_rule', detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

const renderCard = (props: { agenda?: boolean; quotes?: boolean; orders?: boolean; ownsBusiness?: boolean } = {}) =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <NoticeEmailsCard
        businessName="Estética Luna"
        agenda={props.agenda ?? true}
        quotes={props.quotes ?? true}
        orders={props.orders}
        ownsBusiness={props.ownsBusiness}
      />
    </QueryClientProvider>,
  );

/** What each notice answers, saved as the person turns it on or off. */
function serve(initial: Record<string, { enabled: boolean; emailConfirmed?: boolean }>) {
  const state = { ...initial };
  vi.mocked(apiRequest).mockImplementation(async (method, path, options) => {
    const key = path as string;
    if (!state[key]) throw apiError(403, 'Sin permiso.');
    if (method === 'put') state[key] = { ...state[key], enabled: (options as unknown as { body: { enabled: boolean } }).body.enabled };
    return { email: 'ana@demo.com', emailConfirmed: true, ...state[key] } as never;
  });
}

describe('NoticeEmailsCard (Fases 29N and 29O)', () => {
  beforeEach(() => {
    vi.mocked(apiRequest).mockReset();
  });

  it('says which notices reach the person by email, and where, and lets them turn each one off on its own', async () => {
    const user = userEvent.setup();
    serve({ [REQUESTS]: { enabled: true }, [QUOTES]: { enabled: true } });
    renderCard();

    const agenda = await screen.findByRole('switch', { name: 'Citas en línea' });
    const quotes = await screen.findByRole('switch', { name: 'Cotizaciones' });
    await waitFor(() => expect(quotes).toBeChecked());
    expect(agenda).toBeChecked();
    expect(agenda).toHaveAccessibleDescription('Cada solicitud de cita o de visita de Estética Luna, su recordatorio si sigue sin respuesta y cada cita que un cliente cancela.');
    expect(quotes).toHaveAccessibleDescription('Cada vez que un cliente acepta o rechaza desde su enlace una cotización de Estética Luna.');
    expect(screen.getByText('ana@demo.com')).toBeInTheDocument();
    expect(screen.queryByText(/no está confirmado/)).not.toBeInTheDocument();

    await user.click(quotes);

    await waitFor(() => expect(quotes).not.toBeChecked());
    expect(agenda).toBeChecked();
    expect(apiRequest).toHaveBeenCalledWith('put', QUOTES, { body: { enabled: false } });
    expect(apiRequest).not.toHaveBeenCalledWith('put', REQUESTS, expect.anything());
  });

  it('lets whoever confirms the orders choose the online orders their customers cancel (Fase 30B)', async () => {
    const user = userEvent.setup();
    serve({ [ORDERS]: { enabled: false } });
    renderCard({ agenda: false, quotes: false, orders: true });

    const orders = await screen.findByRole('switch', { name: 'Pedidos cancelados' });
    await waitFor(() => expect(orders).toBeEnabled());
    expect(orders).not.toBeChecked();
    expect(orders).toHaveAccessibleDescription('Cada pedido en línea de Estética Luna que su cliente cancela antes de que lo aceptes, o que alguien más del equipo cancela porque el cliente lo pidió.');
    expect(screen.queryByRole('switch', { name: 'Citas en línea' })).not.toBeInTheDocument();

    await user.click(orders);

    await waitFor(() => expect(orders).toBeChecked());
    expect(apiRequest).toHaveBeenCalledWith('put', ORDERS, { body: { enabled: true } });
    expect(apiRequest).not.toHaveBeenCalledWith('get', REQUESTS, expect.anything());
  });

  it('shows only the notices the person may choose', async () => {
    serve({ [QUOTES]: { enabled: false } });
    renderCard({ agenda: false });

    expect(await screen.findByRole('switch', { name: 'Cotizaciones' })).not.toBeChecked();
    expect(screen.queryByRole('switch', { name: 'Citas en línea' })).not.toBeInTheDocument();
    expect(apiRequest).not.toHaveBeenCalledWith('get', REQUESTS, expect.anything());
  });

  it('warns that nothing arrives while the email is not confirmed, and says why a change was not saved', async () => {
    const user = userEvent.setup();
    vi.mocked(apiRequest).mockImplementation(async (method) => {
      if (method === 'get') return { enabled: false, email: 'luis@demo.com', emailConfirmed: false } as never;
      throw apiError(400, 'No se pudo.');
    });
    renderCard({ quotes: false });

    const toggle = await screen.findByRole('switch', { name: 'Citas en línea' });
    await waitFor(() => expect(toggle).toBeEnabled());
    expect(toggle).not.toBeChecked();
    expect(screen.getByText('Tu correo todavía no está confirmado: no te llegan hasta que lo confirmes.')).toBeInTheDocument();

    await user.click(toggle);

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo.');
    expect(toggle).not.toBeChecked();
  });

  it('hides a notice the server refuses, and shows nothing to whoever is not on the team', async () => {
    serve({ [REQUESTS]: { enabled: true } });
    const { unmount } = renderCard();

    const card = await screen.findByRole('heading', { name: 'Avisos por correo' });
    await waitFor(() => expect(within(card.parentElement!).queryByRole('switch', { name: 'Cotizaciones' })).not.toBeInTheDocument());
    expect(screen.getByRole('switch', { name: 'Citas en línea' })).toBeInTheDocument();
    unmount();

    vi.mocked(apiRequest).mockRejectedValue(apiError(404, 'Estos avisos son para el equipo del negocio.'));
    const second = renderCard();
    await waitFor(() => expect(second.container).toBeEmptyDOMElement());
  });

  it('keeps a notice that failed to load for another reason, and lets the person try again', async () => {
    const user = userEvent.setup();
    let fail = true;
    vi.mocked(apiRequest).mockImplementation(async (_method, path) => {
      if (path === QUOTES && fail) throw apiError(500, 'Falló.');
      return { enabled: true, email: 'ana@demo.com', emailConfirmed: true } as never;
    });
    renderCard();

    const quotes = await screen.findByRole('switch', { name: 'Cotizaciones' });
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('No se pudo leer esta elección.');
    expect(quotes).toBeDisabled();
    expect(screen.getByRole('switch', { name: 'Citas en línea' })).toBeChecked();

    fail = false;
    await user.click(within(alert).getByRole('button', { name: 'Reintentar' }));

    await waitFor(() => expect(quotes).toBeChecked());
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('marks a notice still loading as busy', async () => {
    vi.mocked(apiRequest).mockImplementation(async (_method, path) =>
      path === QUOTES ? new Promise<never>(() => {}) : ({ enabled: true, email: 'ana@demo.com', emailConfirmed: true } as never));
    renderCard();

    const agenda = await screen.findByRole('switch', { name: 'Citas en línea' });
    await waitFor(() => expect(agenda).toBeChecked());
    expect(screen.getByRole('switch', { name: 'Cotizaciones' })).toHaveAttribute('aria-busy', 'true');
    expect(agenda).toHaveAttribute('aria-busy', 'false');
  });

  it('tells the team that whoever runs it may also choose them, but not the owner (Fase 29Q)', async () => {
    serve({ [REQUESTS]: { enabled: true } });
    const { unmount } = renderCard({ quotes: false });

    expect(await screen.findByText(/quien administra el equipo también los puede elegir por ti/)).toBeInTheDocument();
    unmount();

    renderCard({ quotes: false, ownsBusiness: true });
    await screen.findByRole('switch', { name: 'Citas en línea' });
    expect(screen.queryByText(/quien administra el equipo/)).not.toBeInTheDocument();
    expect(screen.getByText(/Solo cambian para ti\./)).toBeInTheDocument();
  });
});
