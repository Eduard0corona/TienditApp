import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useAuth } from '../../hooks/useAuth';
import BusinessLandingPage from './BusinessLandingPage';
import { LANDING_TITLE } from './landingContent';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../components/layout/ThemeToggle', () => ({ default: () => null }));

function signedIn(isAuthenticated: boolean) {
  vi.mocked(useAuth).mockReturnValue({ isAuthenticated, isLoading: false } as unknown as ReturnType<typeof useAuth>);
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/negocios']}>
      <BusinessLandingPage />
    </MemoryRouter>,
  );
}

describe('BusinessLandingPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('presents TienditApp and sends every call to action to the business registration', () => {
    signedIn(false);
    renderPage();

    expect(screen.getByRole('heading', { level: 1, name: 'Vende, cobra y factura desde una sola app' })).toBeInTheDocument();
    expect(document.title).toBe(LANDING_TITLE);
    expect(screen.getByRole('link', { name: 'Empieza gratis' })).toHaveAttribute('href', '/register?cuenta=negocio');
    const signUps = screen.getAllByRole('link', { name: /^Crea tu cuenta gratis/ });
    expect(signUps.length).toBeGreaterThan(1);
    for (const link of signUps) expect(link).toHaveAttribute('href', '/register?cuenta=negocio');
    expect(screen.getAllByRole('link', { name: 'Entrar' })[0]).toHaveAttribute('href', '/login');
  });

  it('takes the menu of sections to each one', () => {
    signedIn(false);
    renderPage();

    const sections = screen.getByRole('navigation', { name: 'Secciones' });
    for (const link of within(sections).getAllByRole('link')) {
      const target = link.getAttribute('href')!.slice(1);
      expect(document.getElementById(target), target).not.toBeNull();
    }
  });

  it('shows what each type of business gets, from a click or the arrows', async () => {
    signedIn(false);
    renderPage();
    const tabs = screen.getByRole('tablist', { name: 'Tipo de negocio' });

    expect(within(tabs).getByRole('tab', { name: 'Comida' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel')).toHaveTextContent('Pantalla de cocina');

    await userEvent.click(within(tabs).getByRole('tab', { name: 'Servicios' }));
    expect(screen.getByRole('tabpanel', { name: 'Servicios' })).toHaveTextContent('Citas en línea');

    await userEvent.keyboard('{ArrowRight}');
    const food = within(tabs).getByRole('tab', { name: 'Comida' });
    expect(food).toHaveAttribute('aria-selected', 'true');
    expect(food).toHaveFocus();

    await userEvent.keyboard('{End}');
    expect(within(tabs).getByRole('tab', { name: 'Servicios' })).toHaveFocus();
    await userEvent.keyboard('{ArrowLeft}');
    expect(screen.getByRole('tabpanel', { name: 'Comercio' })).toHaveTextContent('Fiado con límite');
  });

  it('answers the questions of a business without promising what the app does not do', () => {
    signedIn(false);
    renderPage();

    const delivery = screen.getByText('¿Se conecta con Rappi, Uber Eats y DiDi Food?').closest('details')!;
    expect(delivery).toHaveTextContent('Los pedidos no llegan solos desde esas apps.');
    expect(screen.getByText('¿Puedo facturar?')).toBeInTheDocument();
  });

  it('takes whoever already has a session to the app instead of the registration', () => {
    signedIn(true);
    renderPage();

    expect(screen.getByRole('link', { name: 'Abrir TienditApp' })).toHaveAttribute('href', '/');
    expect(screen.queryByRole('link', { name: 'Empieza gratis' })).not.toBeInTheDocument();
  });
});
