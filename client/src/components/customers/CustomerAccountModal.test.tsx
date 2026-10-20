import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { CustomerAccount } from '../../api/customerCredit';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OrderPaymentMethod, TenantCapability } from '../../types';
import CustomerAccountModal from './CustomerAccountModal';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));

const request = vi.mocked(apiRequest);
const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };

const ACCOUNT = {
  phone: '+525512345678',
  name: 'Ana López',
  customerId: 'c1',
  creditLimit: 500,
  sellsOnCredit: true,
  owed: 150,
  available: 350,
  overLimit: false,
  creditTermDays: null,
  overdue: 0,
  orders: [
    { orderId: 'o1', orderNumber: 'ORD-000010', createdAt: '2026-10-10T18:00:00Z', status: 6, soldOnCredit: true, total: 100, paid: 0, writtenOff: 0, balance: 100, dueOn: null, isOverdue: false },
    { orderId: 'o2', orderNumber: 'ORD-000014', createdAt: '2026-10-15T18:00:00Z', status: 6, soldOnCredit: false, total: 80, paid: 30, writtenOff: 0, balance: 50, dueOn: null, isOverdue: false },
  ],
  ordersCount: 2,
  recentPayments: [{ orderNumber: 'ORD-000014', paidAt: '2026-10-15T18:05:00Z', amount: 30, method: OrderPaymentMethod.Cash }],
  recentWriteOffs: [],
} as CustomerAccount;

function apiError(status: number, code: string, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function signIn(permissions: string[], role = 'Employee') {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role, permissions } } as unknown as ReturnType<typeof useAuth>);
}

/** Ana's account with a term of 7 days: the older order is overdue and part of it was forgiven. */
const WITH_TERM = {
  ...ACCOUNT,
  creditTermDays: 7,
  overdue: 100,
  orders: [
    { ...ACCOUNT.orders[0], dueOn: '2026-10-17', isOverdue: true, writtenOff: 20, paid: 0, total: 120 },
    { ...ACCOUNT.orders[1], dueOn: '2026-10-22', isOverdue: false },
  ],
  recentWriteOffs: [{ orderNumber: 'ORD-000010', writtenOffAt: '2026-10-18T17:00:00Z', amount: 20, reason: 3, notes: 'Cliente de años' }],
} as CustomerAccount;

function renderModal() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <CustomerAccountModal of={{ customerId: 'c1' }} name="Ana" onClose={vi.fn()} />
    </QueryClientProvider>,
  );
}

describe('CustomerAccountModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useTenant).mockReturnValue({
      tenant: { name: 'Tiendita Lupita' },
      capabilities: TenantCapability.Retail,
    } as unknown as ReturnType<typeof useTenant>);
  });

  it('shows what they owe, their limit, the orders owed and the summary for WhatsApp', async () => {
    signIn(['ManageCustomers']);
    request.mockResolvedValue(ACCOUNT as never);
    renderModal();

    expect(await screen.findByText('ORD-000010')).toBeInTheDocument();
    expect(screen.getByText('Fiado')).toBeInTheDocument();
    expect(screen.getByText('$350.00')).toBeInTheDocument();
    const whatsApp = screen.getByRole('link', { name: /Enviar por WhatsApp/ });
    expect(whatsApp.getAttribute('href')).toContain('https://wa.me/525512345678?text=');
    expect(decodeURIComponent(whatsApp.getAttribute('href') ?? '')).toContain('Debes *$150.00*');
    // Looking after customers is not charging: no «Abonar».
    expect(screen.queryByRole('button', { name: /Abonar/ })).not.toBeInTheDocument();
    expect(request).toHaveBeenCalledWith('get', '/api/v1/customers/account', expect.objectContaining({ query: { customerId: 'c1', phone: undefined } }));
  });

  it('takes a payment to the account, one per method, and says it was registered', async () => {
    signIn(['ManageOrders', 'ChargeOrders']);
    request.mockImplementation(((method: string) => Promise.resolve(method === 'post' ? { ...ACCOUNT, owed: 50 } : ACCOUNT)) as never);
    renderModal();

    await userEvent.click(await screen.findByRole('button', { name: /Abonar/ }));
    await userEvent.type(screen.getByLabelText('Efectivo recibido'), '100');
    await userEvent.click(screen.getByRole('button', { name: 'Abonar $100.00' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Abono de $100.00 registrado.'));
    expect(request).toHaveBeenCalledWith('post', '/api/v1/customers/account/payments', {
      body: expect.objectContaining({ customerId: 'c1', amount: 100, method: OrderPaymentMethod.Cash, cashReceived: 100, expectedOwed: 150 }),
    });
  });

  it('closes the panel when one payment went through and the next did not, so a retry never sends it again', async () => {
    signIn(['ManageOrders', 'ChargeOrders']);
    request.mockImplementation(((method: string, _path: string, options?: { body?: { method?: number } }) => {
      if (method !== 'post') return Promise.resolve(ACCOUNT);
      return options?.body?.method === OrderPaymentMethod.Card
        ? Promise.reject(apiError(400, 'bad_request', 'La terminal no respondió.'))
        : Promise.resolve({ ...ACCOUNT, owed: 50 });
    }) as never);
    renderModal();

    await userEvent.click(await screen.findByRole('button', { name: /Abonar/ }));
    await userEvent.type(screen.getByLabelText('Efectivo recibido'), '100');
    await userEvent.click(screen.getByRole('button', { name: 'Agregar otro pago' }));
    await userEvent.click(screen.getByRole('button', { name: 'Abonar $150.00' }));

    expect(await screen.findByText(/Se registraron \$100\.00; el resto no: La terminal no respondió\./)).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Estado de cuenta' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Efectivo recibido')).not.toBeInTheDocument();
    // Each payment says what the account owed as the screen saw it: the card one, after the cash.
    expect(request.mock.calls.filter(([method]) => method === 'post').map(([, , options]) => (options as unknown as { body: { expectedOwed: number } }).body.expectedOwed))
      .toEqual([150, 50]);
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('shows the term, what is overdue, when each order is due and what was forgiven', async () => {
    signIn(['ManageCustomers']);
    request.mockResolvedValue(WITH_TERM as never);
    renderModal();

    expect(await screen.findByText('Plazo de 7 días ·')).toBeInTheDocument();
    expect(screen.getByText('Vencido')).toBeInTheDocument();
    expect(screen.getByText(/^Venció el 17 oct 2026$/)).toBeInTheDocument();
    expect(screen.getByText(/^Vence el 22 oct 2026$/)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Lo condonado' })).toBeInTheDocument();
    expect(screen.getByText(/Cortesía · ORD-000010/)).toBeInTheDocument();
    expect(screen.getByText('Cliente de años')).toBeInTheDocument();
    const whatsApp = decodeURIComponent(screen.getByRole('link', { name: /Enviar por WhatsApp/ }).getAttribute('href') ?? '');
    expect(whatsApp).toContain('De eso, *$100.00* ya venció');
    // Only owners and admins forgive.
    expect(screen.queryByRole('button', { name: 'Condonar' })).not.toBeInTheDocument();
  });

  it('lets an owner forgive part of one order, with the reason, and sends what the account owed', async () => {
    signIn([], 'TenantAdmin');
    request.mockImplementation(((method: string) => Promise.resolve(method === 'post' ? { ...ACCOUNT, owed: 110 } : ACCOUNT)) as never);
    renderModal();

    await userEvent.click(await screen.findByRole('button', { name: 'Condonar' }));
    expect(screen.getByRole('dialog', { name: 'Condonar' })).toBeInTheDocument();
    expect(screen.getByLabelText('Cuánto')).toHaveValue('150.00');
    await userEvent.selectOptions(screen.getByLabelText('¿Qué condonas?'), 'o2');
    expect(screen.getByLabelText('Cuánto')).toHaveValue('50.00');
    await userEvent.clear(screen.getByLabelText('Cuánto'));
    await userEvent.type(screen.getByLabelText('Cuánto'), '60');
    expect(screen.getByText('No puedes condonar más de $50.00.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Condonar \$/ })).toBeDisabled();
    await userEvent.clear(screen.getByLabelText('Cuánto'));
    await userEvent.type(screen.getByLabelText('Cuánto'), '40');
    // «Otro» needs a note.
    await userEvent.selectOptions(screen.getByLabelText('Motivo'), 'Otro');
    expect(screen.getByRole('button', { name: 'Condonar $40.00' })).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/^Nota/), 'Pagó con trabajo');
    await userEvent.click(screen.getByRole('button', { name: 'Condonar $40.00' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Se condonaron $40.00.'));
    expect(request).toHaveBeenCalledWith('post', '/api/v1/customers/account/write-offs', {
      body: { customerId: 'c1', phone: undefined, orderId: 'o2', amount: 40, reason: 99, notes: 'Pagó con trabajo', expectedOwed: 150 },
    });
    expect(screen.getByRole('dialog', { name: 'Estado de cuenta' })).toBeInTheDocument();
  });

  it('keeps the write-off panel with the server’s reason when it is refused', async () => {
    signIn([], 'TenantAdmin');
    request.mockImplementation(((method: string) => method === 'post'
      ? Promise.reject(apiError(400, 'bad_request', 'La cuenta cambió: ahora debe $50.00. Revisa el estado de cuenta antes de cobrar.'))
      : Promise.resolve(ACCOUNT)) as never);
    renderModal();

    await userEvent.click(await screen.findByRole('button', { name: 'Condonar' }));
    await userEvent.click(screen.getByRole('button', { name: 'Condonar $150.00' }));

    expect(await screen.findByText(/La cuenta cambió: ahora debe \$50\.00/)).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Condonar' })).toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('keeps the panel with the server’s reason when the payment is refused', async () => {
    signIn(['ManageOrders', 'ChargeOrders']);
    request.mockImplementation(((method: string) => method === 'post'
      ? Promise.reject(apiError(400, 'bad_request', 'Abre tu caja para cobrar: todo cobro se registra en una caja.'))
      : Promise.resolve(ACCOUNT)) as never);
    renderModal();

    await userEvent.click(await screen.findByRole('button', { name: /Abonar/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Abonar $150.00' }));

    expect(await screen.findByText(/Abre tu caja para cobrar/)).toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
  });
});
