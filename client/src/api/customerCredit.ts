import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ReceivablesSort } from './generated/enums';
import { apiRequest, type Schemas } from './typedClient';

// Fiado (Fase 26A): Clientes › Por cobrar, the account of a customer, its payments and the limit; what the business
// forgives and the term of its fiado (Fase 26B); a customer's own term and the reminders of what is overdue (Fase 26C).
// Kept out of queries.ts, which the first visit loads.

export type Receivable = Schemas['ReceivableDto'];
export type Receivables = Schemas['ReceivablesDto'];
export type CustomerAccount = Schemas['CustomerAccountDto'];
export type PayCustomerAccountBody = Schemas['PayCustomerAccountCommand'];
export type WriteOffCustomerAccountBody = Schemas['WriteOffCustomerAccountCommand'];
export type CustomerAccountOrder = Schemas['CustomerAccountOrderDto'];
export type CustomerAccountWriteOff = Schemas['CustomerAccountWriteOffDto'];
export type CreditReminders = Schemas['CreditRemindersDto'];
export type UpdateCreditRemindersBody = Schemas['UpdateCreditRemindersCommand'];

/** Whose account: the customer record, or only the phone of the orders. */
export interface AccountOf {
  customerId?: string | null;
  phone?: string | null;
}

export interface ReceivablesFilter {
  search: string;
  sort: ReceivablesSort;
  /** Only who has something past the term of the fiado. */
  overdueOnly: boolean;
  page: number;
  pageSize: number;
}

/** Cache keys of what customers owe: selling on credit, a payment or a new limit read them again. */
export const creditKeys = {
  all: ['customer-credit'] as const,
  receivables: (filter: ReceivablesFilter) => ['customer-credit', 'receivables', filter] as const,
  account: (of: AccountOf) => ['customer-credit', 'account', of.customerId ?? '', of.phone ?? ''] as const,
  reminders: ['customer-credit', 'reminders'] as const,
};

/** Who owes the business, a row per customer, with the total. */
export function useReceivables(filter: ReceivablesFilter) {
  return useQuery({
    queryKey: creditKeys.receivables(filter),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/customers/receivables', {
      query: {
        search: filter.search || undefined,
        sort: filter.sort,
        overdueOnly: filter.overdueOnly || undefined,
        pageNumber: filter.page,
        pageSize: filter.pageSize,
      },
      signal,
    }),
    placeholderData: keepPreviousData,
  });
}

/** The account of a customer: limit, what they owe and may still owe, the orders owed and their latest payments. */
export function useCustomerAccount(of: AccountOf, enabled = true) {
  return useQuery({
    queryKey: creditKeys.account(of),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/customers/account', {
      query: { customerId: of.customerId ?? undefined, phone: of.phone ?? undefined },
      signal,
    }),
    enabled: enabled && Boolean(of.customerId || of.phone),
    retry: false,
  });
}

/** What a sale on credit, a payment or a limit changes: the lists, the accounts and the customer cards. */
function useRefreshCredit() {
  const client = useQueryClient();
  return () => {
    void client.invalidateQueries({ queryKey: creditKeys.all });
    void client.invalidateQueries({ queryKey: ['customers'] });
  };
}

/** A payment to the account, split over the orders owed from the oldest, into the drawer of whoever charges. */
export function usePayCustomerAccount() {
  const refresh = useRefreshCredit();
  return useMutation({
    mutationFn: (body: PayCustomerAccountBody) => apiRequest('post', '/api/v1/customers/account/payments', { body }),
    onSettled: refresh,
  });
}

/**
 * Forgives part or all of what the customer owes (Fase 26B), on one order or from the oldest: no money moves, and each
 * part stays on its order with the reason (owners and admins).
 */
export function useWriteOffCustomerAccount() {
  const refresh = useRefreshCredit();
  return useMutation({
    mutationFn: (body: WriteOffCustomerAccountBody) => apiRequest('post', '/api/v1/customers/account/write-offs', { body }),
    onSettled: refresh,
  });
}

/**
 * Undoes a write-off made by mistake (Fase 26B): its record stays, marked as undone, and what it forgave is owed again
 * (owners and admins). Sent twice, it undoes it once.
 */
export function useReverseWriteOff() {
  const refresh = useRefreshCredit();
  return useMutation({
    mutationFn: (writeOffId: string) =>
      apiRequest('post', '/api/v1/customers/account/write-offs/{id}/reversal', { path: { id: writeOffId } }),
    onSettled: refresh,
  });
}

/** How many days the business gives to pay what customers owe; null takes the term away (owners and admins). */
export function useUpdateCreditTerm() {
  const refresh = useRefreshCredit();
  return useMutation({
    mutationFn: (creditTermDays: number | null) =>
      apiRequest('put', '/api/v1/customers/credit-term', { body: { creditTermDays } }),
    onSuccess: refresh,
  });
}

/** The customer's own term to pay; null gives them the business's again (Fase 26C; owners and admins). */
export function useSetCustomerCreditTerm() {
  const refresh = useRefreshCredit();
  return useMutation({
    mutationFn: ({ customerId, creditTermDays }: { customerId: string; creditTermDays: number | null }) =>
      apiRequest('put', '/api/v1/customers/{id}/credit-term', { path: { id: customerId }, body: { creditTermDays } }),
    onSuccess: refresh,
  });
}

/** The reminders of what is overdue and what setting them up needs (Fase 26C; owners and admins). */
export function useCreditReminders(enabled = true) {
  return useQuery({
    queryKey: creditKeys.reminders,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/customers/credit-reminders', { signal }),
    enabled,
  });
}

/** Turns the WhatsApp reminders of what is overdue on or off, every how many days and with which template (Fase 26C). */
export function useUpdateCreditReminders() {
  const client = useQueryClient();
  const refresh = useRefreshCredit();
  return useMutation({
    mutationFn: (body: UpdateCreditRemindersBody) => apiRequest('put', '/api/v1/customers/credit-reminders', { body }),
    onSuccess: (reminders) => {
      client.setQueryData(creditKeys.reminders, reminders);
      refresh();
    },
  });
}

/** How much the business lets the customer owe; null takes their fiado away (owners and admins). */
export function useUpdateCreditLimit() {
  const refresh = useRefreshCredit();
  return useMutation({
    mutationFn: ({ customerId, creditLimit }: { customerId: string; creditLimit: number | null }) =>
      apiRequest('put', '/api/v1/customers/{id}/credit-limit', { path: { id: customerId }, body: { creditLimit } }),
    onSuccess: refresh,
  });
}

/** Lets a sale on credit read the account again (what the customer owes changed). */
export function useRefreshCustomerCredit() {
  return useRefreshCredit();
}
