import { describe, expect, it } from 'vitest';
import { isOwnerRole } from './teamRoles';

describe('teamRoles', () => {
  it('knows the owner however the role comes: in capitals from the session, as the enum from the team (Fase 29Q)', () => {
    expect(isOwnerRole('OWNER')).toBe(true);
    expect(isOwnerRole('Owner')).toBe(true);
    expect(isOwnerRole('ADMIN')).toBe(false);
    expect(isOwnerRole('TenantAdmin')).toBe(false);
    expect(isOwnerRole(undefined)).toBe(false);
  });
});
