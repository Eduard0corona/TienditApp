import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import PageTitle from './PageTitle';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));

const renderAt = (path: string, element: React.ReactElement) =>
  render(<MemoryRouter initialEntries={[path]}>{element}</MemoryRouter>);

describe('PageTitle', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useAuth).mockReturnValue({ user: null } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({ capabilities: null } as unknown as ReturnType<typeof useTenant>);
  });

  it('shows only the title on a screen of the menu, where a trail would repeat it', () => {
    renderAt('/orders', <PageTitle title="Pedidos" />);

    expect(screen.getByRole('heading', { name: 'Pedidos' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Ruta de navegación' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Inicio' })).not.toBeInTheDocument();
  });

  it('says where a detail screen hangs from and links back to it', () => {
    renderAt('/products/42/edit', <PageTitle title="Editar producto" parent={{ label: 'Productos', to: '/products' }} />);

    const trail = within(screen.getByRole('navigation', { name: 'Ruta de navegación' }));
    expect(trail.getByRole('link', { name: 'Productos' })).toHaveAttribute('href', '/products');
    expect(trail.getByText('Editar producto')).toHaveAttribute('aria-current', 'page');
  });
});
