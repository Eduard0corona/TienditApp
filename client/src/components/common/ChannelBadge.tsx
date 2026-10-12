import type { OrderChannel } from '../../types';
import { getChannelLabel } from '../../utils/labels';

interface ChannelBadgeProps {
  channel: OrderChannel;
  className?: string;
}

/**
 * Where an order came from, for the orders of a delivery platform ("Rappi"). Neutral on purpose: no platform colors,
 * no `ta-chip-*` (those say the state of an order) and no accent.
 */
export default function ChannelBadge({ channel, className }: ChannelBadgeProps) {
  return (
    <span className={`badge text-bg-light border${className ? ` ${className}` : ''}`}>
      <i className="ri-e-bike-2-line me-1" aria-hidden="true"></i>
      {getChannelLabel(channel)}
    </span>
  );
}
