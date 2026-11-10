import { useMemo } from 'react';
import { useDeliveryZones } from '../../api/deliveryZones';
import { findDeliveryZone } from '../../utils/homeDelivery';
import type { ZonesState } from './saleChecks';
import type { SaleDelivery } from './sellModel';

/**
 * To the customer's address (Fase 22C), with the active zones of the business: never for a tab, a platform's order or the
 * sale of an appointment (`possible`). A delivery typed, or brought back with the sale, stays on screen while the zones
 * load, fail or were turned off, or with services in the ticket: it says why instead of sending the sale without its address.
 */
export function useSaleDelivery(readZones: boolean, possible: boolean, typed: SaleDelivery | null, hasServices: boolean) {
  const query = useDeliveryZones(readZones);
  const activeZones = useMemo(() => (query.data ?? []).filter((zone) => zone.isActive), [query.data]);
  const delivery = possible ? typed : null;
  const zones: ZonesState = { pending: query.isPending, failed: query.isError, active: activeZones.length };
  return {
    zones,
    delivery,
    deliveryOffered: possible && ((!hasServices && activeZones.length > 0) || delivery !== null),
    deliveryZone: delivery ? findDeliveryZone(activeZones, delivery.postalCode) : null,
  };
}
