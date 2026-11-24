import type { AuditLogDto } from '../../types';

/** How a row of the activity reads: its action, and what explains it when the entry carries it. */
export interface AuditActionView {
  label: string;
  detail?: string;
}

/** The actions of the platform admin's support accesses to the business (Fase 32G, ADR 0099). */
export const SUPPORT_ACCESS_ACTIONS = {
  request: 'SupportAccess',
  started: 'SupportAccessStarted',
  ended: 'SupportAccessEnded',
} as const;

function readDetails(metadata: string | undefined): Record<string, unknown> {
  if (!metadata) return {};
  try {
    const details: unknown = JSON.parse(metadata);
    return details !== null && typeof details === 'object' ? (details as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}

const text = (value: unknown) => (typeof value === 'string' && value.trim() ? value.trim() : undefined);

const joined = (parts: (string | undefined)[]) => parts.filter(Boolean).join(' · ') || undefined;

/**
 * The support accesses read as such: who entered the business is the platform, why (its reason) and what it did (the
 * method and path of each request; an entry from before Fase 32G has no reason). Any other action reads as recorded.
 */
export function describeAuditAction(item: AuditLogDto, formatTime: (value: string) => string): AuditActionView {
  if (!Object.values(SUPPORT_ACCESS_ACTIONS).includes(item.action as never)) return { label: item.action };

  const details = readDetails(item.metadata);
  const reason = text(details.Reason);
  const because = reason ? `Motivo: ${reason}` : undefined;
  switch (item.action) {
    case SUPPORT_ACCESS_ACTIONS.started: {
      const until = text(details.ExpiresAt);
      return { label: 'Soporte de la plataforma: abrió un acceso', detail: joined([because, until && `hasta ${formatTime(until)}`]) };
    }
    case SUPPORT_ACCESS_ACTIONS.ended:
      return { label: 'Soporte de la plataforma: cerró su acceso', detail: because };
    default: {
      const method = text(details.Method);
      const path = text(details.Path);
      return { label: 'Soporte de la plataforma', detail: joined([method && path ? `${method} ${path}` : undefined, because]) };
    }
  }
}
