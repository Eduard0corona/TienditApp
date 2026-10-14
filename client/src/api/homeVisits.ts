import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, type Schemas } from './typedClient';

// Appointments at the customer's address (Fase 22E): the Agenda and Configuración › Zonas de entrega only, kept out of
// queries.ts, which the first visit loads.

export type VisitQuote = Schemas['VisitQuoteDto'];
export type VisitRoute = Schemas['VisitRouteDto'];
export type VisitRouteStaff = Schemas['VisitRouteStaffDto'];
export type VisitRouteStop = Schemas['VisitRouteStopDto'];
export type HomeVisitSettings = Schemas['HomeVisitSettingsDto'];

export const homeVisitSettingsKey = ['home-visit-settings'] as const;

/** The zone and the charge of a visit to a postal code; the server says why when it does not go there. */
export function quoteVisit(postalCode: string, signal?: AbortSignal) {
  return apiRequest('get', '/api/v1/appointments/visit-quote', { query: { postalCode }, signal });
}

/** The visits of a day (yyyy-MM-dd, in the business's time zone) by team member, optionally of one of them. */
export function useVisitRoute(date: string, staffUserId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: ['visit-route', date, staffUserId ?? ''],
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/appointments/route', { query: { date, staffUserId }, signal }),
    enabled: enabled && date !== '',
  });
}

/** The travel time between a visit and another appointment of the same person (every member reads it). */
export function useHomeVisitSettings(enabled = true) {
  return useQuery({
    queryKey: homeVisitSettingsKey,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/delivery-zones/home-visits', { signal }),
    enabled,
  });
}

/** Sets the travel time (owners and admins); it is read again. */
export function useSaveHomeVisitSettings() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (travelMinutes: number) => apiRequest('put', '/api/v1/delivery-zones/home-visits', { body: { travelMinutes } }),
    onSettled: () => client.invalidateQueries({ queryKey: homeVisitSettingsKey }),
  });
}
