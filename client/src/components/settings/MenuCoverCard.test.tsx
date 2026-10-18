import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { tenantApi } from '../../api/tenantApi';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import type { TenantProfileDto } from '../../types';
import MenuCoverCard from './MenuCoverCard';

vi.mock('../../api/tenantApi', () => ({ tenantApi: { uploadCover: vi.fn(), deleteCover: vi.fn() } }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));

const api = vi.mocked(tenantApi);
const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const refreshTenant = vi.fn();
const covers = {
  original: '/uploads/covers/t1/p_original.webp', large: '/uploads/covers/t1/p_lg.webp', medium: '/uploads/covers/t1/p_md.webp',
  small: '/uploads/covers/t1/p_sm.webp', thumbnail: '/uploads/covers/t1/p_xs.webp',
};

function withTenant(coverImages: TenantProfileDto['coverImages']) {
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Café Luna', slug: 'cafe-luna', coverImages } as unknown as TenantProfileDto,
    capabilities: 0,
    isLoading: false,
    refreshTenant,
  });
}

describe('MenuCoverCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
  });

  it('uploads a cover and shows how it looks over the menu with the logo', async () => {
    withTenant(null);
    api.uploadCover.mockResolvedValue({ data: covers } as never);
    const { container } = render(<MenuCoverCard />);

    expect(screen.getByRole('region', { name: 'Portada del menú' })).toBeInTheDocument();
    // Without one, the colors of the business and no button to remove it.
    expect(container.querySelector('.mc-cover img')).toBeNull();
    expect(screen.queryByRole('button', { name: /Quitar portada/ })).not.toBeInTheDocument();

    const file = new File(['img'], 'portada.jpg', { type: 'image/jpeg' });
    await userEvent.upload(screen.getByLabelText('Foto de portada'), file);

    await waitFor(() => expect(api.uploadCover).toHaveBeenCalledWith(file));
    expect(refreshTenant).toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledWith('Portada actualizada: ya se ve en tu menú.');
  });

  it('changes or removes the cover it has, and says why when it cannot', async () => {
    withTenant(covers);
    api.deleteCover.mockRejectedValueOnce(new Error('Network Error')).mockResolvedValue({} as never);
    const { container } = render(<MenuCoverCard />);

    expect(container.querySelector('.mc-cover img')).toHaveAttribute('src', '/uploads/covers/t1/p_lg.webp');
    expect(screen.getByRole('button', { name: /Cambiar portada/ })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /Quitar portada/ }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(expect.stringMatching(/./)));
    expect(refreshTenant).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: /Quitar portada/ }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Portada quitada: tu menú lleva los colores de tu negocio.'));
    expect(refreshTenant).toHaveBeenCalled();
  });
});
