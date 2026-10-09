import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { BusinessTheme, type TenantProfileDto } from '../../types';
import { getBusinessTheme } from '../../utils/businessThemes';
import { applyBusinessTheme, previewBusinessTheme } from '../../utils/theme';
import ThemeCard from './ThemeCard';

vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const accent = () => document.documentElement.style.getPropertyValue('--tt-accent');

function withTenant(theme: BusinessTheme, refreshTenant = vi.fn()) {
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Taquería Lupita', slug: 'lupita', theme } as TenantProfileDto,
    capabilities: null,
    isLoading: false,
    refreshTenant,
  });
  return refreshTenant;
}

describe('ThemeCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    previewBusinessTheme(undefined);
    applyBusinessTheme(BusinessTheme.Nopal);
  });

  it('shows every theme with the one the business has', () => {
    withTenant(BusinessTheme.Talavera);
    render(<ThemeCard />);

    expect(screen.getAllByRole('radio')).toHaveLength(8);
    expect(screen.getByRole('radio', { name: /Talavera/ })).toBeChecked();
    expect(screen.getByText('Actual')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Guardar/ })).not.toBeInTheDocument();
  });

  it('previews a theme on the whole app and saves it for the business', async () => {
    const refreshTenant = withTenant(BusinessTheme.Nopal, vi.fn().mockImplementation(async () => {
      // What TenantProvider does with the saved business.
      applyBusinessTheme(BusinessTheme.RosaMexicano);
    }));
    vi.mocked(apiRequest).mockResolvedValue({ theme: BusinessTheme.RosaMexicano } as never);
    render(<ThemeCard />);

    await userEvent.click(screen.getByRole('radio', { name: /Rosa mexicano/ }));
    expect(accent()).toBe(getBusinessTheme(BusinessTheme.RosaMexicano).light.accent);

    await userEvent.click(screen.getByRole('button', { name: 'Guardar tema Rosa mexicano' }));

    expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/auth/my-tenant/theme', { body: { theme: BusinessTheme.RosaMexicano } });
    expect(refreshTenant).toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledWith(expect.stringContaining('Rosa mexicano'));
    expect(accent()).toBe(getBusinessTheme(BusinessTheme.RosaMexicano).light.accent);
  });

  it('goes back to the saved theme when discarded or when leaving the page', async () => {
    withTenant(BusinessTheme.Nopal);
    const { unmount } = render(<ThemeCard />);
    const nopal = getBusinessTheme(BusinessTheme.Nopal).light.accent;

    await userEvent.click(screen.getByRole('radio', { name: /Grafito/ }));
    expect(accent()).toBe(getBusinessTheme(BusinessTheme.Grafito).light.accent);
    await userEvent.click(screen.getByRole('button', { name: 'Descartar' }));
    expect(accent()).toBe(nopal);
    expect(screen.getByRole('radio', { name: /Nopal/ })).toBeChecked();

    await userEvent.click(screen.getByRole('radio', { name: /Jacaranda/ }));
    unmount();
    expect(accent()).toBe(nopal);
    expect(apiRequest).not.toHaveBeenCalled();
  });

  it('keeps the preview and says so when saving fails', async () => {
    withTenant(BusinessTheme.Nopal);
    vi.mocked(apiRequest).mockRejectedValue(new Error('network'));
    render(<ThemeCard />);

    await userEvent.click(screen.getByRole('radio', { name: /Talavera/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Guardar tema Talavera' }));

    expect(toast.error).toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Guardar tema Talavera' })).toBeEnabled();
    expect(accent()).toBe(getBusinessTheme(BusinessTheme.Talavera).light.accent);
  });
});
