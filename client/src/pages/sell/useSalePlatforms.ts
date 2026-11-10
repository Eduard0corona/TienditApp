import { useMemo } from 'react';
import { useDeliveryPlatformChannels } from '../../api/queries';
import type { OrderChannel } from '../../types';
import { isDeliveryPlatform, type DeliveryPlatform } from '../../utils/deliveryPlatforms';

/**
 * Rappi, Uber Eats and DiDi Food as the business set them up: only the active ones are offered. While the list is not
 * known yet (without connection it waits), the platform of a sale brought back stays offered: its order is kept as it is,
 * and the cashier can still take Vender back to the counter.
 */
export function useSalePlatforms(canSell: boolean, channel: OrderChannel) {
  const platformChannels = useDeliveryPlatformChannels(canSell);
  const active = useMemo<DeliveryPlatform[]>(
    () => (platformChannels.data ?? []).flatMap((row) => (row.isActive && isDeliveryPlatform(row.channel) ? [row.channel] : [])),
    [platformChannels.data],
  );
  const offered = useMemo<DeliveryPlatform[]>(
    () => (platformChannels.isPending && active.length === 0 && isDeliveryPlatform(channel) ? [channel] : active),
    [active, channel, platformChannels.isPending],
  );
  // A sale brought back from the device may come from a platform the business turned off since (or whose list did not
  // load): once that is known, it goes back to the counter.
  const turnedOff = !platformChannels.isPending && isDeliveryPlatform(channel) && !active.includes(channel);
  return { active, offered, turnedOff };
}
