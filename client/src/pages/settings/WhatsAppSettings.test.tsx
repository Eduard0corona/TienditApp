import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { tenantApi } from '../../api/tenantApi';
import WhatsAppSettings from './WhatsAppSettings';

vi.mock('../../api/tenantApi', () => ({
  tenantApi: { getWhatsAppConfig: vi.fn(), updateWhatsAppConfig: vi.fn(), sendWhatsAppMessage: vi.fn() },
}));
vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));

const api = vi.mocked(tenantApi);
const VERIFY_TOKEN = '3f9c2a7be1d04c58a6e2b9d1';

describe('WhatsAppSettings', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows the webhook data before the first save, and the verify token once it is saved', async () => {
    api.getWhatsAppConfig.mockResolvedValue({ data: { tenantId: 't1', isEnabled: false, hasAccessToken: false } } as never);
    api.updateWhatsAppConfig.mockResolvedValue({
      data: { id: 'w1', tenantId: 't1', isEnabled: false, hasAccessToken: false, verifyToken: VERIFY_TOKEN },
    } as never);
    render(<WhatsAppSettings />);

    // Step 6 sends the owner to this section, so it is there before the token exists.
    expect(await screen.findByLabelText('URL de devolución de llamada (Callback URL)'))
      .toHaveValue(`${window.location.origin}/api/v1/whatsapp/webhook`);
    expect(screen.getByLabelText('Token de verificación (Verify Token)')).toHaveValue('');
    expect(screen.getByPlaceholderText('Se crea al guardar la configuración')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Copiar el token de verificación' })).not.toBeInTheDocument();

    // Saving without enabling the sending is enough to create it.
    await userEvent.click(screen.getByRole('button', { name: 'Guardar configuración' }));

    expect(api.updateWhatsAppConfig).toHaveBeenCalledWith(expect.objectContaining({ isEnabled: false }));
    expect(await screen.findByDisplayValue(VERIFY_TOKEN)).toHaveAccessibleName('Token de verificación (Verify Token)');
    expect(screen.getByRole('button', { name: 'Copiar el token de verificación' })).toBeInTheDocument();
  });
});
