import { paysWithProblem } from '../../utils/courierCash';
import {
  PLATFORM_ONLY_PRODUCTS_MESSAGE,
  getPlatformFolioProblem,
  getPlatformOrderIdProblem,
  type DeliveryPlatform,
} from '../../utils/deliveryPlatforms';
import {
  ADDRESS_LINE_REQUIRED_MESSAGE,
  DELIVERY_ONLY_PRODUCTS_MESSAGE,
  belowMinimumMessage,
  meetsMinimum,
  normalizePostalCode,
  notCoveredMessage,
  type DeliveryZoneTerms,
} from '../../utils/homeDelivery';
import { isPostalCode } from '../../utils/postalCodes';
import type { SaleTotals } from './saleRequest';
import { isPlausiblePhone, type SaleDelivery, type SaleDetails } from './sellModel';

export const PHONE_PROBLEM = 'Revisa el teléfono: 10 dígitos o el número con lada internacional (+52…).';
export const ZONES_LOADING = 'Cargando tus zonas de entrega…';
export const ZONES_FAILED = 'No se pudieron leer tus zonas de entrega: intenta de nuevo o quita «A domicilio».';
export const NO_ACTIVE_ZONES = 'Ninguna zona de entrega está activa: quita «A domicilio» o activa una en Configuración › Zonas de entrega.';
export const POSTAL_CODE_MISSING = 'Escribe el código postal de la entrega (5 dígitos).';

/** The business's delivery zones as Vender has them while the sale is built. */
export interface ZonesState {
  pending: boolean;
  failed: boolean;
  active: number;
}

export interface SaleCheckInput {
  details: SaleDetails;
  /** The platform whose app took the order; null otherwise. */
  platform: DeliveryPlatform | null;
  delivery: SaleDelivery | null;
  deliveryZone: DeliveryZoneTerms | null;
  zones: ZonesState;
  totals: SaleTotals;
  hasServices: boolean;
  /** A register was attempted: an empty folio says so. */
  folioChecked: boolean;
  /** The cashier left the long id or tried to register: a half-typed one says so. */
  orderIdChecked: boolean;
}

export interface SaleChecks {
  phoneError: string;
  folioProblem: string;
  orderIdProblem: string;
  /** What stops an order of a platform: services in it, its folio or its long id. */
  platformProblem: string;
  /** The folio's problem, once it is worth saying. */
  folioError: string;
  orderIdError: string;
  /** What stops a delivery. */
  deliveryProblem: string;
  /** The delivery's problem, once it is worth saying: what is wrong with the zones right away, the rest with a whole postal code. */
  deliveryHint: string;
}

/** Quick feedback only: the server checks the phone, the platform's folio, the zone and its minimum again. */
export function checkSale(input: SaleCheckInput): SaleChecks {
  const { details, platform, delivery, deliveryZone, zones, totals, hasServices } = input;
  const deliveryPostalCode = delivery ? normalizePostalCode(delivery.postalCode) : '';
  const zonesProblem = !delivery ? ''
    : hasServices ? DELIVERY_ONLY_PRODUCTS_MESSAGE
    : zones.pending ? ZONES_LOADING
    : zones.active === 0 ? (zones.failed ? ZONES_FAILED : NO_ACTIVE_ZONES)
    : '';
  const deliveryProblem = !delivery ? ''
    : zonesProblem ? zonesProblem
    : !isPostalCode(deliveryPostalCode) ? POSTAL_CODE_MISSING
    : !deliveryZone ? notCoveredMessage(deliveryPostalCode)
    : totals.productsTotal !== null && !meetsMinimum(deliveryZone, totals.productsTotal)
      ? belowMinimumMessage(deliveryPostalCode, deliveryZone.minimumOrder)
      : !delivery.addressLine.trim() ? ADDRESS_LINE_REQUIRED_MESSAGE
      : paysWithProblem(delivery.paysWith ?? '', totals.total);
  // An order of a platform never sends the phone: what is in the hidden field does not stop it.
  const phoneError = !platform && details.customerPhone.trim() && !isPlausiblePhone(details.customerPhone) ? PHONE_PROBLEM : '';
  const folioProblem = platform ? getPlatformFolioProblem(platform, details.externalOrderNumber) : '';
  // The long id is optional: what was typed and cannot be one stops the sale.
  const orderIdProblem = platform ? getPlatformOrderIdProblem(details.externalOrderId) : '';
  return {
    phoneError,
    folioProblem,
    orderIdProblem,
    platformProblem: platform && hasServices ? PLATFORM_ONLY_PRODUCTS_MESSAGE : folioProblem || orderIdProblem,
    folioError: folioProblem && (input.folioChecked || details.externalOrderNumber.trim()) ? folioProblem : '',
    orderIdError: orderIdProblem && (input.orderIdChecked || details.externalOrderId.trim().length >= 6) ? orderIdProblem : '',
    deliveryProblem,
    deliveryHint: delivery && (zonesProblem || isPostalCode(deliveryPostalCode)) ? deliveryProblem : '',
  };
}
