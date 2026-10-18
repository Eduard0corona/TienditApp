import { apiRequest, type Schemas } from './typedClient';

export type AccountAddress = Schemas['AccountAddressDto'];
export type AccountAddressInput = Schemas['AccountAddressRequest'];
export type MyOrder = Schemas['MyOrderDto'];
export type MyOrderDetail = Schemas['MyOrderDetailDto'];
export type MyOrdersScope = Schemas['MyOrdersScope'];
export type MyAppointmentsScope = Schemas['MyAppointmentsScope'];
export type MyFavoriteBusiness = Schemas['MyFavoriteBusinessDto'];

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

  /** The appointments the account booked online, in every business (Fase 24D): upcoming soonest first, or the ones before. */
  getAppointments: (scope: MyAppointmentsScope, pageNumber: number, pageSize: number, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/me/appointments', { query: { scope, pageNumber, pageSize }, signal }),

  /** Cancels one of them before it starts; any other is not found (404). */
  cancelAppointment: (id: string) => apiRequest('post', '/api/v1/me/appointments/{id}/cancel', { path: { id } }),

  /**
   * The businesses the account keeps among its favorites (Fase 24F), the last one first: each with its card in Explorar
   * while it shows there, answering for the postal code given.
   */
  getFavorites: (postalCode: string | undefined, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/me/favorites', { query: { postalCode }, signal }),

  /** Keeps a business by the slug of its store; keeping it again changes nothing. A store not published is not found. */
  saveFavorite: (slug: string) => apiRequest('put', '/api/v1/me/favorites/{slug}', { path: { slug } }),

  /** Takes it out; one that was not there changes nothing. */
  removeFavorite: (slug: string) => apiRequest('delete', '/api/v1/me/favorites/{slug}', { path: { slug } }),
};
