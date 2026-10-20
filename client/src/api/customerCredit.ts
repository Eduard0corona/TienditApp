import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ReceivablesSort } from './generated/enums';
import { apiRequest, type Schemas } from './typedClient';

// Fiado (Fase 26A): Clientes › Por cobrar, the account of a customer, its payments and the limit. Kept out of queries.ts,
// which the first visit loads.

export type Receivable = Schemas['ReceivableDto'];
export type Receivables = Schemas['ReceivablesDto'];
export type CustomerAccount = Schemas['CustomerAccountDto'];
export type PayCustomerAccountBody = Schemas['PayCustomerAccountCommand'];

/** Whose account: the customer record, or only the phone of the orders. */
export interface AccountOf {
  customerId?: string | null;
  phone?: string | null;
}

export interface ReceivablesFilter {
  search: string;
  sort: ReceivablesSort;
  page: number;
  pageSize: number;
}

/** Cache keys of what customers owe: selling on credit, a payment or a new limit read them again. */
export const creditKeys = {
  all: ['customer-credit'] as const,
  receivables: (filter: ReceivablesFilter) => ['customer-credit', 'receivables', filter] as const,
  account: (of: AccountOf) => ['customer-credit', 'account', of.customerId ?? '', of.phone ?? ''] as const,
};

/** Who owes the business, a row per customer, with the total. */
export function useReceivables(filter: ReceivablesFilter) {
  return useQuery({
    queryKey: creditKeys.receivables(filter),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/customers/receivables', {
      query: {
        search: filter.search || undefined,
        sort: filter.sort,
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
