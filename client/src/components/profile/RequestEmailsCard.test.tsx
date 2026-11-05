import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import RequestEmailsCard from './RequestEmailsCard';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

function apiError(status: number, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code: 'business_rule', detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

const renderCard = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <RequestEmailsCard businessName="Estética Luna" />
    </QueryClientProvider>,
  );

describe('RequestEmailsCard (Fase 29N)', () => {
  beforeEach(() => {
    vi.mocked(apiRequest).mockReset();
  });

  it('says whether each request reaches the person by email, and where, and lets them turn it off', async () => {
    const user = userEvent.setup();
    vi.mocked(apiRequest).mockImplementation(async (method, _path, options) =>
      (method === 'get'
        ? { enabled: true, email: 'ana@demo.com', emailConfirmed: true }
        : { enabled: (options as { body: { enabled: boolean } }).body.enabled, email: 'ana@demo.com', emailConfirmed: true }) as never);
    renderCard();

    const toggle = await screen.findByRole('switch', { name: 'Recibir un correo con cada solicitud de cita o de visita' });
    expect(toggle).toBeChecked();
    expect(screen.getByText('ana@demo.com')).toBeInTheDocument();
    expect(screen.getByText(/en línea en Estética Luna\. Solo cambia para ti\./)).toBeInTheDocument();
    expect(screen.queryByText(/no está confirmado/)).not.toBeInTheDocument();

    await user.click(toggle);

    await waitFor(() => expect(toggle).not.toBeChecked());
    expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/appointments/request-emails', { body: { enabled: false } });
  });

  it('warns that nothing arrives while the email is not confirmed, and says why a change was not saved', async () => {
    const user = userEvent.setup();
    vi.mocked(apiRequest).mockImplementation(async (method) => {
      if (method === 'get') return { enabled: false, email: 'luis@demo.com', emailConfirmed: false } as never;
      throw apiError(400, 'No se pudo.');
    });
    renderCard();

    const toggle = await screen.findByRole('switch');
    expect(toggle).not.toBeChecked();
    expect(screen.getByText('Tu correo todavía no está confirmado: no te llegan hasta que lo confirmes.')).toBeInTheDocument();

    await user.click(toggle);

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo.');
    expect(toggle).not.toBeChecked();
  });

  it('shows nothing to whoever is not on the team', async () => {
    vi.mocked(apiRequest).mockRejectedValue(apiError(404, 'Estos avisos son para el equipo del negocio.'));
    const { container } = renderCard();

    await waitFor(() => expect(container).toBeEmptyDOMElement());
  });
});
