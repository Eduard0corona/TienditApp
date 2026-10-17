import { apiRequest, type Schemas } from './typedClient';

export type PublicMenu = Schemas['PublicMenuDto'];
export type PublicMenuProduct = Schemas['PublicMenuProductDto'];
export type PublicOrderReceipt = Schemas['PublicOrderReceiptDto'];
export type PlacePublicOrder = Schemas['PlacePublicOrderRequest'];
export type PublicDeliveryQuote = Schemas['PublicDeliveryQuoteDto'];
export type PublicOrderTracking = Schemas['PublicOrderTrackingDto'];

/** The public menu of a business: no sign-in. */
export const publicMenuApi = {
  /** From the QR of one of its tables the customer is already seated: the API takes the order outside the hours. */
  getMenu: (slug: string, table?: string) =>
    apiRequest('get', '/api/v1/public/menu/{slug}', { path: { slug }, query: table ? { table } : undefined }),

  /** Whether the business delivers to a postal code and what it charges (Fase 22C); never the zone's name. */
  getDeliveryQuote: (slug: string, postalCode: string) =>
    apiRequest('get', '/api/v1/public/menu/{slug}/delivery-quote', { path: { slug }, query: { postalCode } }),

  placeOrder: (slug: string, body: PlacePublicOrder) =>
    apiRequest('post', '/api/v1/public/menu/{slug}/orders', { path: { slug }, body }),

  /**
   * An online order followed with the link of its receipt (Fase 24C): the link goes in the body, never in the URL, so no
   * server log keeps it. Unknown or expired, it is not found (404).
   */
  trackOrder: (token: string, signal?: AbortSignal) =>
    apiRequest('post', '/api/v1/public/orders/tracking', { body: { token }, signal }),
};
