import { useId } from 'react';
import { useScrollFade } from '../../hooks/useScrollFade';
import { OrderChannel } from '../../types';
import { PLATFORM_ONLY_PRODUCTS_MESSAGE, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { ORDER_CHANNEL_LABELS } from '../../utils/labels';

interface PlatformOriginPickerProps {
  /** The platforms active in the business, in the order of the enum: only those are offered. */
  platforms: DeliveryPlatform[];
  /** The counter or the platform chosen. */
  value: OrderChannel;
  onChange: (channel: OrderChannel) => void;
  /** Services in the ticket: an order of a platform takes only products. */
  hasServices: boolean;
  disabled: boolean;
}

/**
 * Where the order on screen came from: the counter, or the app of a delivery platform the business has active. Chips
 * like the ones of the charge, in a row that leaves room for the focus ring.
 */
export default function PlatformOriginPicker({ platforms, value, onChange, hasServices, disabled }: PlatformOriginPickerProps) {
  const labelId = useId();
  const noteId = useId();
  const { ref, fadeClass } = useScrollFade<HTMLDivElement>(platforms.join());
  const options: OrderChannel[] = [OrderChannel.Counter, ...platforms];

  return (
    <div className="mb-1">
      <div id={labelId} className="small fw-semibold mb-1">¿De dónde viene el pedido?</div>
      <div
        ref={ref}
        className={`sell-chips ta-scroll-fade ${fadeClass}`}
        role="radiogroup"
        aria-labelledby={labelId}
        aria-describedby={hasServices ? noteId : undefined}
      >
        {options.map((channel) => (
          <button
            key={channel}
            type="button"
            role="radio"
            aria-checked={value === channel}
            className={`btn btn-touch ${value === channel ? 'btn-primary' : 'btn-outline-secondary'}`}
            onClick={() => onChange(channel)}
            disabled={disabled || (hasServices && channel !== OrderChannel.Counter)}
          >
            {ORDER_CHANNEL_LABELS[channel]}
          </button>
        ))}
      </div>
      {hasServices && <p id={noteId} className="small text-muted mb-2">{PLATFORM_ONLY_PRODUCTS_MESSAGE}</p>}
    </div>
  );
}
