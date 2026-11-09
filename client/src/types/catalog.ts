// The catalog: categories and their templates, products and services with their options and images.

import type { BusinessType, CfdiTaxObject, UnitType } from '../api/generated/enums';

export { UnitType } from '../api/generated/enums';

/** CFDI 4.0 ObjetoImp of a product: 02 subject to tax (also exempt), 01 outside IVA. */
export { CfdiTaxObject } from '../api/generated/enums';

export interface ProductImageUrlsDto {
  original: string;
  large: string;
  medium: string;
  small: string;
  thumbnail: string;
}

export interface ProductDto {
  id: string;
  categoryId: string;
  name: string;
  description?: string;
  sku?: string;
  barcode?: string;
  sortOrder: number;
  tags?: string;
  unit?: UnitType;
  priceAmount: number;
  priceCurrency: string;
  compareAtPrice?: number;
  /** IVA rate in percent; empty uses the business default. */
  taxRate?: number | null;
  isTaxExempt: boolean;
  hasDiscount: boolean;
  /** CFDI ClaveProdServ; empty uses the generic 01010101. */
  satProductCode?: string | null;
  /** CFDI ClaveUnidad; empty uses the code of the unit. */
  satUnitCode?: string | null;
  taxObject: CfdiTaxObject;
  imageUrl?: string;
  images?: ProductImageUrlsDto;
  isAvailable: boolean;
  isFeatured: boolean;
  preparationTimeMinutes?: number;
  calories?: number;
  allergens?: string;
  maxQuantityPerOrder?: number;
  availableFromTime?: string;
  availableToTime?: string;
  /** The product controls its own stock in inventory: each unit sold takes one from it. */
  tracksStock: boolean;
  /** The inventory item that holds the product's stock. */
  stockItemId?: string | null;
  /** On hand in every location, when it controls its stock. */
  onHand?: number | null;
  reorderPoint?: number | null;
  /** It controls its stock and has no more than its reorder point (sold out included). */
  isLowStock: boolean;
  isTrackable: boolean;
  notifyCustomer: boolean;
  /** Variants and extras offered with the product. */
  optionGroups?: ProductOptionGroupDto[];
}

/** A choice offered with a product: min 1 and max 1 is a variant (size), min 0 makes extras optional. */
export interface ProductOptionGroupDto {
  id: string;
  name: string;
  minSelections: number;
  maxSelections: number;
  options: ProductOptionDto[];
}

export interface ProductOptionDto {
  id: string;
  name: string;
  /** Added to the product price (negative for a cheaper variant). */
  priceDelta: number;
  isAvailable: boolean;
}

export interface CategoryDto {
  id: string;
  name: string;
  description?: string;
  sortOrder: number;
  isActive: boolean;
  /** Products filed under it (archiving asks where to move them). */
  productCount: number;
}

export interface CategoryTemplateItemDto {
  name: string;
  description?: string;
  sortOrder: number;
}

export interface CategoryTemplateDto {
  id: string;
  name: string;
  description: string;
  businessType?: BusinessType;
  isRecommended: boolean;
  categoryCount: number;
  categories: CategoryTemplateItemDto[];
}

export interface InstallCategoryTemplateResultDto {
  templateId: string;
  templateName: string;
  createdCount: number;
  skippedCount: number;
  totalCount: number;
}

export interface CreateProductRequest {
  categoryId: string;
  name: string;
  description?: string;
  priceAmount: number;
  priceCurrency: string;
  sku?: string;
  barcode?: string;
  sortOrder: number;
  isFeatured: boolean;
  tags?: string;
  compareAtPrice?: number;
  taxRate?: number | null;
  isTaxExempt?: boolean;
  satProductCode?: string;
  satUnitCode?: string;
  taxObject?: CfdiTaxObject;
  calories?: number;
  allergens?: string;
  maxQuantityPerOrder?: number;
  unit?: UnitType;
  preparationTimeMinutes?: number;
  availableFromTime?: string;
  availableToTime?: string;
  isTrackable?: boolean;
  notifyCustomer?: boolean;
}

export interface UpdateProductRequest extends CreateProductRequest {
  id: string;
}

export interface CreateCategoryRequest {
  name: string;
  description?: string;
  sortOrder: number;
}

export interface UpdateCategoryRequest extends CreateCategoryRequest {
  id: string;
}
