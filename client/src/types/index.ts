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

// ==================== Registration ====================

export enum BusinessType {
  Food = 0,
  Retail = 1,
  Service = 2,
}

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

export enum UnitType {
  Unit = 0,
  Pack = 1,
  Box = 2,
  Pair = 3,
  Kg = 10,
  G = 11,
  Lb = 12,
  Oz = 13,
  Liter = 20,
  Ml = 21,
  Portion = 30,
  Slice = 31,
  Serving = 32,
  Meter = 40,
  Roll = 41,
  Hour = 50,
  Session = 51,
  Service = 52,
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
