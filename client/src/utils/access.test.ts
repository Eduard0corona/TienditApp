import { describe, expect, it } from 'vitest';
import { TenantCapability, type UserProfileDto } from '../types';
import { getAccess, getLandingPath } from './access';

const FOOD_WITH_KITCHEN = TenantCapability.Food | TenantCapability.Inventory | TenantCapability.Kitchen;

function user(role: string, permissions: string[] = []): UserProfileDto {
  return {
    id: 'u1',
    email: 'persona@demo.com',
    firstName: 'Persona',
    lastName: 'Demo',
    fullName: 'Persona Demo',
    phoneNumberConfirmed: true,
    emailConfirmed: true,
    role,
    permissions,
    tenantId: 't1',
    isActive: true,
    twoFactorEnabled: false,
    createdAt: '2026-10-05T00:00:00Z',
  };
}

describe('getAccess', () => {
  it('lets a waiter take orders and seat tables but not charge or cook', () => {
    const access = getAccess(user('STAFF', ['ManageOrders']), FOOD_WITH_KITCHEN);

    expect(access.sell).toBe(true);
    expect(access.orders).toBe(true);
    expect(access.tables).toBe(true);
    expect(access.charge).toBe(false);
    expect(access.kitchen).toBe(false);
    expect(access.employees).toBe(false);
  });

  it('follows the permissions of a kitchen account, like the API (no role lists)', () => {
    const cook = getAccess(user('KITCHEN', ['ManageServiceBoard']), FOOD_WITH_KITCHEN);
    const cookWhoSells = getAccess(user('KITCHEN', ['ManageServiceBoard', 'ManageOrders']), FOOD_WITH_KITCHEN);

    expect(cook.kitchen).toBe(true);
    expect(cook.sell).toBe(false);
    expect(cookWhoSells.sell).toBe(true);
  });

  it('lets settle a courier only whoever handles the cash and charges orders, as the API asks', () => {
    expect(getAccess(user('STAFF', ['ManageCash', 'ChargeOrders']), FOOD_WITH_KITCHEN).settleCouriers).toBe(true);
    expect(getAccess(user('STAFF', ['ManageCash']), FOOD_WITH_KITCHEN).settleCouriers).toBe(false);
    expect(getAccess(user('STAFF', ['ManageOrders', 'ChargeOrders']), FOOD_WITH_KITCHEN).settleCouriers).toBe(false);
    expect(getAccess(user('TENANT_ADMIN'), FOOD_WITH_KITCHEN).settleCouriers).toBe(true);
  });

  it('needs the module as well as the permission', () => {
    const withoutKitchen = getAccess(user('STAFF', ['ManageServiceBoard', 'ManageInventory']), TenantCapability.Retail);

    expect(withoutKitchen.kitchen).toBe(false);
    expect(withoutKitchen.inventory).toBe(false);
  });

  it('gives owners and admins every permission but not modules that are off', () => {
    const owner = getAccess(user('TENANT_ADMIN'), FOOD_WITH_KITCHEN);

    expect(owner.employees).toBe(true);
    expect(owner.audit).toBe(true);
    expect(owner.charge).toBe(true);
    expect(owner.refunds).toBe(true);
    expect(owner.appointments).toBe(false);
  });

  it('keeps the team and the activity for owners and admins', () => {
    const everyPermission = getAccess(user('STAFF', ['ManageEmployees', 'ViewReports']), FOOD_WITH_KITCHEN);

    expect(everyPermission.employees).toBe(false);
    expect(everyPermission.audit).toBe(false);
    expect(everyPermission.dashboard).toBe(true);
  });

  it('shows a customer only their orders and the platform admin the console', () => {
    expect(getAccess(user('CUSTOMER'), null).myOrders).toBe(true);
    expect(getAccess(user('CUSTOMER'), null).sell).toBe(false);
    expect(getAccess(user('SUPER_ADMIN'), FOOD_WITH_KITCHEN).platform).toBe(true);
  });
});

describe('getLandingPath', () => {
  it('opens the screen of each job', () => {
    expect(getLandingPath(user('TENANT_ADMIN'), FOOD_WITH_KITCHEN)).toBe('/dashboard');
    expect(getLandingPath(user('STAFF', ['ManageOrders']), FOOD_WITH_KITCHEN)).toBe('/sell');
    expect(getLandingPath(user('KITCHEN', ['ManageServiceBoard']), FOOD_WITH_KITCHEN)).toBe('/kitchen');
    expect(getLandingPath(user('CUSTOMER'), null)).toBe('/mis-pedidos');
    expect(getLandingPath(user('STAFF', []), FOOD_WITH_KITCHEN)).toBe('/profile');
  });
});
