import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { useToast } from '../../hooks/useToast';
import CreditLimitModal from './CreditLimitModal';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));

const request = vi.mocked(apiRequest);
const toast = { success: vi.fn(), error: vi.fn() };

function renderModal(customer: { creditLimit?: number | null; creditTermDays?: number | null }, onClose = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <CreditLimitModal customer={{ id: 'c1', name: 'Ana López', ...customer }} onClose={onClose} />
    </QueryClientProvider>,
  );
  return onClose;
}

describe('CreditLimitModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    request.mockImplementation(((method: string, path: string) => Promise.resolve(
      method === 'get' && path === '/api/v1/customers/credit-reminders' ? { creditTermDays: 15 } : {})) as never);
  });

  it('gives a customer a term of their own without touching their limit', async () => {
    const onClose = renderModal({ creditLimit: 500 });

    expect(await screen.findByText('· 15 días')).toBeInTheDocument();
    await userEvent.click(screen.getByLabelText('Un plazo para este cliente'));
    await userEvent.type(screen.getByLabelText('Días para pagar'), '7');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(request).toHaveBeenCalledWith('put', '/api/v1/customers/{id}/credit-term', { path: { id: 'c1' }, body: { creditTermDays: 7 } });
    expect(request).not.toHaveBeenCalledWith('put', '/api/v1/customers/{id}/credit-limit', expect.anything());
    expect(toast.success).toHaveBeenCalledWith('Ana López paga en 7 días.');
  });

  it('gives a term to who owes without fiado, and back to the business\'s', async () => {
    const onClose = renderModal({ creditLimit: null, creditTermDays: 7 });

    expect(screen.getByLabelText('Días para pagar')).toHaveValue('7');
    await userEvent.click(screen.getByLabelText(/El plazo del negocio/));
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(request).toHaveBeenCalledWith('put', '/api/v1/customers/{id}/credit-term', { path: { id: 'c1' }, body: { creditTermDays: null } });
    expect(toast.success).toHaveBeenCalledWith('Ana López paga en el plazo del negocio.');
  });

  it('saves the limit and the term together, and refuses a term out of range', async () => {
    renderModal({ creditLimit: null });

    await userEvent.type(screen.getByLabelText('Le fías hasta'), '800');
    await userEvent.click(screen.getByLabelText('Un plazo para este cliente'));
    await userEvent.type(screen.getByLabelText('Días para pagar'), '121');
    expect(screen.getByText('El plazo del fiado va de 1 a 120 días.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
    await userEvent.clear(screen.getByLabelText('Días para pagar'));
    await userEvent.type(screen.getByLabelText('Días para pagar'), '30');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Ana López puede deber hasta $800.00 y paga en 30 días.'));
    expect(request).toHaveBeenCalledWith('put', '/api/v1/customers/{id}/credit-limit', { path: { id: 'c1' }, body: { creditLimit: 800 } });
    expect(request).toHaveBeenCalledWith('put', '/api/v1/customers/{id}/credit-term', { path: { id: 'c1' }, body: { creditTermDays: 30 } });
  });

  it('keeps the modal with the server\'s reason when the term is refused', async () => {
    request.mockImplementation(((method: string, path: string) => method === 'put'
      ? Promise.reject({ isAxiosError: true, response: { status: 400, data: { detail: 'El plazo de un cliente va de 1 a 120 días, o el del negocio.' } } })
      : Promise.resolve(path === '/api/v1/customers/credit-reminders' ? { creditTermDays: null } : {})) as never);
    const onClose = renderModal({ creditLimit: 500 });

    expect(await screen.findByText('· sin plazo')).toBeInTheDocument();
    await userEvent.click(screen.getByLabelText('Un plazo para este cliente'));
    await userEvent.type(screen.getByLabelText('Días para pagar'), '7');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('El plazo de un cliente va de 1 a 120 días, o el del negocio.');
    expect(onClose).not.toHaveBeenCalled();
  });
});
