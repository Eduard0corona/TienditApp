import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { OrderChannel } from '../../types';
import ChannelBadge from './ChannelBadge';

describe('ChannelBadge', () => {
  it.each([
    [OrderChannel.Rappi, 'Rappi'],
    [OrderChannel.UberEats, 'Uber Eats'],
    [OrderChannel.DidiFood, 'DiDi Food'],
  ])('names the platform %s with the courier icon', (channel, name) => {
    const { container } = render(<ChannelBadge channel={channel} />);

    expect(screen.getByText(name)).toBeInTheDocument();
    expect(container.querySelector('.ri-e-bike-2-line')).toHaveAttribute('aria-hidden', 'true');
  });

  it('stays neutral: no state chip, no accent and no platform colors', () => {
    const { container } = render(<ChannelBadge channel={OrderChannel.Rappi} className="mt-1" />);

    const badge = container.firstElementChild as HTMLElement;
    expect([...badge.classList]).toEqual(['badge', 'text-bg-light', 'border', 'mt-1']);
    expect(badge.className).not.toMatch(/ta-chip|primary|success|warning|danger|info/);
    expect(badge).not.toHaveAttribute('style');
  });
});
