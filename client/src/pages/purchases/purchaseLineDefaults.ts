import type { SupplyItemDto } from '../../types';

export interface PurchaseLineCost {
  unitCost: string;
  taxPercent: string;
}

const isUntouched = (value: string) => value.trim() === '' || Number(value) === 0;

/**
 * What a purchase line starts with when a supply is picked: the last price paid (or its default cost) and its tax
 * rate. A cost or tax the person already typed stays.
 */
export function getSupplyLineDefaults(
  line: PurchaseLineCost,
  supply?: Pick<SupplyItemDto, 'lastCost' | 'defaultCost' | 'taxRate'>,
): Partial<PurchaseLineCost> {
  if (!supply) return {};
  const patch: Partial<PurchaseLineCost> = {};
  const cost = supply.lastCost > 0 ? supply.lastCost : supply.defaultCost;
  if (isUntouched(line.unitCost) && cost > 0) patch.unitCost = String(cost);
  if (isUntouched(line.taxPercent) && supply.taxRate !== undefined && supply.taxRate !== null && supply.taxRate > 0) {
    patch.taxPercent = String(supply.taxRate);
  }
  return patch;
}
