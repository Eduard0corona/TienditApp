import { render, screen, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { platformApi } from '../../api/platformApi';
import FakeRequestsWatch from './FakeRequestsWatch';

vi.mock('../../api/platformApi', () => ({ platformApi: { getFakeRequests: vi.fn() } }));

function renderWatch() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={client}><FakeRequestsWatch /></QueryClientProvider>);
}

describe('FakeRequestsWatch (Fase 30A)', () => {
  beforeEach(() => vi.clearAllMocks());

  it('shows the businesses that mark the most against their requests, and the phones several marked by their last digits', async () => {
    vi.mocked(platformApi.getFakeRequests).mockResolvedValue({
      top: 20,
      businesses: [
        { tenantId: 't1', name: 'Estética Luna', slug: 'estetica-luna', marked: 6, takenBack: 1, requests: 48, lastMarkedAt: '2026-10-27T18:40:00Z' },
      ],
      phones: [{ ending: '7765', businessNames: ['Estética Luna', 'Spa Sol'], lastMarkedAt: '2026-10-27T18:40:00Z' }],
    });
    renderWatch();

    const region = await screen.findByRole('region', { name: 'Solicitudes marcadas como falsas' });
    const row = await within(region).findByRole('row', { name: /Estética Luna/ });
    expect(within(row).getAllByRole('cell').map((cell) => cell.textContent)).toEqual(
      ['Estética Lunaestetica-luna', '6', '1', '48', expect.stringMatching(/27/)],
    );
    expect(within(region).getByLabelText('Teléfono que termina en 7 7 6 5')).toHaveTextContent('•••• 7765');
    expect(within(region).getByText('Estética Luna, Spa Sol')).toBeInTheDocument();
    expect(within(region).queryByText(/muestra los 20 primeros/)).not.toBeInTheDocument();
  });

  it('says when nobody marked any, and when a list reaches its limit', async () => {
    vi.mocked(platformApi.getFakeRequests).mockResolvedValue({ top: 1, businesses: [], phones: [{ ending: '1234', businessNames: ['A', 'B'], lastMarkedAt: '2026-10-27T18:40:00Z' }] });
    renderWatch();

    expect(await screen.findByText('Ningún negocio marcó solicitudes como falsas.')).toBeInTheDocument();
    expect(screen.getByText('Cada lista muestra los 1 primeros.')).toBeInTheDocument();
  });
});
