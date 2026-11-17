import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { assistantsApi, type AssistantAuthorizationCheck } from '../../api/assistantsApi';
import { AuthContext, type AuthContextType } from '../../contexts/AuthContextDefinition';
import { returnToAssistant } from '../../utils/externalNavigation';
import AuthorizeAssistantPage from './AuthorizeAssistantPage';

vi.mock('../../api/assistantsApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/assistantsApi')>()),
  assistantsApi: { check: vi.fn(), answer: vi.fn() },
}));
vi.mock('../../components/common/BrandLogo', () => ({ default: () => <span>TienditApp</span> }));
vi.mock('../../utils/externalNavigation', () => ({ returnToAssistant: vi.fn() }));

const api = vi.mocked(assistantsApi);
const leave = vi.mocked(returnToAssistant);

const QUERY = '?response_type=code&client_id=abc&redirect_uri=https%3A%2F%2Fclaude.ai%2Fapi%2Fmcp%2Fauth_callback'
  + '&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256'
  + '&resource=https%3A%2F%2Fvecinomarkets.com%2Fmcp%2Fcuenta&scope=cuenta&state=xyz';

const valid: AssistantAuthorizationCheck = {
  valid: true, problem: null, errorRedirectUrl: null, clientName: 'Claude', redirectSite: 'claude.ai', redirectsToThisDevice: false,
};

function renderPage(role = 'CUSTOMER', logout = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const auth = {
    isAuthenticated: true,
    isLoading: false,
    user: { id: 'u1', email: 'mariana@demo.com', firstName: 'Mariana', lastName: 'Ortiz', role },
    logout,
  } as unknown as AuthContextType;
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthContext.Provider value={auth}>
        <MemoryRouter initialEntries={[`/autorizar${QUERY}`]}>
          <Routes>
            <Route path="/autorizar" element={<AuthorizeAssistantPage />} />
            <Route path="/login" element={<p>Iniciar sesión</p>} />
          </Routes>
        </MemoryRouter>
      </AuthContext.Provider>
    </QueryClientProvider>,
  );
}

describe('AuthorizeAssistantPage (Fase 32E)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows who asks, what it will see and where it returns, and allowing it goes back to the assistant', async () => {
    const user = userEvent.setup();
    api.check.mockResolvedValue(valid);
    api.answer.mockResolvedValue({ redirectUrl: 'https://claude.ai/api/mcp/auth_callback?code=c0de&state=xyz' });

    renderPage();

    expect(await screen.findByRole('heading', { name: 'Claude quiere usar tu cuenta de TienditApp' })).toBeInTheDocument();
    expect(document.title).toBe('Conectar un asistente · TienditApp');
    expect(api.check).toHaveBeenCalledWith(expect.objectContaining({
      responseType: 'code', clientId: 'abc', redirectUri: 'https://claude.ai/api/mcp/auth_callback', codeChallengeMethod: 'S256',
      resource: 'https://vecinomarkets.com/mcp/cuenta', scope: 'cuenta', state: 'xyz',
    }), expect.anything());
    expect(screen.getByText('Ver tus direcciones guardadas y su código postal.')).toBeInTheDocument();
    expect(screen.getByText('No podrá enviar pedidos, pagar por ti ni cambiar tu cuenta.')).toBeInTheDocument();
    expect(screen.getByText('claude.ai')).toBeInTheDocument();
    expect(screen.getByText(/mariana@demo\.com/)).toBeInTheDocument();
    expect(screen.queryByRole('note')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Permitir' }));

    await waitFor(() => expect(leave).toHaveBeenCalledWith('https://claude.ai/api/mcp/auth_callback?code=c0de&state=xyz'));
    expect(api.answer).toHaveBeenCalledWith(expect.objectContaining({ clientId: 'abc', state: 'xyz' }), true);
  });

  it('not allowing it goes back too, and a desktop app is pointed out', async () => {
    const user = userEvent.setup();
    api.check.mockResolvedValue({ ...valid, clientName: null, redirectSite: '127.0.0.1:33418', redirectsToThisDevice: true });
    api.answer.mockResolvedValue({ redirectUrl: 'http://127.0.0.1:33418/callback?error=access_denied&state=xyz' });

    renderPage();

    expect(await screen.findByRole('heading', { name: 'Un asistente quiere usar tu cuenta de TienditApp' })).toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent('Regresa a una aplicación de esta computadora');
    await user.click(screen.getByRole('button', { name: 'No permitir' }));

    await waitFor(() => expect(leave).toHaveBeenCalledWith('http://127.0.0.1:33418/callback?error=access_denied&state=xyz'));
    expect(api.answer).toHaveBeenCalledWith(expect.anything(), false);
  });

  it('says what is wrong with a request it cannot take, and offers how to connect', async () => {
    api.check.mockResolvedValue({ ...valid, valid: false, problem: 'Este asistente no está registrado en TienditApp.' });

    renderPage();

    expect(await screen.findByRole('heading', { name: 'No se puede conectar este asistente' })).toBeInTheDocument();
    expect(screen.getByText('Este asistente no está registrado en TienditApp.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Cómo conectar tu asistente' })).toHaveAttribute('href', '/asistentes');
    expect(screen.queryByRole('button', { name: 'Permitir' })).not.toBeInTheDocument();
  });

  it('asks an account of a business to sign in with a customer one', async () => {
    const user = userEvent.setup();
    const logout = vi.fn().mockResolvedValue(undefined);
    api.check.mockResolvedValue(valid);

    renderPage('TENANT_ADMIN', logout);

    expect(await screen.findByRole('heading', { name: 'Inicia sesión con tu cuenta de cliente' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Permitir' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cambiar de cuenta' }));

    expect(logout).toHaveBeenCalled();
    expect(await screen.findByText('Iniciar sesión')).toBeInTheDocument();
  });

  it('shows why the answer failed, and keeps the page', async () => {
    const user = userEvent.setup();
    api.check.mockResolvedValue(valid);
    api.answer.mockRejectedValue(new Error('Tienes 20 asistentes conectados'));

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Permitir' }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(leave).not.toHaveBeenCalled();
  });
});
