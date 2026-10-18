import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import CopyTrackingLink from './CopyTrackingLink';

const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';

describe('CopyTrackingLink', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('copies the whole link, with the fragment', async () => {
    const user = userEvent.setup();
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    render(<CopyTrackingLink token={LINK} />);

    await user.click(screen.getByRole('button', { name: 'Copiar enlace de seguimiento' }));

    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/seguimiento#${LINK}`);
    expect(screen.getByRole('button', { name: 'Enlace copiado' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Enlace de seguimiento copiado');
  });

  it('copies the link of an appointment booked online (Fase 24D)', async () => {
    const user = userEvent.setup();
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    render(<CopyTrackingLink token={LINK} path="/cita" />);

    await user.click(screen.getByRole('button', { name: 'Copiar enlace de tu cita' }));

    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/cita#${LINK}`);
    expect(screen.getByRole('status')).toHaveTextContent('Enlace de tu cita copiado');
  });

  it('shows the link to copy by hand where the browser does not let the page copy', async () => {
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('NotAllowedError'));
    render(<CopyTrackingLink token={LINK} />);

    await user.click(screen.getByRole('button', { name: 'Copiar enlace de seguimiento' }));

    expect(await screen.findByText(`Cópialo de aquí: ${window.location.origin}/seguimiento#${LINK}`)).toBeInTheDocument();
  });
});
