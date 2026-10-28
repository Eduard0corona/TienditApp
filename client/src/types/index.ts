// The enums are generated from the API contract and re-exported where each one belongs below.
import type {
  AppointmentChannel,
  AppointmentStatus,
  BusinessTheme,
  BusinessType,
  CampaignAudienceKind,
  CampaignStatus,
  CashSessionStatus,
  CashTransactionType,
  CfdiTaxObject,
  DayOfWeek,
  DeliveryCourier,
  InsufficientStockPolicy,
  InventoryMovementReason,
  InventoryMovementType,
  KitchenTicketStatus,
  MessagePurpose,
  OnlineMenuMode,
  OptOutSource,
  OrderChannel,
  OrderNotificationPolicy,
  OrderPaymentMethod,
  OrderStatus,
  OrderType,
  OrderWriteOffReason,
  OutgoingMessageStatus,
  PlatformCollectedBy,
  PurchaseOrderStatus,
  ServiceOrderPieceStatus,
  ServicePhotoStage,
  TenantCapability,
  UnitType,
} from '../api/generated/enums';

export interface AuthResponse {
  accessToken?: string;
  refreshToken?: string;
  expiresAt?: string;
  requiresTwoFactor: boolean;
  twoFactorChallengeToken?: string;
  userId?: string;
  user?: UserDto;
}

export interface UserDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
  phoneNumberConfirmed: boolean;
  role: string;
  tenantId: string;
  isActive: boolean;
  avatarUrl?: string;
  avatarImages?: AvatarImageUrlsDto;
}

export interface AvatarImageUrlsDto {
  original: string;
  large: string;
  medium: string;
  small: string;
  thumbnail: string;
}

export interface UserProfileDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  fullName: string;
  phoneNumber?: string;
  phoneNumberConfirmed: boolean;
  emailConfirmed: boolean;
  role: string;
  tenantRole?: string;
  permissions: string[];
  tenantId: string;
  isActive: boolean;
  avatarUrl?: string;
  avatarImages?: AvatarImageUrlsDto;
  twoFactorEnabled: boolean;
  createdAt: string;
}

export interface EmployeeDto {
  tenantUserId: string;
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
  appRole: string;
  tenantRole: string;
  status: string;
  isActive: boolean;
  createdAt: string;
  permissions: string[];
}

export interface MyTenantDto {
  tenantId: string;
  name: string;
  slug: string;
  role: string;
  status: string;
  /** The business's logo in small (128 px) for the business switcher; null while it has none. */
  logoUrl?: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface TwoFactorLoginRequest {
  twoFactorChallengeToken: string;
  code: string;
  useRecoveryCode: boolean;
}

export interface DisableTwoFactorRequest {
  code: string;
  useRecoveryCode: boolean;
}

export interface UpdateProfileRequest {
  firstName: string;
  lastName: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface ApiError {
  error: string;
}

export interface ApiMessage {
  message: string;
}

// ==================== Tenant ====================

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

export type TenantSetupStepKey = 'salesPolicy' | 'catalog' | 'cash' | 'firstSale' | 'team' | 'logo';

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

// ==================== WhatsApp ====================

export interface WhatsAppConfigDto {
  id: string;
  tenantId: string;
  isEnabled: boolean;
  phoneNumberId?: string;
  wabaId?: string;
  displayPhoneNumber?: string;
  hasAccessToken: boolean;
  verifyToken?: string;
}

export interface UpdateWhatsAppConfigRequest {
  isEnabled: boolean;
  accessToken?: string;
  phoneNumberId?: string;
  wabaId?: string;
  displayPhoneNumber?: string;
}

export interface SendWhatsAppMessageRequest {
  phoneNumber: string;
  message: string;
}

// ==================== WhatsApp Chat ====================

export interface ConversationDto {
  id: string;
  customerPhone: string;
  customerName?: string;
  lastMessageBody?: string;
  lastMessageAt: string;
  unreadCount: number;
  /** The automatic replies stay quiet until then (a person answered or paused them). */
  autoRepliesPausedUntil?: string | null;
}

export interface ChatMessageDto {
  id: string;
  conversationId: string;
  direction: string;
  body: string;
  status: string;
  sentAt: string;
}

export interface SendChatMessageRequest {
  phoneNumber: string;
  message: string;
  conversationId?: string;
}

// ==================== Registration ====================

export { BusinessType } from '../api/generated/enums';

export { TenantCapability } from '../api/generated/enums';

/**
 * Same numbers as Domain.Branches.Enums.BranchServiceMode: a branch customers come to, or one that only delivers and
 * keeps its address for the team.
 */
export { BranchServiceMode } from '../api/generated/enums';
import type { OnlinePaymentDispute, OnlinePaymentRefundReason, OnlinePaymentRefundStatus } from '../api/generated/enums';

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

export interface RegisterCustomerRequest {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
}

// ==================== Catalog ====================

export { UnitType } from '../api/generated/enums';

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

/** CFDI 4.0 ObjetoImp of a product: 02 subject to tax (also exempt), 01 outside IVA. */
export { CfdiTaxObject } from '../api/generated/enums';
// The invoice of an order stamped through the PAC (Fase 28A), the global invoice of a period (Fase 28B), what a credit
// note credits (Fase 28D) and the kinds of CFDI of Reportes › CFDI emitidos (Fase 28H).
export {
  CfdiCancellationMotive, CfdiEmailDocument, CfdiEmailFailure, CreditNoteAdjustment, GlobalInvoicePeriodicity, IssuedCfdiExportFormat,
  IssuedCfdiKind, OrderInvoiceStatus,
} from '../api/generated/enums';

/** RFC, name, fiscal postal code, regime and CFDI use, as the customer's constancia says (Fase 28A). */
export interface CustomerFiscalData {
  rfc: string;
  name: string;
  postalCode: string;
  fiscalRegime: string;
  cfdiUse: string;
}

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

export interface PaginatedList<T> {
  items: T[];
  pageNumber: number;
  totalPages: number;
  totalCount: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
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

// ==================== Inventory ====================

// Same numbers as FoodParkDigital.Domain.Inventory.Enums: the API exchanges them as integers and the
// database stores them, so they never change. ClientEnumContractTests (WebApi.Tests) compares both sides.
export { InventoryMovementType } from '../api/generated/enums';

export { InventoryMovementReason } from '../api/generated/enums';
export { InventoryCountStatus } from '../api/generated/enums';
export { InventoryReconciliationEventKind } from '../api/generated/enums';

export interface SupplyItemDto {
  id: string;
  name: string;
  code?: string;
  sku?: string;
  barcode?: string;
  description?: string;
  brand?: string;
  model?: string;
  categoryName?: string;
  unit: UnitType;
  defaultCost: number;
  lastCost: number;
  averageCost: number;
  minStock: number;
  maxStock?: number;
  reorderPoint: number;
  trackBatches: boolean;
  trackExpiration: boolean;
  isPerishable: boolean;
  isCritical: boolean;
  taxRate?: number;
  notes?: string;
  isActive: boolean;
  /** The product this item is the stock of, when the product controls its own. */
  productId?: string | null;
  productName?: string | null;
  createdAt: string;
  updatedAt?: string;
  totalOnHand: number;
  totalReserved: number;
  totalAvailable: number;
  isLowStock: boolean;
}

export interface CreateSupplyItemRequest {
  name: string;
  code?: string;
  sku?: string;
  barcode?: string;
  description?: string;
  brand?: string;
  model?: string;
  categoryName?: string;
  unit: UnitType;
  defaultCost: number;
  minStock: number;
  maxStock?: number;
  reorderPoint: number;
  trackBatches: boolean;
  trackExpiration: boolean;
  isPerishable: boolean;
  isCritical?: boolean;
  taxRate?: number;
  notes?: string;
}

export interface UpdateSupplyItemRequest extends CreateSupplyItemRequest {
  id: string;
}

export interface InventoryLocationDto {
  id: string;
  name: string;
  code: string;
  description?: string;
  addressLine1?: string;
  addressLine2?: string;
  city?: string;
  state?: string;
  country?: string;
  postalCode?: string;
  contactName?: string;
  contactPhone?: string;
  isDefault: boolean;
  isActive: boolean;
  /** Branch the stock belongs to; none for a warehouse that serves every branch. */
  branchId?: string | null;
  branchName?: string | null;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateInventoryLocationRequest {
  name: string;
  code: string;
  description?: string;
  addressLine1?: string;
  addressLine2?: string;
  city?: string;
  state?: string;
  country?: string;
  postalCode?: string;
  contactName?: string;
  contactPhone?: string;
  isDefault: boolean;
  branchId?: string;
}

export interface UpdateInventoryLocationRequest extends CreateInventoryLocationRequest {
  id: string;
  /** Makes it a warehouse that serves every branch (without it, a missing branchId keeps the current one). */
  detachFromBranch?: boolean;
}

export interface InventoryStockDto {
  stockId: string;
  supplyItemId: string;
  supplyItemName: string;
  supplyCode?: string;
  supplySku?: string;
  supplyBarcode?: string;
  unit: UnitType;
  inventoryLocationId: string;
  inventoryLocationName: string;
  inventoryLocationCode: string;
  onHandQuantity: number;
  reservedQuantity: number;
  availableQuantity: number;
  minStock: number;
  reorderPoint: number;
  maxStock?: number;
  lastCost: number;
  lastMovementAt?: string;
  isSupplyActive: boolean;
  isLocationActive: boolean;
  isLowStock: boolean;
}

export interface InventoryMovementLineDto {
  id: string;
  supplyItemId: string;
  supplyItemName: string;
  inventoryLocationId: string;
  inventoryLocationName: string;
  quantity: number;
  unitCost?: number;
  totalCost?: number;
  quantityBefore: number;
  quantityAfter: number;
  lotNumber?: string;
  expirationDate?: string;
  notes?: string;
}

export interface InventoryMovementDto {
  id: string;
  movementNumber: string;
  movementType: InventoryMovementType;
  movementReason: InventoryMovementReason;
  movementDate: string;
  referenceNumber?: string;
  externalReference?: string;
  counterpartyName?: string;
  documentNumber?: string;
  notes?: string;
  totalAmount: number;
  createdAt: string;
  lines: InventoryMovementLineDto[];
}

export interface CreateInventoryMovementLineRequest {
  supplyItemId: string;
  inventoryLocationId: string;
  quantity: number;
  unitCost?: number;
  lotNumber?: string;
  expirationDate?: string;
  notes?: string;
}

export interface CreateInventoryMovementRequest {
  movementType: InventoryMovementType;
  movementReason: InventoryMovementReason;
  movementDate?: string;
  referenceNumber?: string;
  externalReference?: string;
  counterpartyName?: string;
  documentNumber?: string;
  notes?: string;
  lines: CreateInventoryMovementLineRequest[];
}

export interface ProductRecipeLineDto {
  requirementId: string;
  supplyItemId: string;
  supplyItemName: string;
  supplyCode?: string;
  inventoryLocationId?: string;
  inventoryLocationName?: string;
  quantity: number;
  wastePercent: number;
  effectiveQuantity: number;
  notes?: string;
  isActive: boolean;
}

/** The stock of a product in inventory, by location. */
export interface ProductStockDto {
  productId: string;
  tracksStock: boolean;
  stockItemId?: string | null;
  onHand: number;
  reorderPoint: number;
  isLowStock: boolean;
  locations: ProductStockLocationDto[];
}

export interface ProductStockLocationDto {
  inventoryLocationId: string;
  locationName: string;
  onHand: number;
}

export interface TrackProductStockRequest {
  /** An item the business already keeps for the product, instead of a new one. */
  supplyItemId?: string | null;
  initialQuantity: number;
  inventoryLocationId?: string | null;
  unitCost?: number | null;
  reorderPoint: number;
}

export interface ProductRecipeDto {
  productId: string;
  productName: string;
  productIsAvailable: boolean;
  lines: ProductRecipeLineDto[];
}

export interface UpsertProductRecipeLineRequest {
  requirementId?: string;
  supplyItemId: string;
  inventoryLocationId?: string;
  quantity: number;
  wastePercent: number;
  notes?: string;
}

export interface UpsertProductRecipeRequest {
  lines: UpsertProductRecipeLineRequest[];
}

export interface InventoryKardexEntryDto {
  movementLineId: string;
  movementId: string;
  movementNumber: string;
  movementDate: string;
  movementType: InventoryMovementType;
  movementReason: InventoryMovementReason;
  referenceNumber?: string;
  externalReference?: string;
  supplyItemId: string;
  supplyItemName: string;
  inventoryLocationId: string;
  inventoryLocationName: string;
  quantityIn: number;
  quantityOut: number;
  signedQuantity: number;
  quantityBefore: number;
  quantityAfter: number;
  unitCost?: number;
  totalCost?: number;
}

export interface InventoryAlertItemDto {
  supplyItemId: string;
  supplyItemName: string;
  inventoryLocationId: string;
  inventoryLocationName: string;
  onHandQuantity: number;
  reorderPoint: number;
  deficit: number;
  isOutOfStock: boolean;
  isCritical: boolean;
}

export interface InventoryAlertsDto {
  lowStockCount: number;
  criticalLowStockCount: number;
  outOfStockCount: number;
  missingRecipeProductCount: number;
  items: InventoryAlertItemDto[];
}

// ==================== Purchasing ====================

/** Closed (4): part arrived and the rest will not; closed with what came in. */
export { PurchaseOrderStatus } from '../api/generated/enums';

export interface SupplierDto {
  id: string;
  name: string;
  contactName?: string;
  phone?: string;
  email?: string;
  taxId?: string;
  address?: string;
  notes?: string;
  isActive: boolean;
}

export interface CreateSupplierRequest {
  name: string;
  contactName?: string;
  phone?: string;
  email?: string;
  taxId?: string;
  address?: string;
  notes?: string;
}

export interface UpdateSupplierRequest extends CreateSupplierRequest {
  id: string;
}

export interface PurchaseOrderLineDto {
  id: string;
  supplyItemId: string;
  supplyItemName: string;
  /** Unit the supply is bought and stocked in. */
  unit: UnitType;
  inventoryLocationId: string;
  inventoryLocationName: string;
  quantity: number;
  receivedQuantity: number;
  pendingQuantity: number;
  unitCost: number;
  taxPercent: number;
  subTotal: number;
  taxAmount: number;
  total: number;
  notes?: string;
}

export interface PurchaseOrderDto {
  id: string;
  supplierId: string;
  supplierName: string;
  /** Supplier's phone, to send the order by WhatsApp. */
  supplierPhone?: string | null;
  orderNumber: string;
  status: PurchaseOrderStatus;
  orderedAt: string;
  expectedDeliveryAt?: string;
  receivedAt?: string;
  referenceNumber?: string;
  notes?: string;
  subTotal: number;
  tax: number;
  total: number;
  /** When the business stopped waiting for what was missing, and why. */
  closedAt?: string | null;
  closeReason?: string | null;
  lines: PurchaseOrderLineDto[];
}

export interface CreatePurchaseOrderLineRequest {
  supplyItemId: string;
  inventoryLocationId: string;
  quantity: number;
  unitCost: number;
  taxPercent: number;
  notes?: string;
}

export interface CreatePurchaseOrderRequest {
  supplierId: string;
  expectedDeliveryAt?: string;
  referenceNumber?: string;
  notes?: string;
  markAsSent: boolean;
  lines: CreatePurchaseOrderLineRequest[];
}

export interface ReceivePurchaseOrderLineRequest {
  purchaseOrderLineId: string;
  quantityReceived: number;
  /** Supplier's actual price when it differs from the ordered one; the stock is valued at it. */
  unitCost?: number;
}

export interface ReceivePurchaseOrderRequest {
  receivedAt?: string;
  lines: ReceivePurchaseOrderLineRequest[];
  /** The same key again (a double click, a retry) answers with the order as it is, without receiving twice. */
  idempotencyKey?: string;
}

// ==================== Cash ====================

export { CashSessionStatus } from '../api/generated/enums';

export { CashTransactionType } from '../api/generated/enums';

export interface CashSessionDto {
  id: string;
  openedAt: string;
  /** The user (email) of who opened the drawer. */
  openedBy: string;
  /** Their name, when they are a member of the business: shown instead of `openedBy`. */
  openedByName?: string | null;
  openingAmount: number;
  closedAt?: string;
  closedBy?: string;
  /** The name of who closed it, when they are a member of the business: shown instead of `closedBy`. */
  closedByName?: string | null;
  closingAmount?: number;
  status: CashSessionStatus;
  notes?: string;
  /** Cash entering the drawer (cash payments and deposits). */
  inflows: number;
  /** Cash leaving the drawer (expenses, withdrawals, refunds). */
  outflows: number;
  /** Card, transfer and other payments: recorded in the session but not in the drawer. */
  nonCashInflows: number;
  expectedBalance: number;
  openedByUserId?: string | null;
  /** The drawer belongs to the current user: their cash payments enter it. */
  isMine: boolean;
  inventoryLocationId?: string | null;
  inventoryLocationName?: string | null;
  /** Branch of the drawer: payments of sales made there enter it. */
  branchId?: string | null;
  branchName?: string | null;
  /** Expected cash frozen at closing. */
  expectedClosingAmount?: number | null;
  /** Counted minus expected: negative is a shortage. */
  closingDifference?: number | null;
  closingNotes?: string | null;
  cashCount: CashCountLineDto[];
}

export interface CashCountLineDto {
  denomination: number;
  quantity: number;
}

export interface CashTransactionDto {
  id: string;
  cashSessionId: string;
  transactionType: CashTransactionType;
  amount: number;
  occurredAt: string;
  paymentMethod?: OrderPaymentMethod;
  inventoryLocationId?: string;
  inventoryLocationName?: string;
  orderId?: string;
  reference?: string;
  notes?: string;
  createdBy?: string;
}

export interface DailyCashCutByUserDto {
  /** The user (email) who took the payments, or "Sistema": what the cut groups and filters by. */
  userName: string;
  /** Their name, when they are a member of the business: shown instead of `userName`. */
  displayName?: string | null;
  paymentsCount: number;
  totalAmount: number;
}

export interface DailyCashCutByLocationDto {
  inventoryLocationId?: string;
  locationName: string;
  paymentsCount: number;
  totalAmount: number;
}

export interface DailyCashCutByBranchDto {
  branchId?: string | null;
  branchName: string;
  paymentsCount: number;
  totalAmount: number;
}

export interface DailyCashCutDto {
  date: string;
  salesPaymentsTotal: number;
  salesPaymentsCount: number;
  cashTotal: number;
  cardTotal: number;
  transferTotal: number;
  walletTotal: number;
  /**
   * Every method without a bucket of its own. Platforms, online payments and the balance in the customer's favor have theirs:
   * with them, the buckets add up to salesPaymentsTotal.
   */
  otherTotal: number;
  /** What the delivery platforms charged in their apps («Plataforma»): in no drawer and in no cashier's row. */
  platformTotal?: number;
  platformPaymentsCount?: number;
  /** What went back as «Plataforma» in the day: no drawer gave it. */
  platformRefundsTotal?: number;
  /**
   * What customers paid with their balance in their favor (Fase 26D): part of what was charged, in no drawer and no
   * cashier's row, since it entered when they left it.
   */
  customerBalanceTotal?: number;
  customerBalancePaymentsCount?: number;
  /** What was refunded to customers' balances in the day: no drawer gave it. */
  customerBalanceRefundsTotal?: number;
  /** What customers left in their favor and what was given back of it, whatever the method: neither sales nor by hand. */
  customerDepositsTotal?: number;
  customerDepositReturnsTotal?: number;
  /** The part of those in cash: in netCashOnlyFlow. */
  cashCustomerDepositsTotal?: number;
  cashCustomerDepositReturnsTotal?: number;
  /** Deposits, expenses, withdrawals and refunds of the day, whatever their method. */
  manualIncomeTotal: number;
  manualExpenseTotal: number;
  /** The part of those movements made in cash: only it enters or leaves the drawers. */
  cashManualIncomeTotal: number;
  cashManualExpenseTotal: number;
  /** All the money the business received in the day, of any method (a card refund lowers it), without what the platforms charged. */
  netCashFlow: number;
  /** Cash payments plus cash manual income minus cash manual expenses: the net change of the drawers. */
  netCashOnlyFlow: number;
  /** Tips received with the payments of the day (any method); not part of the sales. */
  tipsTotal: number;
  /** Tips paid in cash: they are in the drawers, included in netCashOnlyFlow. */
  cashTipsTotal: number;
  byUser: DailyCashCutByUserDto[];
  byLocation: DailyCashCutByLocationDto[];
  /** Payments by the branch where the sale was made. */
  byBranch: DailyCashCutByBranchDto[];
}

export interface OpenCashSessionRequest {
  openingAmount: number;
  notes?: string;
  /** Branch of the drawer; the default one when omitted. */
  branchId?: string;
}

export interface CloseCashSessionRequest {
  closingAmount: number;
  /** Required when the count differs from the expected cash. */
  notes?: string;
  /** Optional count by denomination; must add up to closingAmount. */
  cashCount?: CashCountLineDto[];
}

export interface AddCashTransactionRequest {
  cashSessionId: string;
  transactionType: CashTransactionType;
  /** Always positive; the server applies the sign from the transaction type. */
  amount: number;
  paymentMethod?: OrderPaymentMethod;
  inventoryLocationId?: string;
  orderId?: string;
  reference?: string;
  notes?: string;
}

// ==================== Appointments ====================

export { AppointmentStatus, AppointmentChannel, MyAppointmentsScope } from '../api/generated/enums';

export interface AppointmentDto {
  id: string;
  customerName: string;
  customerPhone: string;
  serviceName: string;
  staffName?: string;
  /** The service of the catalog it books (older appointments only have the name). */
  serviceProductId?: string | null;
  /** The team member who attends it. */
  staffUserId?: string | null;
  startAt: string;
  endAt: string;
  status: AppointmentStatus;
  notes?: string;
  /** Where it happens when it is at the customer's address (Fase 22E); null when they come to the business. */
  visit?: AppointmentVisitDto | null;
  /** The sale it was charged with; null until it is charged. */
  saleOrderId?: string | null;
  /** Its sale was cancelled: it is charged again with another one. */
  saleCancelled?: boolean;
  /** Who booked it: the business, or its customer from the online menu (Fase 24D). */
  channel?: AppointmentChannel;
  /** Its customer cancelled it from its link or account (Fase 24D). */
  cancelledByCustomer?: boolean;
}

/** The customer's address of an appointment at their home, its zone and the charge for the visit agreed then. */
export interface AppointmentVisitDto {
  postalCode: string;
  addressLine: string;
  references?: string | null;
  zoneName: string;
  /** With the business's tax policy, like its prices; 0 when the zone charges nothing. */
  fee: number;
}

/** Where an appointment at the customer's address happens, as typed: the server finds its zone. */
export interface AppointmentVisitInput {
  postalCode: string;
  addressLine: string;
  references?: string;
}

export interface CreateAppointmentRequest {
  customerName: string;
  customerPhone: string;
  /** Free name; with serviceProductId the catalog name is used. */
  serviceName?: string;
  serviceProductId?: string;
  staffName?: string;
  staffUserId?: string;
  startAt: string;
  endAt: string;
  notes?: string;
  /** At the customer's address; null (or missing) when the customer comes to the business. */
  visit?: AppointmentVisitInput | null;
}

// ==================== Reports ====================

export interface OperationalReportDto {
  rangeFrom: string;
  rangeTo: string;
  /** Completed orders, tax included. */
  totalRevenue: number;
  totalTax: number;
  /** Revenue without tax: the base of the gross profit. */
  netRevenue: number;
  totalOrderPayments: number;
  totalPurchases: number;
  totalCogs: number;
  grossProfit: number;
  ordersCompleted: number;
  appointmentsCompleted: number;
  appointmentsScheduled: number;
  revenueByPaymentMethod: Record<string, number>;
}

// ==================== Audit ====================

export interface AuditLogDto {
  id: string;
  entityName: string;
  entityId: string;
  action: string;
  performedBy: string;
  timestamp: string;
  metadata?: string;
}

// ==================== Marketing ====================

// ==================== Messaging (campaigns, reminders, opt-outs) ====================

// AppointmentNotice: booked, moved, confirmed or cancelled; queued by the system, it has no templates.
export { MessagePurpose } from '../api/generated/enums';

export { OutgoingMessageStatus } from '../api/generated/enums';

export { CampaignStatus } from '../api/generated/enums';

export { CampaignAudienceKind } from '../api/generated/enums';

export { OptOutSource } from '../api/generated/enums';

export interface MessageTemplateDto {
  id: string;
  name: string;
  purpose: MessagePurpose;
  body: string;
  /** Template approved in Meta; without it the text only reaches customers who wrote in the last 24 hours. */
  providerTemplateName?: string;
  languageCode: string;
  isActive: boolean;
  /** Placeholders in the order they become the Meta template parameters. */
  parameters: string[];
  createdAt: string;
  updatedAt?: string;
}

export interface SaveMessageTemplateRequest {
  name: string;
  purpose?: MessagePurpose;
  body: string;
  providerTemplateName?: string;
  languageCode?: string;
  isActive?: boolean;
}

export interface CampaignProgressDto {
  pending: number;
  sent: number;
  delivered: number;
  read: number;
  failed: number;
  skipped: number;
  cancelled: number;
}

export interface CampaignDto {
  id: string;
  name: string;
  templateId: string;
  templateName?: string;
  audienceKind: CampaignAudienceKind;
  audienceMinimumOrders: number;
  audienceDays?: number;
  status: CampaignStatus;
  scheduledAt: string;
  startedAt?: string;
  completedAt?: string;
  cancelledAt?: string;
  recipientCount: number;
  excludedCount: number;
  createdBy?: string;
  createdAt: string;
  progress: CampaignProgressDto;
}

export interface CampaignAudienceRequest {
  templateId: string;
  audienceKind: CampaignAudienceKind;
  minimumOrders: number;
  days?: number;
}

export interface CreateCampaignRequest extends CampaignAudienceRequest {
  name: string;
  /** Empty starts now. */
  scheduledAt?: string;
}

export interface CampaignPreviewDto {
  recipientCount: number;
  excludedCount: number;
  truncated: boolean;
  maxRecipients: number;
  usesApprovedTemplate: boolean;
  samples: Array<{ recipientName?: string; phone: string; text: string }>;
}

export interface OutgoingMessageDto {
  id: string;
  purpose: MessagePurpose;
  campaignId?: string;
  appointmentId?: string;
  phone: string;
  recipientName?: string;
  body: string;
  status: OutgoingMessageStatus;
  attempts: number;
  createdAt: string;
  nextAttemptAt: string;
  sentAt?: string;
  deliveredAt?: string;
  readAt?: string;
  lastError?: string;
}

export interface MessagingOptOutDto {
  id: string;
  phone: string;
  source: OptOutSource;
  optedOutAt: string;
  resubscribedAt?: string;
  isActive: boolean;
}

export interface MessagingSettingsDto {
  whatsAppConnected: boolean;
  appointmentsEnabled: boolean;
  appointmentRemindersEnabled: boolean;
  appointmentReminderHoursBefore: number;
  appointmentReminderTemplateId?: string;
  defaultReminderBody: string;
  minReminderHoursBefore: number;
  maxReminderHoursBefore: number;
  campaignPlaceholders: string[];
  reminderPlaceholders: string[];
  /** Fase 26C: the variables of a reminder of the fiado. */
  creditReminderPlaceholders: string[];
  creditDueSoonPlaceholders: string[];
}

export interface UpdateAppointmentRemindersRequest {
  enabled: boolean;
  hoursBefore: number;
  templateId?: string;
}

// ==================== Customers ====================

export interface CustomerAddressDto {
  id: string;
  label: string;
  addressLine: string;
  references?: string | null;
  isDefault: boolean;
}

export interface CustomerDto {
  id: string;
  name: string;
  /** E.164 (+52 and ten digits for Mexican numbers). */
  phone?: string;
  email?: string;
  isPublicGeneral: boolean;
  isActive: boolean;
  notes?: string | null;
  /** How much the business lets them owe (fiado); null when they are not sold on credit. */
  creditLimit?: number | null;
  /** Their own term to pay what they owe (Fase 26C); null when the business's applies. */
  creditTermDays?: number | null;
  /** What their invoices go to (Fase 28A); null when the business has not captured it. */
  fiscalData?: CustomerFiscalData | null;
  /** Where their CFDI go by email (Fase 28E), kept with their fiscal data; null when none. */
  invoiceEmail?: string | null;
  /** Only in the detail of one customer. */
  addresses?: CustomerAddressDto[];
  createdAt: string;
  updatedAt?: string;
}

export interface CreateCustomerRequest {
  name: string;
  phone: string;
  email?: string;
  notes?: string;
}

export interface UpdateCustomerRequest extends CreateCustomerRequest {
  id: string;
}

// ==================== Orders ====================

export { OrderStatus } from '../api/generated/enums';

export { OrderType } from '../api/generated/enums';

export { OrderPaymentMethod } from '../api/generated/enums';

export { ServiceOrderPieceStatus } from '../api/generated/enums';

/** The photos of a service (Fase 25): before (0) or after (1). */
export { ServicePhotoStage } from '../api/generated/enums';

/** How a customer follows an order (Fase 24C): its steps and the account's orders in progress or before. */
export { OrderTrackingStep, OrderTrackingStepState, MyOrdersScope } from '../api/generated/enums';

export interface OrderItemDto {
  id: string;
  productId: string;
  productName: string;
  /** Up to three decimals for weight, volume, length and time; whole for pieces. */
  quantity: number;
  /** Units given back with refunds; the sold quantity never changes. */
  returnedQuantity?: number;
  /** Units of a tab's dish cancelled after it went to the kitchen: the order no longer charges them. */
  cancelledQuantity?: number;
  /** Unit the line was sold in, with its CFDI codes at the time of the sale. */
  unit?: UnitType;
  satProductCode?: string;
  satUnitCode?: string;
  taxObject?: CfdiTaxObject;
  unitPrice: number;
  /** The line as it was ordered: quantity by unit price. */
  totalPrice: number;
  /** What the line charges: its total less the units cancelled from a tab. */
  chargedPrice?: number;
  notes?: string;
  /** IVA rate in percent the line was sold with (0 when exempt). */
  taxRate: number;
  isTaxExempt: boolean;
  /** Round of an open tab the line was ordered in (1 for any other sale). */
  round?: number;
  /** Variants and extras chosen, already included in the unit price. */
  options?: OrderItemOptionDto[];
}

export interface OrderItemOptionDto {
  optionId: string;
  groupName: string;
  optionName: string;
  priceDelta: number;
}

/** Same numbers as Domain.Orders.Enums.KitchenTicketStatus: a round of an open tab in the kitchen. */
export { KitchenTicketStatus } from '../api/generated/enums';

export interface KitchenTicketSummaryDto {
  id: string;
  round: number;
  status: KitchenTicketStatus;
  sentAt: string;
  sentBy?: string | null;
  readyAt?: string | null;
  servedAt?: string | null;
}

/** A round of an open tab on the kitchen board, with only its own lines. */
export interface KitchenTicketDto {
  id: string;
  orderId: string;
  orderNumber: string;
  customerName: string;
  tableNumber?: string | null;
  customerNotes?: string | null;
  round: number;
  status: KitchenTicketStatus;
  sentAt: string;
  startedAt?: string | null;
  readyAt?: string | null;
  items: OrderItemDto[];
}

/** IVA of the lines of one rate, as computed by the server. */
export interface OrderTaxLineDto {
  ratePercent: number;
  isExempt: boolean;
  taxableBase: number;
  taxAmount: number;
  total: number;
}

export interface OrderStatusHistoryDto {
  id: string;
  previousStatus: OrderStatus;
  newStatus: OrderStatus;
  changedAt: string;
  changedBy?: string;
}

export interface ServiceOrderDetailDto {
  id: string;
  receivedAt: string;
  promisedAt?: string;
  priority: number;
  intakeNotes?: string;
  deliveryNotes?: string;
  intakeChecklistJson?: string;
  beforePhotosJson?: string;
  afterPhotosJson?: string;
}

/** A photo of a service for the business's team (Fase 25): taken when the order was received, or added later. */
export interface ServiceOrderPhotoDto {
  /** None for a photo taken when the order was received. */
  id?: string;
  stage: ServicePhotoStage;
  /** Private file path ("/files/orders/..."). */
  url: string;
  fileName?: string;
  note?: string;
  atIntake: boolean;
  takenAt?: string;
  takenBy?: string;
  /** Whether this person may remove it: while the order is open, whoever took it or a manager. */
  canRemove: boolean;
}

export interface ServiceOrderPhotosDto {
  orderId: string;
  /** Every service order takes photos but a cancelled one. */
  canAdd: boolean;
  /** Once delivered or finished, only photos after. */
  canAddBefore: boolean;
  photos: ServiceOrderPhotoDto[];
}

export interface AddServiceOrderPhotoRequest {
  stage: ServicePhotoStage;
  /** What the upload returned. */
  fileUrl: string;
  fileName?: string;
  note?: string;
}

export interface ServiceOrderPieceDto {
  id: string;
  itemName: string;
  serviceProductId?: string;
  isTrackable: boolean;
  notifyCustomer: boolean;
  assignedToUserId?: string;
  assignedToName?: string;
  assignedAt?: string;
  brand?: string;
  model?: string;
  color?: string;
  size?: string;
  material?: string;
  conditionNotes?: string;
  requestedService?: string;
  internalNotes?: string;
  status: ServiceOrderPieceStatus;
}

export interface ServiceBoardPieceDto {
  pieceId: string;
  orderId: string;
  orderNumber: string;
  customerName: string;
  customerPhone?: string;
  orderCreatedAt: string;
  promisedAt?: string;
  priority: number;
  itemName: string;
  requestedService?: string;
  internalNotes?: string;
  notifyCustomer: boolean;
  status: ServiceOrderPieceStatus;
  assignedToUserId?: string;
  assignedToName?: string;
  assignedAt?: string;
  isOverdue: boolean;
  delayMinutes?: number;
  minutesToPromise?: number;
}

export interface OrderPaymentDto {
  id: string;
  /** What the payment covers of the sale; the tip is apart. */
  amount: number;
  method: OrderPaymentMethod;
  paidAt: string;
  inventoryLocationId?: string;
  evidenceUrl?: string;
  reference?: string;
  notes?: string;
  /** Tip given with this payment: not part of the sale, its total or its balance. */
  tip?: number;
  /** Cash handed by the customer (cash payments only). */
  cashReceived?: number | null;
  /** Cash given back: received minus amount minus tip. */
  change?: number;
}

export interface OrderDto {
  id: string;
  customerUserId?: string;
  temporaryCustomerId?: string;
  createdByBusiness: boolean;
  orderNumber: string;
  businessType: BusinessType;
  orderType: OrderType;
  status: OrderStatus;
  canCancel: boolean;
  allowedTransitions: OrderStatus[];
  customerName: string;
  customerPhone?: string;
  customerNotes?: string;
  tableNumber?: string;
  /** True when item prices included IVA; false when IVA was added on top. */
  pricesIncludeTax: boolean;
  /** Taxable base: item amounts without IVA. */
  subTotal: number;
  tax: number;
  total: number;
  taxBreakdown: OrderTaxLineDto[];
  channel?: OrderChannel;
  /** An order of Rappi, Uber Eats or DiDi Food: the folio of its app and who charged it (never its commission). */
  platformOrder?: OrderPlatformDto | null;
  /** An order sent to the customer's address: where, the zone, what it charged and the courier's trip. */
  delivery?: OrderDeliveryDto | null;
  /** The sale of an appointment at the customer's address: what it charged for the visit (Fase 22E). */
  visitCharge?: OrderVisitChargeDto | null;
  paidAmount: number;
  balance: number;
  /** Money given back; payments are never edited. */
  refundedAmount?: number;
  /** Paid minus refunded. */
  netPaidAmount?: number;
  /** What the business forgave of what the customer owed (Fase 26B); the balance already leaves it out. */
  writtenOffAmount?: number;
  /** Tips received with the payments; apart from the total. */
  tipTotal?: number;
  cancellationReason?: string;
  createdAt: string;
  updatedAt?: string;
  items: OrderItemDto[];
  statusHistory: OrderStatusHistoryDto[];
  serviceDetail?: ServiceOrderDetailDto;
  servicePieces: ServiceOrderPieceDto[];
  payments: OrderPaymentDto[];
  refunds?: OrderRefundDto[];
  /**
   * A chargeback or a claim on the payment of an order paid online, as Mercado Pago last reported it (Fase 24K): while it
   * is open Mercado Pago holds the money.
   */
  onlineDispute?: OnlinePaymentDispute | null;
  onlineDisputeUpdatedAt?: string | null;
  /** What the business forgave, each with its reason and who forgave it (Fase 26B). */
  writeOffs?: OrderWriteOffDto[];
  /** Dishes of the tab cancelled after they went to the kitchen, with the reason and who cancelled them. */
  itemCancellations?: OrderItemCancellationDto[];
  /** The order is (or was) a tab: rounds added over time, charged when the table leaves. */
  isTab?: boolean;
  /** A tab still taking rounds. */
  isOpenTab?: boolean;
  /** A counter sale closed on credit (fiado): its balance is what the customer owes. Still true once paid. */
  soldOnCredit?: boolean;
  diningTableId?: string | null;
  kitchenTickets?: KitchenTicketSummaryDto[];
  /** Only in the answer to a new sale: recipe supplies it took below zero ("Tortilla quedó en -3"). */
  stockWarnings?: string[];
  /** When the device captured it without connection; it reached the server later. */
  capturedOfflineAt?: string | null;
}

export interface OrderPlatformDto {
  /** The folio the platform's app shows for the order. */
  externalOrderNumber: string;
  /** The platform's long id of the order, when the business wrote it (what its statements name the order by). */
  externalOrderId?: string | null;
  /** The platform, in its app, or the business, when the courier pays on pickup. */
  collectedBy: PlatformCollectedBy;
}

/** What the sale of an appointment at the customer's address charged for the visit; in the total and IVA breakdown. */
export interface OrderVisitChargeDto {
  appointmentId: string;
  zoneName: string;
  fee: number;
  feeTaxRate: number;
}

/** Where an order goes and what it charged for it (Fase 22C); the fee is in the order's total and IVA breakdown. */
export interface OrderDeliveryDto {
  postalCode: string;
  addressLine: string;
  references?: string | null;
  /** The zone that reached the postal code, as it was named then. */
  zoneName: string;
  /** 0 when the delivery was free. */
  fee: number;
  feeTaxRate: number;
  /** The courier the business asked for the trip (Paquetenvia), with its folio and link. */
  courier?: DeliveryCourier | null;
  courierFolio?: string | null;
  trackingUrl?: string | null;
  shipmentRegisteredAt?: string | null;
  shipmentRegisteredBy?: string | null;
  /** The bill the customer pays the courier with, for the change (Fase 22D). */
  paysWith?: number | null;
  /** When it went out with the courier; none while it waits. */
  dispatchedAt?: string | null;
  dispatchedBy?: string | null;
  /** What the courier collects in cash: the balance when it went out; none when it collects nothing. */
  cashToCollect?: number | null;
  /** Delivered for the courier to collect and not paid yet: the courier owes the order's balance. */
  awaitsCourierCash: boolean;
}

export { DeliveryCourier } from '../api/generated/enums';

/** Where a new order goes: the postal code its zone is found by, the address and how to find it. */
export interface OrderDeliveryInput {
  postalCode: string;
  addressLine: string;
  references?: string;
  /** The bill the customer will pay the courier with, for the change (Fase 22D); it covers the total. */
  paysWith?: number;
}

/** The order goes out with the courier (Fase 22D): the trip, and whether the courier collects the balance in cash. */
export interface DispatchOrderRequest {
  courier: DeliveryCourier;
  /** As typed: the server normalizes it like the folio of a platform. */
  folio: string;
  /** https only. */
  trackingUrl?: string;
  collectsCash: boolean;
  /** Only when the courier collects: the bill the customer pays with. */
  paysWith?: number;
}

/** Writes, corrects or clears (empty) the platform's long id of an order of Rappi, Uber Eats or DiDi Food. */
export interface SetPlatformOrderIdRequest {
  /** As typed: the server normalizes it like the folio. */
  externalOrderId: string;
  /** Saves it although another order of the platform has it (409 possible_duplicate_order). */
  confirmPossibleDuplicate?: boolean;
}

export interface RegisterCourierShipmentRequest {
  courier: DeliveryCourier;
  /** As typed: the server normalizes it like the folio of a platform. */
  folio: string;
  /** https only. */
  trackingUrl?: string;
}

export interface OrderRefundDto {
  id: string;
  amount: number;
  method: OrderPaymentMethod;
  reason: string;
  refundedAt: string;
  refundedBy: string;
  reference?: string;
  restockItems: boolean;
  lines: Array<{ orderItemId: string; quantity: number }>;
  /** An «En línea» refund goes back through Mercado Pago (Fase 24I): asked, given back or refused. */
  onlineStatus?: OnlinePaymentRefundStatus | null;
  /** What Mercado Pago answered when it refused it. */
  onlineFailure?: string | null;
  /** Why it went back (Fase 24K): asked by the business, made in Mercado Pago, for a claim or a lost chargeback. */
  onlineReason?: OnlinePaymentRefundReason | null;
}

export { OrderWriteOffReason } from '../api/generated/enums';

/** What the business forgave of an order a customer owed (Fase 26B): no money moved. */
export interface OrderWriteOffDto {
  id: string;
  amount: number;
  reason: OrderWriteOffReason;
  notes?: string | null;
  writtenOffAt: string;
  writtenOffBy: string;
  /** When and by whom it was undone (owed again); null while it stands. */
  reversedAt?: string | null;
  reversedBy?: string | null;
}

/** Units of a dish of a tab cancelled after it went to the kitchen. */
export interface OrderItemCancellationDto {
  id: string;
  orderItemId: string;
  round: number;
  quantity: number;
  /** What the units took off the line, at its prices. */
  amount: number;
  reason: string;
  /** Its supplies went back to stock; otherwise they were lost. */
  returnedToStock: boolean;
  cancelledAt: string;
  cancelledBy: string;
}

export interface CancelTabItemRequest {
  orderItemId: string;
  /** Up to three decimals for dishes sold by weight. */
  quantity: number;
  reason: string;
  /** The kitchen did not prepare it: its supplies go back to stock. Otherwise they were lost. */
  returnToStock: boolean;
}

export interface RefundOrderRequest {
  amount: number;
  method: OrderPaymentMethod;
  reason: string;
  /** Re-enter the recipe supplies of the returned units. */
  restockItems: boolean;
  items: Array<{ orderItemId: string; quantity: number }>;
  reference?: string;
}

export interface VoidOrderRequest {
  reason: string;
  restockItems: boolean;
}

export interface CreateOrderItemRequest {
  productId: string;
  quantity: number;
  notes?: string;
  /** Variants and extras chosen (ids of the product's options). */
  optionIds?: string[];
}

export { OrderChannel } from '../api/generated/enums';

export { PlatformCollectedBy } from '../api/generated/enums';

export { PlatformCommissionBase } from '../api/generated/enums';

export { PlatformSettlementStatus } from '../api/generated/enums';

export { PlatformSettlementAdjustmentKind } from '../api/generated/enums';

/** What a folio of a platform's statement is in TienditApp (Fase 22H): the settlement takes it, or why not. */
export { PlatformStatementMatchStatus } from '../api/generated/enums';
export { CourierSettlementAdjustmentKind } from '../api/generated/enums';

export interface CustomerCardDto {
  phone?: string;
  name?: string;
  customerId?: string;
  email?: string;
  ordersCount: number;
  /** Paid minus refunded, over orders that were not cancelled. */
  totalSpent: number;
  averageTicket: number;
  /** What they owe: orders delivered or sold on credit and not paid (an order in progress is not owed yet). */
  balance: number;
  /** How much the business lets them owe, from the customer record; null without fiado. */
  creditLimit?: number | null;
  /** The business sells to them on credit now. */
  sellsOnCredit?: boolean;
  firstOrderAt?: string;
  lastOrderAt?: string;
  recentOrders: Array<{
    id: string;
    orderNumber: string;
    createdAt: string;
    status: OrderStatus;
    channel: OrderChannel;
    total: number;
    balance: number;
  }>;
  favoriteProducts: Array<{ productId: string; productName: string; quantity: number }>;
  upcomingAppointments: Array<{ id: string; serviceName: string; startAt: string; status: number }>;
  lastVisitAt?: string;
  optedOut: boolean;
  optedOutAt?: string;
  conversationId?: string;
}

export interface CreateOrderRequest {
  customerId?: string;
  customerName?: string;
  customerPhone?: string;
  customerNotes?: string;
  tableNumber?: string;
  createdByBusiness?: boolean;
  orderType?: OrderType;
  serviceDetail?: CreateServiceOrderDetailRequest;
  items: CreateOrderItemRequest[];
  /** Payments taken with the sale: stored with the order or not at all. */
  payments?: CreateOrderPaymentRequest[];
  /** Total shown to the customer; the API rejects the sale if a price changed meanwhile. */
  expectedTotal?: number;
  /** Counter (default), WhatsApp when the order is taken from a chat, or Rappi, Uber Eats or DiDi Food. */
  channel?: OrderChannel;
  /** The folio the delivery platform's app shows for the order (Rappi, Uber Eats or DiDi Food only). */
  externalOrderNumber?: string;
  /** The platform's long id of the order, optional (Rappi, Uber Eats or DiDi Food only). */
  externalOrderId?: string;
  /** Who charged an order of a delivery platform: the platform when omitted, or the business. */
  collectedBy?: PlatformCollectedBy;
  /**
   * Registers it although today there is an order of the same platform with the same folio, or ever one with the same
   * long id (409 possible_duplicate_order).
   */
  confirmPossibleDuplicate?: boolean;
  /** Opens the order as a tab: its items are the first round sent to the kitchen. */
  openTab?: boolean;
  diningTableId?: string;
  /** Branch where the sale is made; the cashier's drawer branch or the default one when omitted. */
  branchId?: string;
  /** Key of this sale on the device: sending it again returns the same sale instead of charging twice. */
  idempotencyKey?: string;
  /** When the device captured it without connection. */
  capturedOfflineAt?: string;
  /** Sent to the customer's address: the zone that reaches the postal code charges its fee (never offline or for a tab). */
  delivery?: OrderDeliveryInput;
  /** The appointment of the agenda this sale charges (Fase 22E): done with it, and its visit charge in the total. */
  appointmentId?: string;
  /**
   * Sold on credit (fiado): what the payments leave unpaid is what the customer owes. Only a counter sale with
   * connection to a customer the business sells to on credit, within their limit (the server checks it).
   */
  onCredit?: boolean;
}

export interface CreateOrderPaymentRequest {
  amount: number;
  method: OrderPaymentMethod;
  inventoryLocationId?: string;
  evidenceUrl?: string;
  reference?: string;
  notes?: string;
  /** Tip on top of the amount; never larger than the amount. */
  tip?: number;
  /** Cash handed by the customer, to store the change (cash payments only). */
  cashReceived?: number;
}

export interface CreateServiceOrderPieceRequest {
  itemName: string;
  serviceProductId?: string;
  isTrackable?: boolean;
  notifyCustomer?: boolean;
  brand?: string;
  model?: string;
  color?: string;
  size?: string;
  material?: string;
  conditionNotes?: string;
  requestedService?: string;
  internalNotes?: string;
}

export interface CreateServiceOrderDetailRequest {
  receivedAt?: string;
  promisedAt?: string;
  priority?: number;
  intakeNotes?: string;
  deliveryNotes?: string;
  intakeChecklistJson?: string;
  beforePhotosJson?: string;
  afterPhotosJson?: string;
  pieces: CreateServiceOrderPieceRequest[];
}

export interface AddOrderPaymentRequest {
  amount: number;
  method: OrderPaymentMethod;
  inventoryLocationId?: string;
  /** Must be a URL returned by the evidence upload endpoint. */
  evidenceUrl?: string;
  reference?: string;
  notes?: string;
  /** Tip on top of the amount; never larger than the amount. */
  tip?: number;
  /** Cash handed by the customer, to store the change (cash payments only). */
  cashReceived?: number;
}

export interface UploadedFileDto {
  url: string;
  fileName: string;
  contentType: string;
}

export interface TopSellingItemDto {
  productId: string;
  productName: string;
  itemType: 'Product' | 'Service';
  quantitySold: number;
  totalAmount: number;
}

export interface DashboardMetricsDto {
  rangeFrom: string;
  rangeTo: string;
  totalOrders: number;
  inProgressOrders: number;
  pendingOrders: number;
  completedOrders: number;
  cancelledOrders: number;
  uniqueCustomers: number;
  activeProducts: number;
  activeServices: number;
  /** Payments registered in the range, whatever the status of their orders. */
  totalRevenue: number;
  /** The part of totalRevenue the delivery platforms charged in their apps, minus what went back as «Plataforma». */
  platformCollected?: number;
  /** The part of totalRevenue customers paid online in Mercado Pago, minus what went back as «En línea» (Fase 24I). */
  onlineCollected?: number;
  /** Revenue divided by the orders that received those payments. */
  averageOrderValue: number;
  paidOrders: number;
  /** Balance still owed by the orders of the range that are not cancelled. */
  outstandingBalance: number;
  unpaidOrders: number;
  ordersByStatus: Record<string, number>;
  topSellingItems: TopSellingItemDto[];
}

export interface KitchenOrderDto {
  id: string;
  orderNumber: string;
  /** Where the order came from: an order of a delivery platform is packed for its courier. */
  channel?: OrderChannel;
  /** The folio of the delivery platform's app, written on the bag; null for any other channel. */
  externalOrderNumber?: string | null;
  /** It goes out to the customer's address (never where: the kitchen only packs it). */
  isDelivery?: boolean;
  status: OrderStatus;
  customerName: string;
  tableNumber?: string;
  customerNotes?: string;
  createdAt: string;
  items: OrderItemDto[];
}

// ==================== AI Agents ====================

export interface AiAgentDto {
  id: string;
  name: string;
  role: string;
  personality: string;
  language: string;
  tone: string;
  businessName?: string;
  businessDescription?: string;
  businessProductsAndServices?: string;
  welcomeMessage: string;
  fallbackMessage: string;
  closingMessage: string;
  askForName: boolean;
  askForPhone: boolean;
  askForEmail: boolean;
  askForAddress: boolean;
  customQuestions?: string;
  model: string;
  temperature: number;
  maxTokens: number;
  useConversationMemory: boolean;
  strictMode: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateAiAgentRequest {
  name: string;
  role: string;
  personality: string;
  language: string;
  tone: string;
  businessName?: string;
  businessDescription?: string;
  businessProductsAndServices?: string;
  welcomeMessage: string;
  fallbackMessage: string;
  closingMessage: string;
  askForName: boolean;
  askForPhone: boolean;
  askForEmail: boolean;
  askForAddress: boolean;
  customQuestions?: string;
  model: string;
  temperature: number;
  maxTokens: number;
  useConversationMemory: boolean;
  strictMode: boolean;
}

export interface UpdateAiAgentRequest extends CreateAiAgentRequest {
  id: string;
}
