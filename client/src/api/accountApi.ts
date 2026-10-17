import { apiRequest, type Schemas } from './typedClient';

export type AccountAddress = Schemas['AccountAddressDto'];
export type AccountAddressInput = Schemas['AccountAddressRequest'];
export type MyOrder = Schemas['MyOrderDto'];
export type MyOrderDetail = Schemas['MyOrderDetailDto'];
export type MyOrdersScope = Schemas['MyOrdersScope'];

/** The addresses a customer account keeps for its orders (Fase 24B): ten at most. */
export const MAX_ACCOUNT_ADDRESSES = 10;

/** What a customer account keeps: its addresses and its orders in every business. */
export const accountApi = {
  getAddresses: (signal?: AbortSignal) => apiRequest('get', '/api/v1/me/addresses', { signal }),

  /** The same place again (postal code and line) is not a second address: it takes the new name and references. */
  saveAddress: (body: AccountAddressInput) => apiRequest('post', '/api/v1/me/addresses', { body }),

  updateAddress: (id: string, body: AccountAddressInput) =>
    apiRequest('put', '/api/v1/me/addresses/{id}', { path: { id }, body }),

  deleteAddress: (id: string) => apiRequest('delete', '/api/v1/me/addresses/{id}', { path: { id } }),

  /** The account's orders in every business, newest first: in progress, the ones before, or all (Fase 24C). */
  getOrders: (scope: MyOrdersScope, pageNumber: number, pageSize: number, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/me/orders', { query: { scope, pageNumber, pageSize }, signal }),

  /** One order of the account, step by step; any other is not found (404). */
  getOrder: (id: string, signal?: AbortSignal) => apiRequest('get', '/api/v1/me/orders/{id}', { path: { id }, signal }),
};
