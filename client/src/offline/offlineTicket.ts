import type { OrderDto, OrderPaymentMethod, OrderTaxLineDto, UnitType } from '../types';
import { BusinessType, OrderStatus, OrderType } from '../types';

export interface OfflineTicketLine {
  key: string;
  productId: string;
  name: string;
  optionsLabel: string;
  quantity: number;
  unitPrice: number;
  notes: string;
  ratePercent: number;
  isExempt: boolean;
  unit?: UnitType;
}

interface OfflineTicketInput {
  folio: string;
  capturedAt: string;
  lines: OfflineTicketLine[];
  totals: { subtotal: number; tax: number; total: number; breakdown: OrderTaxLineDto[] };
  pricesIncludeTax: boolean;
  payments: Array<{ amount: number; method: OrderPaymentMethod; reference?: string; tip?: number; cashReceived?: number }>;
  customerName?: string;
  tableNumber?: string;
}

const centsOf = (quantity: number, unitPrice: number) => Math.round(quantity * unitPrice * 100) / 100;

/**
 * The ticket of a sale charged without connection, for the customer, built on the device with the same totals
 * the screen showed. Display only: the server computes the sale again when it receives it and gives the real folio.
 */
export function buildOfflineTicketOrder(input: OfflineTicketInput): OrderDto {
  const paid = input.payments.reduce((sum, payment) => sum + payment.amount, 0);
  return {
    id: input.folio,
    createdByBusiness: true,
    orderNumber: input.folio,
    businessType: BusinessType.Retail,
    orderType: OrderType.Retail,
    status: OrderStatus.Completed,
    canCancel: false,
    allowedTransitions: [],
    customerName: input.customerName || 'Público en general',
    tableNumber: input.tableNumber,
    pricesIncludeTax: input.pricesIncludeTax,
    subTotal: input.totals.subtotal,
    tax: input.totals.tax,
    total: input.totals.total,
    taxBreakdown: input.totals.breakdown,
    paidAmount: paid,
    balance: Math.max(0, Math.round((input.totals.total - paid) * 100) / 100),
    createdAt: input.capturedAt,
    capturedOfflineAt: input.capturedAt,
    items: input.lines.map((line) => ({
      id: line.key,
      productId: line.productId,
      productName: line.name,
      quantity: line.quantity,
      unit: line.unit,
      unitPrice: line.unitPrice,
      totalPrice: centsOf(line.quantity, line.unitPrice),
      notes: line.notes || undefined,
      taxRate: line.isExempt ? 0 : line.ratePercent,
      isTaxExempt: line.isExempt,
      options: line.optionsLabel
        ? line.optionsLabel.split(', ').map((name, index) => ({ optionId: `${line.key}-${index}`, groupName: '', optionName: name, priceDelta: 0 }))
        : [],
    })),
    statusHistory: [],
    servicePieces: [],
    tipTotal: input.payments.reduce((sum, payment) => sum + (payment.tip ?? 0), 0),
    payments: input.payments.map((payment, index) => ({
      id: `${input.folio}-${index}`,
      amount: payment.amount,
      method: payment.method,
      paidAt: input.capturedAt,
      reference: payment.reference,
      tip: payment.tip,
      cashReceived: payment.cashReceived,
      change: payment.cashReceived !== undefined
        ? Math.round((payment.cashReceived - payment.amount - (payment.tip ?? 0)) * 100) / 100
        : 0,
    })),
  };
}
