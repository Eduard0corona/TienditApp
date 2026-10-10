import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import NewVersionNotice from './NewVersionNotice';

describe('NewVersionNotice', () => {
  it('says there is a new version and reloads on request', () => {
    const reload = vi.fn();
    vi.spyOn(window, 'location', 'get').mockReturnValue({ ...window.location, reload });

    render(<NewVersionNotice onClose={vi.fn()} />);

    expect(screen.getByRole('status')).toHaveTextContent('Hay una versión nueva de TienditApp');
    fireEvent.click(screen.getByRole('button', { name: 'Actualizar' }));
    expect(reload).toHaveBeenCalled();
  });

  it('offers no reload without connection: the till keeps selling offline', () => {
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);

    render(<NewVersionNotice onClose={vi.fn()} />);

    expect(screen.getByRole('status')).toHaveTextContent('Actualiza cuando vuelva la conexión.');
    expect(screen.queryByRole('button', { name: 'Actualizar' })).not.toBeInTheDocument();
  });

  it('can be closed', () => {
    const onClose = vi.fn();
    render(<NewVersionNotice onClose={onClose} />);

    fireEvent.click(screen.getByRole('button', { name: 'Cerrar aviso' }));

    expect(onClose).toHaveBeenCalled();
  });
});
