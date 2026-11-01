import { apiRequest, type Schemas } from './typedClient';

export type PublicMenu = Schemas['PublicMenuDto'];
export type PublicMenuProduct = Schemas['PublicMenuProductDto'];
export type PublicOrderReceipt = Schemas['PublicOrderReceiptDto'];
export type PlacePublicOrder = Schemas['PlacePublicOrderRequest'];
export type PublicDeliveryQuote = Schemas['PublicDeliveryQuoteDto'];
export type PublicOrderTracking = Schemas['PublicOrderTrackingDto'];
export type PublicBooking = Schemas['PublicBookingDto'];
export type PublicBookingService = Schemas['PublicBookingServiceDto'];
export type PublicBookingStaff = Schemas['PublicBookingStaffDto'];
export type PublicBookingSlots = Schemas['PublicBookingSlotsDto'];
export type PublicBookingSlot = Schemas['PublicBookingSlotDto'];
export type RequestPublicAppointment = Schemas['RequestPublicAppointmentRequest'];
export type PublicAppointmentReceipt = Schemas['PublicAppointmentReceiptDto'];
export type CustomerAppointment = Schemas['CustomerAppointmentDto'];
export type PublicHomeVisit = Schemas['PublicHomeVisitDto'];
export type PublicHomeVisitCoverage = Schemas['PublicHomeVisitCoverageDto'];
export type RequestPublicHomeVisit = Schemas['RequestPublicHomeVisitRequest'];
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

  /**
   * The free times of a service for a week from `from` (today when omitted); `nextFrom` opens the next one. With `staffId`
   * (Fase 24J), the ones of that person; without it, whoever is free. Not found (404) when the person no longer takes it.
   */
  getBookingSlots: (slug: string, serviceId: string, from?: string, staffId?: string, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/public/menu/{slug}/booking/slots', {
      path: { slug },
      query: { serviceId, ...(from ? { from } : {}), ...(staffId ? { staffId } : {}) },
      signal,
    }),

  /** Asks for one of those times: the appointment waits «Por confirmar» until the business accepts it. */
  requestAppointment: (slug: string, body: RequestPublicAppointment) =>
    apiRequest('post', '/api/v1/public/menu/{slug}/appointments', { path: { slug }, body }),

  /**
   * The visit to quote a customer requests online (Fase 29B): what it is and what it costs before the postal code. Not
   * found (404) while the business does not receive visits.
   */
  getHomeVisit: (slug: string, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/public/menu/{slug}/home-visit', { path: { slug }, signal }),

  /** Whether the business goes to a postal code and what the visit costs there; never the zone's name. */
  getHomeVisitCoverage: (slug: string, postalCode: string, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/public/menu/{slug}/home-visit/coverage', { path: { slug }, query: { postalCode }, signal }),

  /**
   * The free times of the visit for a week from `from` (today when omitted), with the trip around each visit free. With
   * `staffId`, the ones of that person; not found (404) when they no longer go to visits.
   */
  getHomeVisitSlots: (slug: string, from?: string, staffId?: string, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/public/menu/{slug}/home-visit/slots', {
      path: { slug },
      query: { ...(from ? { from } : {}), ...(staffId ? { staffId } : {}) },
      signal,
    }),

  /** Requests the visit at one of those times: it waits «Por confirmar» until the business accepts it. */
  requestHomeVisit: (slug: string, body: RequestPublicHomeVisit) =>
    apiRequest('post', '/api/v1/public/menu/{slug}/home-visits', { path: { slug }, body }),

  /** An appointment followed with the link of its receipt: the link goes in the body, like an order's. */
  trackAppointment: (token: string, signal?: AbortSignal) =>
    apiRequest('post', '/api/v1/public/appointments/tracking', { body: { token }, signal }),

  /** The customer cancels it from its link, before it starts. */
  cancelAppointmentByLink: (token: string) =>
    apiRequest('post', '/api/v1/public/appointments/tracking/cancel', { body: { token } }),
};
