import { apiRequest, type Schemas } from './typedClient';

export type PublicMenu = Schemas['PublicMenuDto'];
export type PublicMenuProduct = Schemas['PublicMenuProductDto'];
export type PublicOrderReceipt = Schemas['PublicOrderReceiptDto'];
export type PlacePublicOrder = Schemas['PlacePublicOrderRequest'];
export type PublicDeliveryQuote = Schemas['PublicDeliveryQuoteDto'];
export type PublicOrderTracking = Schemas['PublicOrderTrackingDto'];
export type PublicBooking = Schemas['PublicBookingDto'];
export type PublicBookingService = Schemas['PublicBookingServiceDto'];
export type PublicBookingSlots = Schemas['PublicBookingSlotsDto'];
export type PublicBookingSlot = Schemas['PublicBookingSlotDto'];
export type RequestPublicAppointment = Schemas['RequestPublicAppointmentRequest'];
export type PublicAppointmentReceipt = Schemas['PublicAppointmentReceiptDto'];
export type CustomerAppointment = Schemas['CustomerAppointmentDto'];
export type CreateOnlineCheckout = Schemas['CreateOnlineCheckoutRequest'];
export type OnlineCheckoutStarted = Schemas['OnlineCheckoutStartedDto'];
export type OnlineCheckoutStatus = Schemas['OnlineCheckoutStatusDto'];

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
   * Pays the order online (Fase 24I): the server prices it and answers Mercado Pago's page, where the customer pays within
   * 30 minutes. The order is placed when the payment is approved.
   */
  startOnlineCheckout: (slug: string, body: CreateOnlineCheckout) =>
    apiRequest('post', '/api/v1/public/menu/{slug}/checkouts', { path: { slug }, body }),

  /**
   * How an online payment stands after asking Mercado Pago. The return key goes in the body: without it the order's
   * tracking link is not answered (the checkout's id travels in the return URL, which server telemetry keeps).
   */
  confirmOnlineCheckout: (slug: string, checkoutId: string, body: { paymentId?: string | null; returnKey?: string | null }, signal?: AbortSignal) =>
    apiRequest('post', '/api/v1/public/menu/{slug}/checkouts/{checkoutId}/confirm', { path: { slug, checkoutId }, body, signal }),

  /**
   * An online order followed with the link of its receipt (Fase 24C): the link goes in the body, never in the URL, so no
   * server log keeps it. Unknown or expired, it is not found (404).
   */
  trackOrder: (token: string, signal?: AbortSignal) =>
    apiRequest('post', '/api/v1/public/orders/tracking', { body: { token }, signal }),

  /** The services a business books online (Fase 24D): not found (404) while its owner has online booking off. */
  getBooking: (slug: string, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/public/menu/{slug}/booking', { path: { slug }, signal }),

  /** The free times of a service for a week from `from` (today when omitted); `nextFrom` opens the next one. */
  getBookingSlots: (slug: string, serviceId: string, from?: string, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/public/menu/{slug}/booking/slots', {
      path: { slug },
      query: from ? { serviceId, from } : { serviceId },
      signal,
    }),

  /** Asks for one of those times: the appointment waits «Por confirmar» until the business accepts it. */
  requestAppointment: (slug: string, body: RequestPublicAppointment) =>
    apiRequest('post', '/api/v1/public/menu/{slug}/appointments', { path: { slug }, body }),

  /** An appointment followed with the link of its receipt: the link goes in the body, like an order's. */
  trackAppointment: (token: string, signal?: AbortSignal) =>
    apiRequest('post', '/api/v1/public/appointments/tracking', { body: { token }, signal }),

  /** The customer cancels it from its link, before it starts. */
  cancelAppointmentByLink: (token: string) =>
    apiRequest('post', '/api/v1/public/appointments/tracking/cancel', { body: { token } }),
};
