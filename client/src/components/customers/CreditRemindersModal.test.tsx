import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { CreditReminders } from '../../api/customerCredit';
import { apiRequest } from '../../api/typedClient';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import CreditRemindersModal from './CreditRemindersModal';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));

const request = vi.mocked(apiRequest);
const toast = { success: vi.fn(), error: vi.fn() };

function settings(overrides: Partial<CreditReminders> = {}): CreditReminders {
  return {
    enabled: true,
    everyDays: 7,
    templateId: null,
    whatsAppConnected: true,
    creditTermDays: 15,
    customersWithOwnTerm: 0,
    defaultBody: 'Hola {nombre}, debes {monto}; {vencido} venció el {fecha}.',
    placeholders: ['nombre', 'negocio', 'monto', 'vencido', 'fecha'],
    templates: [],
    minEveryDays: 1,
    maxEveryDays: 30,
    sendFrom: '10:00',
    sendUntil: '19:00',
    dueSoon: {
      enabled: false,
      daysBefore: 2,
      templateId: null,
      defaultBody: 'Hola {nombre}, en {negocio} debes {monto}; {porvencer} vence el {fecha}.',
      placeholders: ['nombre', 'negocio', 'monto', 'porvencer', 'fecha'],
      templates: [{ id: 't-soon', name: 'Por vencer', body: 'Ana: el {fecha} vence {porvencer}.', providerTemplateName: 'por_vencer' }],
      minDaysBefore: 1,
      maxDaysBefore: 7,
    },
    ...overrides,
  };
}

function renderModal(current: CreditReminders, onClose = vi.fn()) {
  request.mockImplementation(((method: string, _path: string, options?: { body?: unknown }) => method === 'get'
    ? Promise.resolve(current)
    : Promise.resolve({ ...current, ...(options?.body as object), dueSoon: { ...current.dueSoon, ...(options?.body as { dueSoon: object }).dueSoon } })) as never);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <CreditRemindersModal onClose={onClose} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return onClose;
}

describe('CreditRemindersModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useTenant).mockReturnValue({ tenant: { name: 'Tiendita' } } as unknown as ReturnType<typeof useTenant>);
  });

  it('turns the notice before the last day to pay on with its template, next to the reminders', async () => {
    const onClose = renderModal(settings());

    await userEvent.click(await screen.findByLabelText('Avisar antes de que venza'));
    await userEvent.clear(screen.getByLabelText('Días antes'));
    await userEvent.type(screen.getByLabelText('Días antes'), '3');
    const [noticeMessage] = screen.getAllByLabelText('Mensaje');
    await userEvent.selectOptions(noticeMessage, 't-soon');
    expect(screen.getByText('Ana: el lunes 5 de octubre vence $500.00.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(request).toHaveBeenCalledWith('put', '/api/v1/customers/credit-reminders', {
      body: { enabled: true, everyDays: 7, templateId: null, dueSoon: { enabled: true, daysBefore: 3, templateId: 't-soon' } },
    });
    expect(toast.success).toHaveBeenCalledWith('Se avisará 3 días antes de que venza y se recordará lo vencido cada 7 días.');
  });

  it('refuses days before out of range, and turning either on without WhatsApp', async () => {
    renderModal(settings({ enabled: false, whatsAppConnected: false }));

    expect(await screen.findByText(/Conecta WhatsApp para mandar recordatorios/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeEnabled();
    await userEvent.click(screen.getByLabelText('Avisar antes de que venza'));
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
    await userEvent.click(screen.getByLabelText('Avisar antes de que venza'));
    await userEvent.clear(screen.getByLabelText('Días antes'));
    await userEvent.type(screen.getByLabelText('Días antes'), '8');
    expect(screen.getByText('El aviso se manda de 1 a 7 días antes.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
  });

  it('says when both are off', async () => {
    const onClose = renderModal(settings({ dueSoon: { ...settings().dueSoon, enabled: true } }));

    await userEvent.click(await screen.findByLabelText('Avisar antes de que venza'));
    await userEvent.click(screen.getByLabelText('Recordar lo vencido por WhatsApp'));
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(toast.success).toHaveBeenCalledWith('Los recordatorios del fiado están apagados.');
  });
});
