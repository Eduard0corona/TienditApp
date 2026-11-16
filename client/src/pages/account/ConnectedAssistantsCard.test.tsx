import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { assistantsApi, type MyAssistantConnection } from '../../api/assistantsApi';
import ConfirmProvider from '../../components/feedback/ConfirmProvider';
import ToastProvider from '../../components/feedback/ToastProvider';
import ConnectedAssistantsCard from './ConnectedAssistantsCard';

vi.mock('../../api/assistantsApi', () => ({
  assistantsApi: { getMine: vi.fn(), disconnect: vi.fn() },
}));

const api = vi.mocked(assistantsApi);

const claude: MyAssistantConnection = {
  id: 'c1', name: 'Claude', site: 'claude.ai', grantedAt: '2026-11-10T18:00:00Z', lastUsedAt: '2026-11-15T20:00:00Z',
};
const unnamed: MyAssistantConnection = {
  id: 'c2', name: null, site: 'localhost:6274', grantedAt: '2026-11-16T09:00:00Z', lastUsedAt: null,
};

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <ToastProvider>
          <ConfirmProvider>
            <ConnectedAssistantsCard />
          </ConfirmProvider>
        </ToastProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ConnectedAssistantsCard (Fase 32E)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('lists the assistants connected, with the site they return to and when they last read the account', async () => {
    api.getMine.mockResolvedValue([claude, unnamed]);

    renderCard();

    const card = screen.getByRole('region', { name: 'Asistentes conectados' });
    const items = await within(card).findAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('Claude');
    expect(items[0]).toHaveTextContent('claude.ai · conectado el');
    expect(items[0]).toHaveTextContent('lo usó el');
    expect(items[1]).toHaveTextContent('Asistente sin nombre');
    expect(items[1]).toHaveTextContent('todavía no lo usa');
    expect(within(card).getByRole('link', { name: 'Cómo conectar uno' })).toHaveAttribute('href', '/asistentes');
  });

  it('disconnects one after asking, and says when there are none', async () => {
    const user = userEvent.setup();
    api.getMine.mockResolvedValueOnce([claude]).mockResolvedValue([]);
    api.disconnect.mockResolvedValue(undefined as never);

    renderCard();
    await user.click(await screen.findByRole('button', { name: 'Desconectar Claude' }));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('Claude deja de ver tu cuenta');
    await user.click(within(dialog).getByRole('button', { name: 'Desconectar' }));

    await waitFor(() => expect(api.disconnect).toHaveBeenCalledWith('c1'));
    expect(await screen.findByText('No tienes asistentes conectados.')).toBeInTheDocument();
    expect(await screen.findByText('Claude: desconectado.')).toBeInTheDocument();
  });

  it('offers to try again when they cannot be read', async () => {
    const user = userEvent.setup();
    api.getMine.mockRejectedValueOnce(new Error('red')).mockResolvedValue([]);

    renderCard();
    await user.click(await screen.findByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByText('No tienes asistentes conectados.')).toBeInTheDocument();
  });
});
