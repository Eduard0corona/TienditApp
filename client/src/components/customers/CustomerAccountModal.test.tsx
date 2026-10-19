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
  orders: [
    { orderId: 'o1', orderNumber: 'ORD-000010', createdAt: '2026-10-10T18:00:00Z', status: 6, soldOnCredit: true, total: 100, paid: 0, balance: 100 },
    { orderId: 'o2', orderNumber: 'ORD-000014', createdAt: '2026-10-15T18:00:00Z', status: 6, soldOnCredit: false, total: 80, paid: 30, balance: 50 },
  ],
  ordersCount: 2,
  recentPayments: [{ orderNumber: 'ORD-000014', paidAt: '2026-10-15T18:05:00Z', amount: 30, method: OrderPaymentMethod.Cash }],
} as CustomerAccount;

function apiError(status: number, code: string, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function signIn(permissions: string[]) {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'Employee', permissions } } as unknown as ReturnType<typeof useAuth>);
}

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
      body: expect.objectContaining({ customerId: 'c1', amount: 100, method: OrderPaymentMethod.Cash, cashReceived: 100 }),
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
    expect(request.mock.calls.filter(([method]) => method === 'post')).toHaveLength(2);
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
