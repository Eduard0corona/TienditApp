export interface AuthResponse {
  accessToken?: string;
  refreshToken?: string;
  expiresAt?: string;
  requiresTwoFactor: boolean;
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
  tenantId: string;
  isActive: boolean;
  avatarUrl?: string;
  avatarImages?: AvatarImageUrlsDto;
  twoFactorEnabled: boolean;
  createdAt: string;
}

export interface LoginRequest {
  email: string;
  password: string;
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
}

export interface CategoryDto {
  id: string;
  name: string;
  description?: string;
  sortOrder: number;
  isActive: boolean;
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

export const InventoryMovementType = {
  Entry: 0,
  Exit: 1,
  AdjustmentIncrease: 2,
  AdjustmentDecrease: 3,
  TransferIn: 4,
  TransferOut: 5,
  ReturnFromProduction: 6,
  ReturnToSupplier: 7,
} as const;
export type InventoryMovementType = (typeof InventoryMovementType)[keyof typeof InventoryMovementType];

export const InventoryMovementReason = {
  Purchase: 0,
  Sale: 1,
  Production: 2,
  Consumption: 3,
  Waste: 4,
  Damaged: 5,
  InventoryCount: 6,
  InitialLoad: 7,
  Return: 8,
  ManualAdjustment: 9,
  Other: 10,
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
}

export interface InventoryAlertsDto {
  lowStockCount: number;
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
  inflows: number;
  outflows: number;
  expectedBalance: number;
}

export interface CashTransactionDto {
  id: string;
  cashSessionId: string;
  transactionType: CashTransactionType;
  amount: number;
  occurredAt: string;
  paymentMethod?: OrderPaymentMethod;
  orderId?: string;
  reference?: string;
  notes?: string;
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
  amount: number;
  occurredAt?: string;
  paymentMethod?: OrderPaymentMethod;
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
  totalRevenue: number;
  totalOrderPayments: number;
  totalPurchases: number;
  totalCogs: number;
  grossProfit: number;
  ordersCompleted: number;
  appointmentsCompleted: number;
  appointmentsScheduled: number;
  revenueByPaymentMethod: Record<string, number>;
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
export type ServiceOrderPieceStatus = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 99;
export type OrderPaymentMethod = 0 | 1 | 2 | 3 | 4;

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
  Received: 0,
  Inspection: 1,
  Washing: 2,
  Drying: 3,
  Detailing: 4,
  Ready: 5,
  Delivered: 6,
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

export interface OrderPaymentDto {
  id: string;
  amount: number;
  method: OrderPaymentMethod;
  paidAt: string;
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
  paidAt?: string;
  reference?: string;
  notes?: string;
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
