import { render, screen, waitFor, within } from '@testing-library/react';
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

/** One half of the dialog, by its legend. */
const section = async (name: 'Antes de que venza' | 'Lo vencido') => within(await screen.findByRole('group', { name }));

describe('CreditRemindersModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useTenant).mockReturnValue({ tenant: { name: 'Tiendita' } } as unknown as ReturnType<typeof useTenant>);
  });

  it('turns the notice before the last day to pay on with its template, next to the reminders', async () => {
    const onClose = renderModal(settings());

    const notice = await section('Antes de que venza');
    await userEvent.click(notice.getByLabelText('Avisar por WhatsApp antes de que venza'));
    await userEvent.clear(notice.getByLabelText('Anticipación'));
    await userEvent.type(notice.getByLabelText('Anticipación'), '3');
    await userEvent.selectOptions(notice.getByLabelText('Mensaje'), 't-soon');
    expect(notice.getByText('Ana: el lunes 5 de octubre vence $500.00.')).toBeInTheDocument();
    expect(notice.queryByText(/Sin plantilla aprobada en Meta/)).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(request).toHaveBeenCalledWith('put', '/api/v1/customers/credit-reminders', {
      body: { enabled: true, everyDays: 7, templateId: null, dueSoon: { enabled: true, daysBefore: 3, templateId: 't-soon' } },
    });
    expect(toast.success).toHaveBeenCalledWith('Se avisará 3 días antes de que venza y se recordará lo vencido cada 7 días.');
  });

  it('refuses days before out of range while the notice is on, and turning either on without WhatsApp', async () => {
    renderModal(settings({ enabled: false, whatsAppConnected: false }));

    expect(await screen.findByText(/Conecta WhatsApp para mandar recordatorios/)).toBeInTheDocument();
    const notice = await section('Antes de que venza');
    const overdue = await section('Lo vencido');
    const save = screen.getByRole('button', { name: 'Guardar' });
    expect(save).toBeEnabled();
    await userEvent.click(notice.getByLabelText('Avisar por WhatsApp antes de que venza'));
    expect(save).toBeDisabled();
    await userEvent.click(notice.getByLabelText('Avisar por WhatsApp antes de que venza'));
    await userEvent.click(overdue.getByLabelText('Recordar lo vencido por WhatsApp'));
    expect(save).toBeDisabled();
    await userEvent.click(overdue.getByLabelText('Recordar lo vencido por WhatsApp'));
    expect(save).toBeEnabled();

    await userEvent.click(notice.getByLabelText('Avisar por WhatsApp antes de que venza'));
    await userEvent.clear(notice.getByLabelText('Anticipación'));
    await userEvent.type(notice.getByLabelText('Anticipación'), '8');
    expect(notice.getByText('El aviso se manda de 1 a 7 días antes.')).toBeInTheDocument();
    expect(save).toBeDisabled();
  });

  it('keeps the saved days of a message that is off when what is typed cannot be saved', async () => {
    const onClose = renderModal(settings());

    const notice = await section('Antes de que venza');
    await userEvent.clear(notice.getByLabelText('Anticipación'));
    await userEvent.type(notice.getByLabelText('Anticipación'), '9');
    // Off, it neither warns nor blocks the reminders.
    expect(notice.queryByText('El aviso se manda de 1 a 7 días antes.')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(request).toHaveBeenCalledWith('put', '/api/v1/customers/credit-reminders', {
      body: { enabled: true, everyDays: 7, templateId: null, dueSoon: { enabled: false, daysBefore: 2, templateId: null } },
    });
  });

  it('starts with the default text when the chosen notice template was retired, and sends none', async () => {
    const onClose = renderModal(settings({ dueSoon: { ...settings().dueSoon, enabled: true, templateId: 'retired' } }));

    const notice = await section('Antes de que venza');
    expect(notice.getByLabelText('Mensaje')).toHaveValue('');
    expect(notice.getByText(/Sin plantilla aprobada en Meta/)).toBeInTheDocument();
    await userEvent.click(notice.getByLabelText('Avisar por WhatsApp antes de que venza'));
    await userEvent.click((await section('Lo vencido')).getByLabelText('Recordar lo vencido por WhatsApp'));
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(request).toHaveBeenCalledWith('put', '/api/v1/customers/credit-reminders', {
      body: { enabled: false, everyDays: 7, templateId: null, dueSoon: { enabled: false, daysBefore: 2, templateId: null } },
    });
    expect(toast.success).toHaveBeenCalledWith('Los recordatorios del fiado están apagados.');
  });

  it('keeps the dialog with the server\'s reason when the save is refused', async () => {
    const onClose = renderModal(settings());
    request.mockImplementation(((method: string) => method === 'get'
      ? Promise.resolve(settings())
      : Promise.reject({ isAxiosError: true, response: { status: 400, data: { detail: 'Elige una plantilla de aviso antes de vencer activa.' } } })) as never);

    await userEvent.click((await section('Antes de que venza')).getByLabelText('Avisar por WhatsApp antes de que venza'));
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Elige una plantilla de aviso antes de vencer activa.');
    expect(onClose).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });
});
