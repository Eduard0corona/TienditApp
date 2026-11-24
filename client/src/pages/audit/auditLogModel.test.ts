import { describe, expect, it } from 'vitest';
import type { AuditLogDto } from '../../types';
import { describeAuditAction } from './auditLogModel';

const entry = (action: string, metadata?: string): AuditLogDto => ({
  id: 'a1', entityName: 'Tenant', entityId: 'b7', action, performedBy: 'soporte@tienditapp.mx', timestamp: '2026-11-24T18:00:00Z', metadata,
});
const time = (value: string) => `[${value}]`;
const reason = 'El dueño reporta que su corte de caja no cuadra.';

describe('describeAuditAction', () => {
  it('reads what the platform did in the business, and why', () => {
    expect(describeAuditAction(entry('SupportAccess', JSON.stringify({ Method: 'POST', Path: '/api/v1/categories', Reason: reason })), time))
      .toEqual({ label: 'Soporte de la plataforma', detail: `POST /api/v1/categories · Motivo: ${reason}` });
  });

  it('reads when the platform opened its access, why and until when, and when it closed it', () => {
    expect(describeAuditAction(entry('SupportAccessStarted', JSON.stringify({ Reason: reason, ExpiresAt: '2026-11-24T18:30:00+00:00' })), time))
      .toEqual({ label: 'Soporte de la plataforma: abrió un acceso', detail: `Motivo: ${reason} · hasta [2026-11-24T18:30:00+00:00]` });
    expect(describeAuditAction(entry('SupportAccessEnded', JSON.stringify({ Reason: reason })), time))
      .toEqual({ label: 'Soporte de la plataforma: cerró su acceso', detail: `Motivo: ${reason}` });
  });

  it('reads an entry from before the reasons, or one whose details cannot be read, without them', () => {
    expect(describeAuditAction(entry('SupportAccess', JSON.stringify({ Method: 'GET', Path: '/api/v1/orders' })), time))
      .toEqual({ label: 'Soporte de la plataforma', detail: 'GET /api/v1/orders' });
    expect(describeAuditAction(entry('SupportAccess', '{not json'), time)).toEqual({ label: 'Soporte de la plataforma', detail: undefined });
    expect(describeAuditAction(entry('SupportAccessStarted', 'null'), time)).toEqual({ label: 'Soporte de la plataforma: abrió un acceso', detail: undefined });
  });

  it('leaves any other action as recorded', () => {
    expect(describeAuditAction(entry('PermissionsChanged', JSON.stringify({ Reason: reason })), time)).toEqual({ label: 'PermissionsChanged' });
  });
});
