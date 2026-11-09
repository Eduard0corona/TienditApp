// Customers: the customer, their card and their fiscal data.

import type { OrderChannel, OrderStatus } from '../api/generated/enums';

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

/** RFC, name, fiscal postal code, regime and CFDI use, as the customer's constancia says (Fase 28A). */
export interface CustomerFiscalData {
  rfc: string;
  name: string;
  postalCode: string;
  fiscalRegime: string;
  cfdiUse: string;
}

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
