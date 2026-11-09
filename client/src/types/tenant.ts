// The business: its profile, hours, sales policy, setup and registration, and the enums of its settings.

import type {
  BusinessTheme, BusinessType, DayOfWeek, InsufficientStockPolicy, OnlineMenuMode, OrderNotificationPolicy,
  TenantCapability,
} from '../api/generated/enums';

export interface LogoImageUrlsDto {
  original: string;
  large: string;
  medium: string;
  small: string;
  thumbnail: string;
}

export interface TenantProfileDto {
  id: string;
  name: string;
  slug: string;
  businessType: BusinessType;
  status: number;
  contactEmail: string;
  contactPhone?: string;
  logoUrl?: string;
  logoImages?: LogoImageUrlsDto;
  /** The cover of the online menu (Fase 24E); missing without one. */
  coverImages?: LogoImageUrlsDto | null;
  capabilities: TenantCapability;
  createdAt: string;
  /** True when catalog prices already include IVA; false when IVA is added on top. */
  pricesIncludeTax: boolean;
  /** IVA rate in percent for products without their own (16, 8 or 0). */
  defaultTaxRate: number;
  /** Counter sales close by themselves once fully paid. */
  autoCloseCounterSales: boolean;
  /** The public menu (/m/{slug}): off, menu only, or menu that takes orders. */
  onlineMenuMode?: OnlineMenuMode;
  /** The colors the owner picked for the team's app and the public menu (Nopal until then). */
  theme?: BusinessTheme;
  /** Which orders send the customer WhatsApp messages (paid templates). */
  orderNotificationPolicy?: OrderNotificationPolicy;
  /** Refuse (default) or warn when a sale's recipe supplies are short. */
  insufficientStockPolicy?: InsufficientStockPolicy;
  /** When the owner finished or skipped the guided setup; null while it is pending. */
  onboardingCompletedAt?: string | null;
  /** IANA time zone of the business: dates show and days start as the business lives them. */
  timeZone?: string;
  /** The hours of the business in its time zone; null or missing while it has none (never shows as closed). */
  openingHours?: OpeningIntervalDto[] | null;
  rfc?: string;
  legalName?: string;
  industry?: string;
  taxRegime?: string;
  street?: string;
  exteriorNumber?: string;
  interiorNumber?: string;
  neighborhood?: string;
  zipCode?: string;
  city?: string;
  state?: string;
  country?: string;
}

/**
 * One opening of a day, in the business's time: "09:00" to "14:00". A closing at or before the opening ends the
 * next day ("18:00" to "02:00"); the same time on both is open the whole day. Up to two openings a day.
 */
export interface OpeningIntervalDto {
  day: DayOfWeek;
  opens: string;
  closes: string;
}

export interface TenantSalesPolicyDto {
  pricesIncludeTax: boolean;
  defaultTaxRate: number;
  autoCloseCounterSales: boolean;
  /** Which orders send the customer WhatsApp messages. Omitted keeps the current value. */
  orderNotificationPolicy?: OrderNotificationPolicy;
  /** Refuse or warn when a sale's recipe supplies are short. Omitted keeps the current value. */
  insufficientStockPolicy?: InsufficientStockPolicy;
}

/** The first steps of a business; homeVisits only for one with visits to quote on and the agenda (Fase 29A). */
export type TenantSetupStepKey = 'salesPolicy' | 'catalog' | 'homeVisits' | 'cash' | 'firstSale' | 'team' | 'logo';

export interface TenantSetupStepDto {
  key: TenantSetupStepKey;
  done: boolean;
}

export interface StarterCatalogDto {
  id: string;
  name: string;
  description: string;
  businessType: BusinessType;
  productCount: number;
  categories: string[];
  /** Made for the giro of the business: shown first. */
  isRecommended: boolean;
}

export interface TenantSetupDto {
  businessType: BusinessType;
  capabilities: TenantCapability;
  pricesIncludeTax: boolean;
  defaultTaxRate: number;
  completedAt?: string | null;
  hasProducts: boolean;
  steps: TenantSetupStepDto[];
  starterCatalogs: StarterCatalogDto[];
}

export interface InstallStarterCatalogResultDto {
  templateId: string;
  templateName: string;
  categoriesCreated: number;
  productsCreated: number;
}

export interface UpdateTenantRequest {
  name: string;
  contactEmail: string;
  contactPhone?: string;
  capabilities?: TenantCapability;
  rfc?: string;
  legalName?: string;
  industry?: string;
  taxRegime?: string;
  street?: string;
  exteriorNumber?: string;
  interiorNumber?: string;
  neighborhood?: string;
  zipCode?: string;
  city?: string;
  state?: string;
  /** IANA id of the business's time zone; omitted keeps the current one. */
  timeZone?: string;
}

export { BusinessType } from '../api/generated/enums';

export { TenantCapability } from '../api/generated/enums';

/**
 * Same numbers as Domain.Branches.Enums.BranchServiceMode: a branch customers come to, or one that only delivers and
 * keeps its address for the team.
 */
export { BranchServiceMode } from '../api/generated/enums';

export interface RegisterBusinessRequest {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  businessName: string;
  businessType: BusinessType;
  rfc?: string;
  legalName?: string;
  industry?: string;
  taxRegime?: string;
  contactPhone?: string;
  street?: string;
  exteriorNumber?: string;
  interiorNumber?: string;
  neighborhood?: string;
  zipCode?: string;
  city?: string;
  state?: string;
}

/** Same numbers as Domain.Tenants.Enums.OnlineMenuMode. */
export { OnlineMenuMode } from '../api/generated/enums';

/** Same numbers as Domain.Tenants.Enums.BusinessTheme; the palettes live in utils/businessThemes.ts. */
export { BusinessTheme } from '../api/generated/enums';

/** Same numbers as .NET's System.DayOfWeek: Sunday is 0. The hours of a business use it. */
export { DayOfWeek } from '../api/generated/enums';

/** Same numbers as Domain.Tenants.Enums.SpecialDayKind: a special day closes the business or opens it with other hours. */
export { SpecialDayKind } from '../api/generated/enums';

/**
 * Same numbers as Domain.Tenants.Enums.OrderNotificationPolicy. RemoteAndServices (the default) messages orders
 * placed from a distance and services; a counter sale gets no message.
 */
export { OrderNotificationPolicy } from '../api/generated/enums';

/**
 * Same numbers as Domain.Tenants.Enums.InsufficientStockPolicy: a sale made by the team whose recipe supplies are
 * short is refused naming them (Block, the default) or goes through and warns (Warn).
 */
export { InsufficientStockPolicy } from '../api/generated/enums';
