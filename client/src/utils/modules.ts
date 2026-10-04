import { TenantCapability } from '../types';

/** Modules a business turns on (same flags as the API's TenantCapability). */
export const MODULE_OPTIONS: Array<{ value: number; label: string }> = [
  { value: TenantCapability.Retail, label: 'Venta de productos' },
  { value: TenantCapability.Service, label: 'Servicios' },
  { value: TenantCapability.Food, label: 'Alimentos' },
  { value: TenantCapability.Inventory, label: 'Inventario' },
  { value: TenantCapability.Appointments, label: 'Agenda' },
  { value: TenantCapability.Kitchen, label: 'Cocina' },
];

export const SALE_MODULES = TenantCapability.Retail | TenantCapability.Service | TenantCapability.Food;

export function moduleLabels(flags: number): string[] {
  return MODULE_OPTIONS.filter((option) => (flags & option.value) === option.value).map((option) => option.label);
}
