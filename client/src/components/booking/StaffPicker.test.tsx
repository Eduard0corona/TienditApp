import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import StaffPicker from './StaffPicker';

describe('StaffPicker', () => {
  it('shows the photo the business chose for a person, or their initial, and says who is chosen', async () => {
    const user = userEvent.setup();
    const onChoose = vi.fn();
    const { container } = render(
      <StaffPicker
        people={[{ id: 'p1', name: 'Ana L.', photoUrl: '/uploads/staff/t1/ana_xs.webp' }, { id: 'p2', name: 'Luis M.', photoUrl: null }]}
        personId="p2"
        onChoose={onChoose}
        label="Quién te atiende"
      />,
    );

    const ana = screen.getByRole('button', { name: 'Ana L.' });
    expect(ana.querySelector('img')).toHaveAttribute('src', '/uploads/staff/t1/ana_xs.webp');
    // Decoration: the name is next to it.
    expect(ana.querySelector('img')).toHaveAttribute('alt', '');
    expect(screen.getByRole('button', { name: 'Luis M.' }).querySelector('img')).toBeNull();
    expect(container.querySelectorAll('img')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Luis M.' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(ana);
    expect(onChoose).toHaveBeenCalledWith('p1');
  });
});
