// Purchasing: suppliers and purchase orders.

import type { PurchaseOrderStatus, UnitType } from '../api/generated/enums';

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
