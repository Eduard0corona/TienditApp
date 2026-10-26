import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import CustomerFiscalDataModal from './CustomerFiscalDataModal';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const KARLA = { rfc: 'XIQB891116QE4', name: 'KARLA FUENTE NOLASCO', postalCode: '26015', fiscalRegime: '612', cfdiUse: 'G03' };

function renderModal(fiscalData: typeof KARLA | null, onClose = vi.fn(), invoiceEmail: string | null = null) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <CustomerFiscalDataModal customer={{ id: 'c-1', name: 'Karla', fiscalData, invoiceEmail }} onClose={onClose} />
    </QueryClientProvider>,
  );
  return onClose;
}

describe('CustomerFiscalDataModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(apiRequest).mockResolvedValue({});
  });

  it('saves the fiscal data as the API takes it, with a use the regime may ask for', async () => {
    const onClose = renderModal(null);

    await userEvent.type(screen.getByLabelText('RFC'), 'xiqb891116qe4');
    await userEvent.type(screen.getByLabelText('Nombre o razón social'), 'KARLA FUENTE NOLASCO');
    await userEvent.type(screen.getByLabelText('Código postal fiscal'), '26015');
    // A salaried person cannot ask for G03: the use changes to one the regime may ask for.
    await userEvent.selectOptions(screen.getByLabelText('Régimen fiscal'), '605');
    expect(screen.getByLabelText('Uso del CFDI')).toHaveValue('D01');
    expect(screen.queryByRole('option', { name: 'G03 · Gastos en general' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(onClose).toHaveBeenCalledWith(true));
    expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/customers/{id}/fiscal-data', {
      path: { id: 'c-1' },
      body: { fiscalData: { ...KARLA, fiscalRegime: '605', cfdiUse: 'D01' }, invoiceEmail: '' },
    });
    expect(toast.success).toHaveBeenCalledWith('Datos fiscales de Karla guardados.');
  });

  it('keeps the email for their invoices with the fiscal data, and takes only a plain address', async () => {
    const onClose = renderModal(KARLA, vi.fn(), 'facturas@ejemplo.mx');
    const email = screen.getByLabelText('Correo para facturas (opcional)');
    expect(email).toHaveValue('facturas@ejemplo.mx');

    await userEvent.clear(email);
    await userEvent.type(email, 'pagos');
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
    expect(email).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText('Escribe un correo válido, como nombre@ejemplo.com, o déjalo vacío.')).toBeInTheDocument();

    await userEvent.type(email, '@ejemplo.mx ');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(onClose).toHaveBeenCalledWith(true));
    expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/customers/{id}/fiscal-data', {
      path: { id: 'c-1' },
      body: { fiscalData: KARLA, invoiceEmail: 'pagos@ejemplo.mx' },
    });
  });

  it('forgets the email alone when it is left empty', async () => {
    const onClose = renderModal(KARLA, vi.fn(), 'facturas@ejemplo.mx');

    await userEvent.clear(screen.getByLabelText('Correo para facturas (opcional)'));
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(onClose).toHaveBeenCalledWith(true));
    // Empty, not left out: left out, the API keeps the one it has.
    expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/customers/{id}/fiscal-data', {
      path: { id: 'c-1' },
      body: { fiscalData: KARLA, invoiceEmail: '' },
    });
  });

  it('forgets them without fiscal data in the body', async () => {
    const onClose = renderModal(KARLA);

    await userEvent.click(screen.getByRole('button', { name: 'Quitar datos fiscales' }));

    await waitFor(() => expect(onClose).toHaveBeenCalledWith(true));
    expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/customers/{id}/fiscal-data', { path: { id: 'c-1' }, body: {} });
  });

  it('keeps the dialog open with the reason of the server', async () => {
    const config = { headers: new AxiosHeaders() };
    vi.mocked(apiRequest).mockRejectedValue(new AxiosError('Bad Request', 'ERR_BAD_REQUEST', config, null, {
      status: 400, statusText: '', headers: {}, config,
      data: { code: 'validation_failed', errors: { 'FiscalData.Rfc': ['El RFC del cliente no tiene un formato válido.'] } },
    }));
    const onClose = renderModal(KARLA);

    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('El RFC del cliente no tiene un formato válido.'));
    expect(onClose).not.toHaveBeenCalled();
  });
});
