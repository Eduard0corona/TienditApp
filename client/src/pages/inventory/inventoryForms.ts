import {
  InventoryMovementReason,
  InventoryMovementType,
  type CreateInventoryLocationRequest,
  type CreateInventoryMovementRequest,
  type CreateSupplyItemRequest,
  type InventoryLocationDto,
  type ProductRecipeDto,
  type SupplyItemDto,
  type UnitType,
  type UpsertProductRecipeRequest,
} from '../../types';
import { businessDayStart } from '../../utils/dates';

/** What a form gives: the request for the API, or the first problem to show. */
export type FormResult<T> = { request: T; error?: undefined } | { request?: undefined; error: string };

export interface SupplyFormState {
  name: string;
  code: string;
  sku: string;
  barcode: string;
  description: string;
  brand: string;
  model: string;
  categoryName: string;
  unit: UnitType;
  defaultCost: string;
  minStock: string;
  maxStock: string;
  reorderPoint: string;
  trackBatches: boolean;
  trackExpiration: boolean;
  isPerishable: boolean;
  isCritical: boolean;
  taxRate: string;
  notes: string;
}

export interface LocationFormState {
  name: string;
  code: string;
  description: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  state: string;
  country: string;
  postalCode: string;
  contactName: string;
  contactPhone: string;
  isDefault: boolean;
  /** Empty: a warehouse that serves every branch. */
  branchId: string;
}

export interface MovementLineFormState {
  supplyItemId: string;
  inventoryLocationId: string;
  quantity: string;
  unitCost: string;
  lotNumber: string;
  expirationDate: string;
  notes: string;
}

export interface MovementFormState {
  movementType: InventoryMovementType;
  movementReason: InventoryMovementReason;
  movementDate: string;
  referenceNumber: string;
  externalReference: string;
  counterpartyName: string;
  documentNumber: string;
  notes: string;
  lines: MovementLineFormState[];
}

export interface RecipeLineFormState {
  clientId: string;
  requirementId?: string;
  supplyItemId: string;
  inventoryLocationId: string;
  quantity: string;
  wastePercent: string;
  notes: string;
}

const optional = (value: string) => value.trim() || undefined;

export const buildEmptySupplyForm = (): SupplyFormState => ({
  name: '',
  code: '',
  sku: '',
  barcode: '',
  description: '',
  brand: '',
  model: '',
  categoryName: '',
  unit: 0,
  defaultCost: '0',
  minStock: '0',
  maxStock: '',
  reorderPoint: '0',
  trackBatches: false,
  trackExpiration: false,
  isPerishable: false,
  isCritical: false,
  taxRate: '',
  notes: '',
});

export const toSupplyForm = (supply: SupplyItemDto): SupplyFormState => ({
  name: supply.name,
  code: supply.code ?? '',
  sku: supply.sku ?? '',
  barcode: supply.barcode ?? '',
  description: supply.description ?? '',
  brand: supply.brand ?? '',
  model: supply.model ?? '',
  categoryName: supply.categoryName ?? '',
  unit: supply.unit,
  defaultCost: supply.defaultCost.toString(),
  minStock: supply.minStock.toString(),
  maxStock: supply.maxStock?.toString() ?? '',
  reorderPoint: supply.reorderPoint.toString(),
  trackBatches: supply.trackBatches,
  trackExpiration: supply.trackExpiration,
  isPerishable: supply.isPerishable,
  isCritical: supply.isCritical,
  taxRate: supply.taxRate?.toString() ?? '',
  notes: supply.notes ?? '',
});

export function toSupplyRequest(form: SupplyFormState): FormResult<CreateSupplyItemRequest> {
  const name = form.name.trim();
  if (!name) return { error: 'El nombre del insumo es obligatorio.' };

  const defaultCost = Number(form.defaultCost || '0');
  const minStock = Number(form.minStock || '0');
  const reorderPoint = Number(form.reorderPoint || '0');
  const maxStock = form.maxStock === '' ? undefined : Number(form.maxStock);
  const taxRate = form.taxRate === '' ? undefined : Number(form.taxRate);

  if (Number.isNaN(defaultCost) || defaultCost < 0) return { error: 'Costo por defecto inválido.' };
  if (Number.isNaN(minStock) || minStock < 0) return { error: 'Stock mínimo inválido.' };
  if (Number.isNaN(reorderPoint) || reorderPoint < 0) return { error: 'Punto de reorden inválido.' };
  if (maxStock !== undefined && (Number.isNaN(maxStock) || maxStock < minStock))
    return { error: 'El stock máximo debe ser mayor o igual al stock mínimo.' };
  if (taxRate !== undefined && (Number.isNaN(taxRate) || taxRate < 0 || taxRate > 100))
    return { error: 'La tasa de impuesto debe estar entre 0 y 100.' };

  return {
    request: {
      name,
      code: optional(form.code),
      sku: optional(form.sku),
      barcode: optional(form.barcode),
      description: optional(form.description),
      brand: optional(form.brand),
      model: optional(form.model),
      categoryName: optional(form.categoryName),
      unit: Number(form.unit) as UnitType,
      defaultCost,
      minStock,
      maxStock,
      reorderPoint,
      trackBatches: form.trackBatches,
      trackExpiration: form.trackExpiration,
      isPerishable: form.isPerishable,
      isCritical: form.isCritical,
      taxRate,
      notes: optional(form.notes),
    },
  };
}

export const buildEmptyLocationForm = (): LocationFormState => ({
  name: '',
  code: '',
  description: '',
  addressLine1: '',
  addressLine2: '',
  city: '',
  state: '',
  country: '',
  postalCode: '',
  contactName: '',
  contactPhone: '',
  isDefault: false,
  branchId: '',
});

export const toLocationForm = (location: InventoryLocationDto): LocationFormState => ({
  name: location.name,
  code: location.code,
  description: location.description ?? '',
  addressLine1: location.addressLine1 ?? '',
  addressLine2: location.addressLine2 ?? '',
  city: location.city ?? '',
  state: location.state ?? '',
  country: location.country ?? '',
  postalCode: location.postalCode ?? '',
  contactName: location.contactName ?? '',
  contactPhone: location.contactPhone ?? '',
  isDefault: location.isDefault,
  branchId: location.branchId ?? '',
});

export function toLocationRequest(form: LocationFormState): FormResult<CreateInventoryLocationRequest> {
  const name = form.name.trim();
  const code = form.code.trim();
  if (!name || !code) return { error: 'Nombre y código de ubicación son obligatorios.' };

  return {
    request: {
      name,
      code,
      description: optional(form.description),
      addressLine1: optional(form.addressLine1),
      addressLine2: optional(form.addressLine2),
      city: optional(form.city),
      state: optional(form.state),
      country: optional(form.country),
      postalCode: optional(form.postalCode),
      contactName: optional(form.contactName),
      contactPhone: optional(form.contactPhone),
      isDefault: form.isDefault,
      branchId: form.branchId || undefined,
    },
  };
}

export const buildEmptyMovementLineForm = (): MovementLineFormState => ({
  supplyItemId: '',
  inventoryLocationId: '',
  quantity: '1',
  unitCost: '',
  lotNumber: '',
  expirationDate: '',
  notes: '',
});

/** The value of a datetime-local input: the device's own date and time, to the minute. */
export const toDateTimeLocalValue = (value: Date) => {
  const year = value.getFullYear();
  const month = `${value.getMonth() + 1}`.padStart(2, '0');
  const day = `${value.getDate()}`.padStart(2, '0');
  const hour = `${value.getHours()}`.padStart(2, '0');
  const minute = `${value.getMinutes()}`.padStart(2, '0');
  return `${year}-${month}-${day}T${hour}:${minute}`;
};

export const buildEmptyMovementForm = (): MovementFormState => ({
  movementType: InventoryMovementType.Entry,
  movementReason: InventoryMovementReason.Purchase,
  movementDate: toDateTimeLocalValue(new Date()),
  referenceNumber: '',
  externalReference: '',
  counterpartyName: '',
  documentNumber: '',
  notes: '',
  lines: [buildEmptyMovementLineForm()],
});

export function toMovementRequest(form: MovementFormState): FormResult<CreateInventoryMovementRequest> {
  if (form.lines.length === 0) return { error: 'Debes agregar al menos un detalle.' };

  for (let index = 0; index < form.lines.length; index += 1) {
    const line = form.lines[index];
    const quantity = Number(line.quantity);
    if (!line.supplyItemId || !line.inventoryLocationId)
      return { error: `Línea ${index + 1}: insumo y ubicación son obligatorios.` };
    if (Number.isNaN(quantity) || quantity <= 0) return { error: `Línea ${index + 1}: cantidad inválida.` };
    if (line.unitCost && (Number.isNaN(Number(line.unitCost)) || Number(line.unitCost) < 0))
      return { error: `Línea ${index + 1}: costo unitario inválido.` };
  }

  return {
    request: {
      movementType: form.movementType,
      movementReason: form.movementReason,
      movementDate: form.movementDate ? new Date(form.movementDate).toISOString() : undefined,
      referenceNumber: optional(form.referenceNumber),
      externalReference: optional(form.externalReference),
      counterpartyName: optional(form.counterpartyName),
      documentNumber: optional(form.documentNumber),
      notes: optional(form.notes),
      lines: form.lines.map((line) => ({
        supplyItemId: line.supplyItemId,
        inventoryLocationId: line.inventoryLocationId,
        quantity: Number(line.quantity),
        unitCost: line.unitCost ? Number(line.unitCost) : undefined,
        lotNumber: optional(line.lotNumber),
        expirationDate: line.expirationDate ? businessDayStart(line.expirationDate) : undefined,
        notes: optional(line.notes),
      })),
    },
  };
}

const newClientId = () => (globalThis.crypto && 'randomUUID' in globalThis.crypto)
  ? globalThis.crypto.randomUUID()
  : `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;

export const buildEmptyRecipeLineForm = (): RecipeLineFormState => ({
  clientId: newClientId(),
  supplyItemId: '',
  inventoryLocationId: '',
  quantity: '1',
  wastePercent: '0',
  notes: '',
});

export const toRecipeLines = (recipe: ProductRecipeDto): RecipeLineFormState[] =>
  recipe.lines.map((line) => ({
    clientId: line.requirementId || newClientId(),
    requirementId: line.requirementId,
    supplyItemId: line.supplyItemId,
    inventoryLocationId: line.inventoryLocationId ?? '',
    quantity: line.quantity.toString(),
    wastePercent: line.wastePercent.toString(),
    notes: line.notes ?? '',
  }));

export function toRecipeRequest(lines: RecipeLineFormState[]): FormResult<UpsertProductRecipeRequest> {
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    const quantity = Number(line.quantity);
    const wastePercent = Number(line.wastePercent);
    if (!line.supplyItemId) return { error: `Línea ${index + 1}: el insumo es obligatorio.` };
    if (Number.isNaN(quantity) || quantity <= 0) return { error: `Línea ${index + 1}: cantidad inválida.` };
    if (Number.isNaN(wastePercent) || wastePercent < 0 || wastePercent > 100)
      return { error: `Línea ${index + 1}: merma inválida (0 a 100).` };
  }

  return {
    request: {
      lines: lines.map((line) => ({
        requirementId: line.requirementId,
        supplyItemId: line.supplyItemId,
        inventoryLocationId: line.inventoryLocationId || undefined,
        quantity: Number(line.quantity),
        wastePercent: Number(line.wastePercent),
        notes: optional(line.notes),
      })),
    },
  };
}

/** What a line takes with its waste, shown while editing; the API computes the one it saves. */
export const effectiveRecipeQuantity = (line: RecipeLineFormState) => {
  const quantity = Number(line.quantity || '0');
  const wastePercent = Number(line.wastePercent || '0');
  return Number.isNaN(quantity) || Number.isNaN(wastePercent) ? 0 : quantity * (1 + (wastePercent / 100));
};
