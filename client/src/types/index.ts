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

export interface EmployeePermissionOptionDto {
  code: string;
  name: string;
  description: string;
}

export interface CreateEmployeeRequest {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
  appRole: number;
  tenantRole: number;
  permissions: string[];
}

export interface UpdateEmployeeRequest {
  tenantUserId: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
  appRole: number;
  tenantRole: number;
  permissions: string[];
}

export interface MyTenantDto {
  tenantId: string;
  name: string;
  slug: string;
  role: string;
  status: string;
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
  phoneNumber?: string;
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
  capabilities: TenantCapability;
  createdAt: string;
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

export const BusinessType = {
  Food: 0,
  Retail: 1,
  Service: 2,
} as const;
export type BusinessType = (typeof BusinessType)[keyof typeof BusinessType];

export const TenantCapability = {
  Retail: 1,
  Service: 2,
  Food: 4,
  Inventory: 8,
  Appointments: 16,
  Kitchen: 32,
} as const;
export type TenantCapability = number;

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

export const UnitType = {
  Unit: 0,
  Pack: 1,
  Box: 2,
  Pair: 3,
  Kg: 10,
  G: 11,
  Lb: 12,
  Oz: 13,
  Liter: 20,
  Ml: 21,
  Portion: 30,
  Slice: 31,
  Serving: 32,
  Meter: 40,
  Roll: 41,
  Hour: 50,
  Session: 51,
  Service: 52,
} as const;
export type UnitType = (typeof UnitType)[keyof typeof UnitType];

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
  taxRate?: number;
  hasDiscount: boolean;
  imageUrl?: string;
  images?: ProductImageUrlsDto;
  isAvailable: boolean;
  isFeatured: boolean;
  preparationTimeMinutes?: number;
  calories?: number;
  allergens?: string;
  stockQuantity?: number;
  lowStockThreshold?: number;
  maxQuantityPerOrder?: number;
  availableFromTime?: string;
  availableToTime?: string;
  isLowStock: boolean;
  isTrackable: boolean;
  notifyCustomer: boolean;
}

export interface CategoryDto {
  id: string;
  name: string;
  description?: string;
  sortOrder: number;
  isActive: boolean;
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
  taxRate?: number;
  calories?: number;
  allergens?: string;
  stockQuantity?: number;
  lowStockThreshold?: number;
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
export const InventoryMovementType = {
  Entry: 1,
  Exit: 2,
  AdjustmentIncrease: 3,
  AdjustmentDecrease: 4,
  TransferIn: 5,
  TransferOut: 6,
  ReturnFromProduction: 7,
  ReturnToSupplier: 8,
} as const;
export type InventoryMovementType = (typeof InventoryMovementType)[keyof typeof InventoryMovementType];

export const InventoryMovementReason = {
  Purchase: 1,
  Sale: 2,
  Production: 3,
  Consumption: 4,
  Waste: 5,
  Damaged: 6,
  InventoryCount: 7,
  InitialLoad: 8,
  Return: 9,
  ManualAdjustment: 10,
  Other: 99,
} as const;
export type InventoryMovementReason = (typeof InventoryMovementReason)[keyof typeof InventoryMovementReason];

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
}

export interface UpdateInventoryLocationRequest extends CreateInventoryLocationRequest {
  id: string;
}

export type BranchDto = InventoryLocationDto;
export type CreateBranchRequest = CreateInventoryLocationRequest;
export type UpdateBranchRequest = UpdateInventoryLocationRequest;

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

export type PurchaseOrderStatus = 0 | 1 | 2 | 3 | 99;

export const PurchaseOrderStatus = {
  Draft: 0,
  Sent: 1,
  PartiallyReceived: 2,
  Received: 3,
  Cancelled: 99,
} as const;

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
}

export interface ReceivePurchaseOrderRequest {
  receivedAt?: string;
  lines: ReceivePurchaseOrderLineRequest[];
}

// ==================== Cash ====================

export type CashSessionStatus = 0 | 1;
export type CashTransactionType = 0 | 1 | 2 | 3 | 4 | 5 | 6;

export const CashSessionStatus = {
  Open: 0,
  Closed: 1,
} as const;

export const CashTransactionType = {
  SalePayment: 0,
  Refund: 1,
  Expense: 2,
  Withdrawal: 3,
  Deposit: 4,
  OpeningAdjustment: 5,
  ClosingAdjustment: 6,
} as const;

export interface CashSessionDto {
  id: string;
  openedAt: string;
  openedBy: string;
  openingAmount: number;
  closedAt?: string;
  closedBy?: string;
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
  userName: string;
  paymentsCount: number;
  totalAmount: number;
}

export interface DailyCashCutByLocationDto {
  inventoryLocationId?: string;
  locationName: string;
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
  otherTotal: number;
  manualIncomeTotal: number;
  manualExpenseTotal: number;
  netCashFlow: number;
  /** Cash payments plus manual income minus manual expenses: what the drawers should hold. */
  netCashOnlyFlow: number;
  byUser: DailyCashCutByUserDto[];
  byLocation: DailyCashCutByLocationDto[];
}

export interface OpenCashSessionRequest {
  openingAmount: number;
  notes?: string;
}

export interface CloseCashSessionRequest {
  closingAmount: number;
  notes?: string;
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

export type AppointmentStatus = 0 | 1 | 2 | 3 | 4 | 99;

export const AppointmentStatus = {
  Scheduled: 0,
  Confirmed: 1,
  InService: 2,
  Completed: 3,
  NoShow: 4,
  Cancelled: 99,
} as const;

export interface AppointmentDto {
  id: string;
  customerName: string;
  customerPhone: string;
  serviceName: string;
  staffName?: string;
  startAt: string;
  endAt: string;
  status: AppointmentStatus;
  notes?: string;
}

export interface CreateAppointmentRequest {
  customerName: string;
  customerPhone: string;
  serviceName: string;
  staffName?: string;
  startAt: string;
  endAt: string;
  notes?: string;
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

export interface SendPromotionRequest {
  message: string;
  minimumCompletedOrders: number;
  lastPurchaseFrom?: string;
}

// ==================== Customers ====================

export interface CustomerDto {
  id: string;
  name: string;
  phone?: string;
  email?: string;
  isPublicGeneral: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateCustomerRequest {
  name: string;
  phone: string;
  email?: string;
}

export interface UpdateCustomerRequest extends CreateCustomerRequest {
  id: string;
}

// ==================== Orders ====================

export type OrderStatus = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 99;
export type OrderType = 0 | 1;
export type ServiceOrderPieceStatus = 0 | 1 | 2 | 99;
export type OrderPaymentMethod = 0 | 1 | 2 | 3 | 4;

export const OrderStatus = {
  Pending: 0,
  Confirmed: 1,
  InPreparation: 2,
  Ready: 3,
  Accepted: 4,
  InProgress: 5,
  Delivered: 6,
  Completed: 7,
  Cancelled: 99,
} as const;

export const OrderType = {
  Retail: 0,
  Service: 1,
} as const;

export const OrderPaymentMethod = {
  Cash: 0,
  Card: 1,
  Transfer: 2,
  DigitalWallet: 3,
  Other: 4,
} as const;

export const ServiceOrderPieceStatus = {
  Accepted: 0,
  InProgress: 1,
  Completed: 2,
  Cancelled: 99,
} as const;

export interface OrderItemDto {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  notes?: string;
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
  amount: number;
  method: OrderPaymentMethod;
  paidAt: string;
  inventoryLocationId?: string;
  evidenceUrl?: string;
  reference?: string;
  notes?: string;
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
  subTotal: number;
  tax: number;
  total: number;
  paidAmount: number;
  balance: number;
  cancellationReason?: string;
  createdAt: string;
  updatedAt?: string;
  items: OrderItemDto[];
  statusHistory: OrderStatusHistoryDto[];
  serviceDetail?: ServiceOrderDetailDto;
  servicePieces: ServiceOrderPieceDto[];
  payments: OrderPaymentDto[];
}

export interface CreateOrderItemRequest {
  productId: string;
  quantity: number;
  notes?: string;
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
  totalRevenue: number;
  averageOrderValue: number;
  ordersByStatus: Record<string, number>;
  topSellingItems: TopSellingItemDto[];
}

export interface KitchenOrderDto {
  id: string;
  orderNumber: string;
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
