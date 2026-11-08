import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { previewChime } from '../../hooks/useArrivalChime';
import ChimeSettingsButton from './ChimeSettingsButton';

vi.mock('../../hooks/useArrivalChime', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../hooks/useArrivalChime')>()),
  previewChime: vi.fn(),
}));

describe('ChimeSettingsButton (Fase 29Z)', () => {
  beforeEach(() => {
    vi.mocked(previewChime).mockReset();
    window.localStorage.clear();
  });

  it('picks the tone of every sound of the device, ringing it, and its volume, and keeps them', async () => {
    const user = userEvent.setup();
    render(<ChimeSettingsButton />);

    await user.click(screen.getByRole('button', { name: 'Tono y volumen de los sonidos' }));
    const dialog = await screen.findByRole('dialog', { name: 'Tono y volumen' });
    expect(dialog).toHaveTextContent('Para todos los sonidos de este dispositivo: pedidos, solicitudes, cotizaciones y cocina.');
    // Never picked: the sound every one had before.
    expect(screen.getByRole('radio', { name: 'Dos notas' })).toBeChecked();
    expect(screen.getByRole('slider', { name: 'Volumen' })).toHaveValue('50');
    expect(screen.getByRole('slider', { name: 'Volumen' })).toHaveAttribute('aria-valuetext', '50 %');

    await user.click(screen.getByRole('radio', { name: 'Timbre' }));
    expect(screen.getByRole('radio', { name: 'Timbre' })).toBeChecked();
    expect(window.localStorage.getItem('tienditapp:chime-tone')).toBe('doorbell');
    expect(previewChime).toHaveBeenCalledTimes(1);

    fireEvent.change(screen.getByRole('slider', { name: 'Volumen' }), { target: { value: '80' } });
    expect(window.localStorage.getItem('tienditapp:chime-volume')).toBe('80');
    expect(screen.getByRole('slider', { name: 'Volumen' })).toHaveAttribute('aria-valuetext', '80 %');

    await user.click(screen.getByRole('button', { name: 'Probar' }));
    expect(previewChime).toHaveBeenCalledTimes(2);

    await user.click(screen.getByRole('button', { name: 'Listo' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('shows what the device already picked, and what one of its buttons picks the other shows', async () => {
    window.localStorage.setItem('tienditapp:chime-tone', 'bell');
    window.localStorage.setItem('tienditapp:chime-volume', '30');
    const user = userEvent.setup();
    render(<><ChimeSettingsButton /><ChimeSettingsButton /></>);
    const [first, second] = screen.getAllByRole('button', { name: 'Tono y volumen de los sonidos' });
    expect(first).toHaveAttribute('aria-haspopup', 'dialog');

    await user.click(first);
    expect(await screen.findByRole('radio', { name: 'Campana' })).toBeChecked();
    expect(screen.getByRole('slider', { name: 'Volumen' })).toHaveValue('30');
    await user.click(screen.getByRole('radio', { name: 'Suave' }));
    await user.click(screen.getByRole('button', { name: 'Listo' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    await user.click(second);
    expect(await screen.findByRole('radio', { name: 'Suave' })).toBeChecked();
  });
});
