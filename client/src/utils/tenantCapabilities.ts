import { TenantCapability } from '../types';

export function hasCapability(capabilities: number | null | undefined, capability: number): boolean {
  if (capabilities == null) return false;
  return (capabilities & capability) === capability;
}

export function hasAnyCapability(capabilities: number | null | undefined, required: number): boolean {
  if (capabilities == null) return false;
  return (capabilities & required) !== 0;
}

export const COMMERCE_CAPABILITIES =
  TenantCapability.Retail | TenantCapability.Service | TenantCapability.Food;
