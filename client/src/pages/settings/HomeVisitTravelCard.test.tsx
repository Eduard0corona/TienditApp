import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import HomeVisitTravelCard from './HomeVisitTravelCard';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

/** The API with the travel time saved, which a save changes. */
function serve(saved: { travelMinutes: number }) {
  vi.mocked(apiRequest).mockImplementation(((method: string, path: string, options?: { body?: { travelMinutes: number } }) => {
    if (path !== '/api/v1/delivery-zones/home-visits') return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
    if (method === 'put') saved.travelMinutes = options!.body!.travelMinutes;
    return Promise.resolve({ ...saved });
  }) as unknown as typeof apiRequest);
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <HomeVisitTravelCard />
    </QueryClientProvider>,
  );
}

describe('HomeVisitTravelCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows the travel time saved and saves another one', async () => {
    const user = userEvent.setup();
    serve({ travelMinutes: 30 });
    renderCard();

    const minutes = screen.getByLabelText('Traslado entre citas (minutos)');
    await waitFor(() => expect(minutes).toHaveValue(30));
    const save = screen.getByRole('button', { name: 'Guardar' });
    expect(save).toBeDisabled();

    await user.clear(minutes);
    await user.type(minutes, '45');
    await user.click(save);

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Tiempo de traslado guardado.'));
    expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/delivery-zones/home-visits', { body: { travelMinutes: 45 } });
    await waitFor(() => expect(minutes).toHaveValue(45));
    expect(save).toBeDisabled();
  });

  it('says the limits before sending a time outside them', async () => {
    const user = userEvent.setup();
    serve({ travelMinutes: 30 });
    renderCard();

    const minutes = screen.getByLabelText('Traslado entre citas (minutos)');
    await waitFor(() => expect(minutes).toHaveValue(30));
    await user.clear(minutes);
    await user.type(minutes, '241');

    expect(screen.getByText('El traslado entre visitas va de 0 a 240 minutos.')).toBeInTheDocument();
    expect(minutes).toHaveAttribute('aria-describedby', 'home-visit-travel-help');
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
    expect(vi.mocked(apiRequest).mock.calls.filter(([method]) => method === 'put')).toHaveLength(0);
  });
});
