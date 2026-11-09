// Cash: sessions, transactions and the daily cut.

import type { CashSessionStatus, CashTransactionType, OrderPaymentMethod } from '../api/generated/enums';

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
