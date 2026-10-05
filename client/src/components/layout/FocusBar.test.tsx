import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import FocusBar from './FocusBar';

describe('FocusBar', () => {
  it('says where you are and lets you leave focus mode', async () => {
    const onExit = vi.fn();
    render(<FocusBar area="kitchen" businessName="Fonda Lupita" onExit={onExit} />);

    expect(screen.getByText('Cocina')).toBeInTheDocument();
    expect(screen.getByText('Fonda Lupita')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Salir del modo enfocado' }));

    expect(onExit).toHaveBeenCalledOnce();
  });
});
