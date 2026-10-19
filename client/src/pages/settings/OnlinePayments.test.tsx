import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, onTestFinished, vi } from 'vitest';
import { IN_COURSE_REFRESH_MS } from '../../api/onlinePayments';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { goToExternalPage } from '../../utils/externalNavigation';
import OnlinePayments from './OnlinePayments';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../utils/externalNavigation', () => ({ goToExternalPage: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));
// The section tabs read the signed-in user; they are not what this test is about.
vi.mock('../../components/layout/SectionTabs', () => ({ default: () => null }));
const confirm = vi.fn();
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm }) }));

type Settings = Schemas['OnlinePaymentSettingsDto'];

const NOT_CONNECTED: Settings = {
  available: true, sandbox: false, connected: false, accountId: null, connectedAt: null, keyExpiresAt: null,
  needsReconnect: false, acceptsPayments: false, menuTakesOrders: true, checkoutsInCourse: 0, refundsInCourse: 0, refundsRefused: 0,
};
const CONNECTED: Settings = {
  ...NOT_CONNECTED, connected: true, accountId: 123456789, connectedAt: '2026-10-19T17:00:00Z', keyExpiresAt: '2027-04-17T17:00:00Z',
};

function serve(settings: Settings, change: (method: string, path: string, body: unknown) => Promise<unknown> = () => Promise.resolve(settings)) {
  vi.mocked(apiRequest).mockImplementation(((method: string, path: string, options?: { body?: unknown }) => {
    if (method === 'get' && path === '/api/v1/online-payments') return Promise.resolve(settings);
    return change(method, path, options?.body);
  }) as unknown as typeof apiRequest);
}

function writes() {
  return vi.mocked(apiRequest).mock.calls.filter(([method]) => method !== 'get');
}

function apiError(status: number, code: string, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

/** The address the page is at, to see that it forgets what Mercado Pago sent. */
function Address() {
  const location = useLocation();
  return <div hidden data-testid="address">{`${location.pathname}${location.search}`}</div>;
}

function renderPage(url = '/settings/online-payments') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  // Strict mode runs each effect twice: what Mercado Pago sent back is still used once.
  return render(
    <StrictMode>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[url]}>
          <Address />
          <OnlinePayments />
        </MemoryRouter>
      </QueryClientProvider>
    </StrictMode>,
  );
}

describe('OnlinePayments', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    confirm.mockResolvedValue(true);
  });

  it('says paying online is not offered while the platform has not set it up', async () => {
    serve({ ...NOT_CONNECTED, available: false });

    renderPage();

    expect(await screen.findByRole('heading', { name: 'El pago en línea no está disponible por ahora' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Conectar Mercado Pago' })).not.toBeInTheDocument();
  });

  it('sends the owner to authorize TienditApp in Mercado Pago', async () => {
    serve(NOT_CONNECTED, (method, path) => (method === 'post' && path === '/api/v1/online-payments/connect'
      ? Promise.resolve({ authorizationUrl: 'https://auth.mercadopago.com.mx/authorization?state=abc' })
      : Promise.reject(new Error(`Sin respuesta para ${method} ${path}`))));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Conectar Mercado Pago' }));

    await waitFor(() => expect(goToExternalPage).toHaveBeenCalledWith('https://auth.mercadopago.com.mx/authorization?state=abc'));
  });

  it('finishes connecting with what Mercado Pago sent back, once, and the address forgets the code', async () => {
    serve(NOT_CONNECTED, (method, path) => (method === 'post' && path === '/api/v1/online-payments/connect/complete'
      ? Promise.resolve(CONNECTED)
      : Promise.reject(new Error(`Sin respuesta para ${method} ${path}`))));

    renderPage('/settings/online-payments?code=TG-codigo&state=7b0c2f4e');

    expect(await screen.findByText('Conectada')).toBeInTheDocument();
    expect(writes()).toEqual([
      ['post', '/api/v1/online-payments/connect/complete', { body: { state: '7b0c2f4e', code: 'TG-codigo' } }],
    ]);
    expect(screen.getByTestId('address')).toHaveTextContent(/^\/settings\/online-payments$/);
    expect(toast.success).toHaveBeenCalledWith('Cuenta de Mercado Pago conectada.');
    expect(screen.getByText('123456789')).toBeInTheDocument();
  });

  it('says why the connection did not finish', async () => {
    serve(NOT_CONNECTED, () => Promise.reject(apiError(422, 'online_payments.connection', 'La autorización venció. Conecta la cuenta otra vez.')));

    renderPage('/settings/online-payments?code=TG-codigo&state=7b0c2f4e');

    expect(await screen.findByText('La autorización venció. Conecta la cuenta otra vez.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Conectar Mercado Pago' })).toBeEnabled();
  });

  it('says the owner did not authorize, and asks nothing of the server', async () => {
    serve(NOT_CONNECTED);

    renderPage('/settings/online-payments?error=access_denied&state=7b0c2f4e');

    expect(await screen.findByText('No se conectó la cuenta: la autorización en Mercado Pago no se completó.')).toBeInTheDocument();
    expect(writes()).toEqual([]);
    expect(screen.getByTestId('address')).toHaveTextContent(/^\/settings\/online-payments$/);
  });

  it('turns paying online on and off, and disconnects the account after asking only once it is off', async () => {
    let accepting = false;
    serve(CONNECTED, (method, path, body) => {
      if (method === 'put' && path === '/api/v1/online-payments/accepts-payments') {
        accepting = (body as { accepts: boolean }).accepts;
        return Promise.resolve({ ...CONNECTED, acceptsPayments: accepting });
      }
      if (method === 'delete' && path === '/api/v1/online-payments/account') return Promise.resolve(NOT_CONNECTED);
      return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
    });

    renderPage();
    const toggle = await screen.findByRole('switch', { name: 'Ofrecer pagar en línea en mi menú' });
    expect(toggle).not.toBeChecked();
    await userEvent.click(toggle);

    await waitFor(() => expect(toggle).toBeChecked());
    expect(toast.success).toHaveBeenCalledWith('Tu menú ya ofrece pagar en línea.');
    // On, the account stays: no payment may start with an account on its way out.
    expect(screen.getByRole('button', { name: 'Desconectar' })).toBeDisabled();
    expect(screen.getByText('Para desconectar o cambiar la cuenta, primero apaga el pago en línea.')).toBeInTheDocument();

    await userEvent.click(toggle);
    await waitFor(() => expect(toggle).not.toBeChecked());
    await userEvent.click(screen.getByRole('button', { name: 'Desconectar' }));
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Desconectar Mercado Pago', danger: true }));
    expect(await screen.findByRole('button', { name: 'Conectar Mercado Pago' })).toBeInTheDocument();
    expect(writes()).toEqual([
      ['put', '/api/v1/online-payments/accepts-payments', { body: { accepts: true } }],
      ['put', '/api/v1/online-payments/accepts-payments', { body: { accepts: false } }],
      ['delete', '/api/v1/online-payments/account'],
    ]);
  });

  it('keeps the account while it is not confirmed', async () => {
    confirm.mockResolvedValue(false);
    serve(CONNECTED);

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Desconectar' }));

    expect(confirm).toHaveBeenCalled();
    expect(writes()).toEqual([]);
  });

  it('waits for the payments and refunds in course before the account goes, asks again meanwhile, and points to the refused refunds', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    onTestFinished(() => { vi.useRealTimers(); });
    serve({ ...CONNECTED, checkoutsInCourse: 1, refundsInCourse: 1, refundsRefused: 2, needsReconnect: true });

    renderPage();

    expect(await screen.findByText(/Hay 2 pagos o devoluciones en curso: para cambiar o desconectar la cuenta, espera a que terminen \(unos 45 minutos\)/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Desconectar' })).toBeDisabled();
    // Connecting the same account again is how it starts answering again, also with payments in course.
    expect(screen.getByRole('button', { name: 'Volver a conectar' })).toBeEnabled();
    expect(screen.getByText(/La cuenta de Mercado Pago ya no responde/)).toBeInTheDocument();
    expect(screen.getByText(/Mercado Pago no hizo 2 devoluciones/)).toBeInTheDocument();

    // Once they end, the next ask lets the account go.
    serve({ ...CONNECTED, refundsRefused: 2, needsReconnect: true });
    await vi.advanceTimersByTimeAsync(IN_COURSE_REFRESH_MS);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Desconectar' })).toBeEnabled());
  });

  it('tells the owner the menu must take orders for paying online to show', async () => {
    serve({ ...CONNECTED, menuTakesOrders: false, sandbox: true });

    renderPage();

    expect(await screen.findByText('Modo de pruebas')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Configuración › Negocio' })).toHaveAttribute('href', '/settings/business');
  });
});
