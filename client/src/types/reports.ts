// Reports: the operational report, the metrics of Inicio and the activity of the business (audit).

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

export interface AuditLogDto {
  id: string;
  entityName: string;
  entityId: string;
  action: string;
  performedBy: string;
  timestamp: string;
  metadata?: string;
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
  topSellingItems: TopSellingItemDto[];
}
