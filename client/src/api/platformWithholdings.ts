import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { OrderChannel } from '../types';
import { apiRequest, type Schemas } from './typedClient';

// Reportes › Liquidaciones › Retenciones only (Fase 22J): kept out of queries.ts, which the first visit loads.

export type PlatformWithholdings = Schemas['PlatformWithholdingsDto'];
export type PlatformWithholdingPeriod = Schemas['PlatformWithholdingPeriodDto'];
export type PlatformWithholdingCertificate = Schemas['PlatformWithholdingCertificateDto'];

/** Cache keys of the withholdings: an upload or a removal reads every year of every platform again. */
export const withholdingKeys = {
  all: ['platform-withholdings'] as const,
  year: (channel: OrderChannel, year: number) => ['platform-withholdings', channel, year] as const,
};

/** A platform's withholdings in a year, month by month, with its CFDI de retenciones of the year. */
export function usePlatformWithholdings(channel: OrderChannel, year: number) {
  return useQuery({
    queryKey: withholdingKeys.year(channel, year),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/platform-withholdings', { query: { channel, year }, signal }),
    // Another platform or year keeps this one on screen until it arrives.
    placeholderData: keepPreviousData,
  });
}

/**
 * Registers the XML of a CFDI de retenciones (owners and admins). One call per file: each says whether it went in, and
 * the reconciliation is read again once, by whoever uploads the batch, after the last one.
 */
export function useRegisterPlatformWithholdingCertificate() {
  return useMutation({
    mutationFn: ({ channel, xml }: { channel: OrderChannel; xml: string }) =>
      apiRequest('post', '/api/v1/platform-withholdings', { body: { channel, xml } }),
  });
}

/** Takes a CFDI out of the reconciliation with a reason (owners and admins); it stays in the list as removed. */
export function useRemovePlatformWithholdingCertificate() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      apiRequest('post', '/api/v1/platform-withholdings/{id}/remove', { path: { id }, body: { reason } }),
    onSettled: () => client.invalidateQueries({ queryKey: withholdingKeys.all }),
  });
}

/** Reads every reconciliation again: after a batch of uploads, whatever came of each. */
export function useRefreshPlatformWithholdings() {
  const client = useQueryClient();
  return () => client.invalidateQueries({ queryKey: withholdingKeys.all });
}
