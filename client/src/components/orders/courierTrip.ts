// The courier's trip of an order sent to the customer's address (Fases 22C and 22D): the folio and the link, with the same
// limits as the API (PlatformOrderNumber.MaxInputLength, CourierShipment.MaxTrackingUrlLength).
export const FOLIO_MAX_LENGTH = 60;
export const TRACKING_URL_MAX_LENGTH = 500;
export const TRACKING_URL_MESSAGE = 'El enlace del envío debe empezar con https://.';

/** A link the browser can open safely: written with «https://», as the server keeps them. */
export function isHttpsUrl(value: string | null | undefined): value is string {
  if (!value || !/^https:\/\//i.test(value.trim())) return false;
  try {
    return new URL(value).protocol === 'https:';
  } catch {
    return false;
  }
}
