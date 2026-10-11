import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { aiAgentsApi } from '../../api/aiAgentsApi';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { DayOfWeek, TenantCapability, type AiAgentDto, type OpeningIntervalDto, type TenantProfileDto } from '../../types';
import AiAgentSettings from './AiAgentSettings';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));
vi.mock('../../api/aiAgentsApi', () => ({ aiAgentsApi: { getAll: vi.fn() } }));

const agent = {
  id: 'a1',
  name: 'Respuestas 1',
  role: 'Asesor comercial',
  personality: 'Profesional y resolutivo',
  language: 'Espanol',
  tone: 'Amigable',
  businessName: 'Taquería Lupita',
  businessDescription: 'Abrimos de lunes a sábado de 9 a 20.',
  welcomeMessage: 'Hola, ¿en qué te ayudo?',
  fallbackMessage: 'No tengo esa información por ahora.',
  closingMessage: 'Gracias por escribir.',
  askForName: true,
  askForPhone: true,
  askForEmail: false,
  askForAddress: false,
  model: 'gpt-4.1-mini',
  temperature: 0.4,
  maxTokens: 800,
  useConversationMemory: true,
  strictMode: false,
  isActive: true,
  createdAt: '2026-10-01T12:00:00Z',
} as AiAgentDto;

function withHours(openingHours: OpeningIntervalDto[] | null) {
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Taquería Lupita', slug: 'lupita', openingHours } as TenantProfileDto,
    capabilities: TenantCapability.Food,
    isLoading: false,
    refreshTenant: vi.fn(),
  });
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/ai-agent']}>
      <AiAgentSettings />
    </MemoryRouter>,
  );
}

describe('AiAgentSettings', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', firstName: 'Lupita', role: 'OWNER', permissions: [] },
    } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useConfirm).mockReturnValue({ confirm: vi.fn(), prompt: vi.fn() });
    vi.mocked(aiAgentsApi.getAll).mockResolvedValue({ data: [agent] } as Awaited<ReturnType<typeof aiAgentsApi.getAll>>);
  });

  it('says hours questions are answered from the hours of Configuración › Negocio once the business has them', async () => {
    withHours([{ day: DayOfWeek.Monday, opens: '09:00', closes: '20:00' }]);
    renderPage();

    expect(await screen.findByText(/Reconocen palabras completas/)).toHaveTextContent(
      'Las preguntas de horario se contestan con el horario de Configuración › Negocio y tus días especiales.',
    );
    expect(screen.getByLabelText('Descripción del negocio')).toHaveAccessibleDescription(
      'Las preguntas de horario ya no usan este texto: se contestan con tu horario de Configuración › Negocio.',
    );
  });

  it('says hours questions still use the description while the business has no hours', async () => {
    withHours(null);
    renderPage();

    const alert = await screen.findByText(/Reconocen palabras completas/);
    // Without weekly hours the replies tell only the special days of the next two weeks; otherwise the description.
    expect(alert).toHaveTextContent(
      'Sin horario en Configuración › Negocio, las preguntas de horario se contestan con tus días especiales de las próximas dos semanas o, si no hay, con la descripción del negocio.',
    );
    expect(alert).not.toHaveTextContent('se contestan con el horario de Configuración › Negocio');
    expect(screen.getByLabelText('Descripción del negocio')).not.toHaveAccessibleDescription();
  });
});
