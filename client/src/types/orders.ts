// Orders: sales and service orders, payments, refunds, the kitchen, delivery and the delivery platforms.

import type {
  BusinessType, CfdiTaxObject, DeliveryCourier, KitchenTicketStatus, OnlinePaymentDispute, OnlinePaymentRefundReason,
  OnlinePaymentRefundStatus, OrderChannel, OrderPaymentMethod, OrderStatus, OrderType, OrderWriteOffReason,
  PlatformCollectedBy, ServiceOrderPieceStatus, ServicePhotoStage, UnitType,
} from '../api/generated/enums';
import type { WorkAppointmentDto } from './appointments';

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
  /** The accepted quote the service order came from (Fase 29E), to open it. */
  quote?: OrderQuoteDto | null;
  /** The appointments that do its job (Fase 29F), in the order of their time. */
  workAppointments?: WorkAppointmentDto[];
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
  /** For an order of the online menu still «Por confirmar», until when its answer keeps it out of the slowest (Fase 29X). */
  confirmBy?: string | null;
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

/** The accepted quote a service order came from (Fase 29E): its folio and version. */
export interface OrderQuoteDto {
  id: string;
  number: string;
  revision: number;
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
