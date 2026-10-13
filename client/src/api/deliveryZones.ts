import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, type Schemas } from './typedClient';

// Configuración › Zonas de entrega only: kept out of queries.ts, which the first visit loads.

export type DeliveryZone = Schemas['DeliveryZoneDto'];
export type SaveDeliveryZone = Schemas['SaveDeliveryZoneRequest'];

export const deliveryZonesKey = ['delivery-zones'] as const;

/** The delivery zones of the business, by name, with their postal codes. */
export function useDeliveryZones(enabled = true) {
  return useQuery({
    queryKey: deliveryZonesKey,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/delivery-zones', { signal }),
    enabled,
  });
}

/** Adds a zone, or saves one with the revision it was read with (an older one answers 409); the list is read again. */
export function useSaveDeliveryZone() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SaveDeliveryZone }) => (id
      ? apiRequest('put', '/api/v1/delivery-zones/{id}', { path: { id }, body })
      : apiRequest('post', '/api/v1/delivery-zones', { body })),
    onSettled: () => client.invalidateQueries({ queryKey: deliveryZonesKey }),
  });
}

/** Deletes a zone with the revision it was read with; the list is read again. */
export function useDeleteDeliveryZone() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, revision }: { id: string; revision: number }) =>
      apiRequest('delete', '/api/v1/delivery-zones/{id}', { path: { id }, query: { revision } }),
    onSettled: () => client.invalidateQueries({ queryKey: deliveryZonesKey }),
  });
}
