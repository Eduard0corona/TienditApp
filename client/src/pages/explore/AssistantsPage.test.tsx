import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useToast } from '../../hooks/useToast';
import { mcpAccountServerUrl, mcpServerUrl } from '../../utils/mcpServer';
import AssistantsPage from './AssistantsPage';

vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));

const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };

function renderPage() {
  return render(
    <MemoryRouter>
      <AssistantsPage />
    </MemoryRouter>,
  );
}

describe('AssistantsPage (Fase 32D)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
  });

  it('is the MCP server of this same site', () => {
    expect(mcpServerUrl('https://vecinomarkets.com')).toBe('https://vecinomarkets.com/mcp');
    expect(mcpServerUrl('https://vecinomarkets.com/')).toBe('https://vecinomarkets.com/mcp');
    // With the customer's account (Fase 32E).
    expect(mcpAccountServerUrl('https://vecinomarkets.com/')).toBe('https://vecinomarkets.com/mcp/cuenta');
  });

  it('gives the address of the server, how to add it to Claude and to others, and what the assistant never does', () => {
    renderPage();

    expect(screen.getByRole('heading', { level: 1, name: 'Pide con tu asistente de IA' })).toBeInTheDocument();
    expect(document.title).toBe('Pide con tu asistente de IA · TienditApp');
    const url = `${window.location.origin}/mcp`;
    expect(screen.getByLabelText('Dirección sin cuenta')).toHaveValue(url);
    expect(screen.getByLabelText('Dirección con tu cuenta')).toHaveValue(`${url}/cuenta`);
    const claude = screen.getByRole('region', { name: 'En Claude' });
    expect(within(claude).getByText('No sign-in')).toBeInTheDocument();
    expect(within(claude).getByLabelText('Comando para Claude Code')).toHaveValue(`claude mcp add --transport http tienditapp ${url}`);
    expect(within(claude).getByLabelText('Comando para Claude Code con tu cuenta'))
      .toHaveValue(`claude mcp add --transport http tienditapp-cuenta ${url}/cuenta`);
    // With the account: what it sees, that the customer decides, and where to disconnect it.
    const account = screen.getByRole('region', { name: 'Con tu cuenta' });
    expect(account).toHaveTextContent('tú decides');
    expect(within(account).getByRole('link', { name: 'Mi cuenta' })).toHaveAttribute('href', '/cuenta');
    expect(screen.getByRole('region', { name: 'En ChatGPT y otros asistentes' })).toHaveTextContent('sin autenticación');
    // Without connectors, the store's menu in text and its link (Fase 32A), from Explorar.
    expect(within(screen.getByRole('region', { name: '¿Tu asistente no acepta conectores?' })).getByRole('link', { name: 'Explorar' }))
      .toHaveAttribute('href', '/explorar');
    const safe = screen.getByRole('region', { name: 'Lo que hace tu asistente y lo que no' });
    expect(safe).toHaveTextContent('No envía el pedido');
    expect(safe).toHaveTextContent('Sin cuenta no ve tus datos');
    expect(safe).toHaveTextContent('Nunca le des datos de tarjeta.');
  });

  it('copies the address and the command, and shows them when the clipboard is blocked', async () => {
    const user = userEvent.setup();
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);
    renderPage();
    const url = `${window.location.origin}/mcp`;

    await user.click(screen.getByRole('button', { name: 'Copiar la dirección sin cuenta' }));
    expect(writeText).toHaveBeenCalledWith(url);
    expect(toast.success).toHaveBeenCalledWith('Dirección copiada.');
    await user.click(screen.getByRole('button', { name: 'Copiar la dirección con tu cuenta' }));
    expect(writeText).toHaveBeenLastCalledWith(`${url}/cuenta`);

    writeText.mockRejectedValueOnce(new Error('blocked'));
    await user.click(screen.getByRole('button', { name: 'Copiar el comando sin cuenta' }));
    expect(toast.info).toHaveBeenCalledWith(`claude mcp add --transport http tienditapp ${url}`);
  });
});
