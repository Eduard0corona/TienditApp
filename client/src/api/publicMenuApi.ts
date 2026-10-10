import { apiRequest, type Schemas } from './typedClient';

export type PublicMenu = Schemas['PublicMenuDto'];
export type PublicMenuProduct = Schemas['PublicMenuProductDto'];
export type PublicOrderReceipt = Schemas['PublicOrderReceiptDto'];
export type PlacePublicOrder = Schemas['PlacePublicOrderRequest'];

/** The public menu of a business: no sign-in. */
export const publicMenuApi = {
  /** From the QR of one of its tables the customer is already seated: the API takes the order outside the hours. */
  getMenu: (slug: string, table?: string) =>
    apiRequest('get', '/api/v1/public/menu/{slug}', { path: { slug }, query: table ? { table } : undefined }),

  placeOrder: (slug: string, body: PlacePublicOrder) =>
    apiRequest('post', '/api/v1/public/menu/{slug}/orders', { path: { slug }, body }),
};
