import { describe, expect, it } from 'vitest';
import { OrderChannel } from '../../types';
import { PLATFORM_FOLIO_FORMAT_MESSAGE, PLATFORM_ONLY_PRODUCTS_MESSAGE, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { ADDRESS_LINE_REQUIRED_MESSAGE, DELIVERY_ONLY_PRODUCTS_MESSAGE, belowMinimumMessage, notCoveredMessage } from '../../utils/homeDelivery';
import {
  NO_ACTIVE_ZONES,
  PHONE_PROBLEM,
  POSTAL_CODE_MISSING,
  ZONES_FAILED,
  ZONES_LOADING,
  checkSale,
  type SaleCheckInput,
} from './saleChecks';
import { NO_SALE_DETAILS } from './sellModel';

const RAPPI: DeliveryPlatform = OrderChannel.Rappi;
const UBER_EATS: DeliveryPlatform = OrderChannel.UberEats;
const zone = { name: 'Centro', isActive: true, postalCodes: ['06700'], fee: 30, minimumOrder: 150 };
const totals = { subtotal: 172.41, tax: 27.59, total: 200, breakdown: [], productsTotal: 200, fee: 0 };
const counter: SaleCheckInput = {
  details: NO_SALE_DETAILS,
  platform: null,
  delivery: null,
  deliveryZone: null,
  zones: { pending: false, failed: false, active: 1 },
  totals,
  hasServices: false,
  folioChecked: false,
  orderIdChecked: false,
};
const address = { postalCode: ' 06700 ', addressLine: 'Calle 5 #12, Roma', references: '' };

describe('checkSale', () => {
  it('lets a counter sale go without anything to say', () => {
    expect(Object.values(checkSale(counter)).every((problem) => problem === '')).toBe(true);
  });

  it('says a phone that cannot be one, never on an order of a platform', () => {
    const typed = { ...NO_SALE_DETAILS, customerPhone: '55 12' };
    expect(checkSale({ ...counter, details: typed }).phoneError).toBe(PHONE_PROBLEM);
    expect(checkSale({ ...counter, details: { ...typed, customerPhone: '55 1234 5678' } }).phoneError).toBe('');
    expect(checkSale({ ...counter, details: { ...typed, channel: OrderChannel.Rappi }, platform: OrderChannel.Rappi }).phoneError).toBe('');
  });

  it('stops an order of a platform without its folio, saying so once a register was attempted', () => {
    const rappi = { ...counter, platform: RAPPI, details: { ...NO_SALE_DETAILS, channel: OrderChannel.Rappi } };

    const untouched = checkSale(rappi);
    expect(untouched.platformProblem).toBe('Escribe el folio de Rappi.');
    expect(untouched.folioError).toBe('');
    expect(checkSale({ ...rappi, folioChecked: true }).folioError).toBe('Escribe el folio de Rappi.');

    // A folio that cannot be one says so as it is typed.
    const typed = checkSale({ ...rappi, details: { ...rappi.details, externalOrderNumber: 'BC 953 !' } });
    expect(typed.folioError).toBe(PLATFORM_FOLIO_FORMAT_MESSAGE);
    expect(checkSale({ ...rappi, details: { ...rappi.details, externalOrderNumber: 'BC953' } }).platformProblem).toBe('');
  });

  it('stops a long id that cannot be one, saying so once left or six characters in', () => {
    const uber = {
      ...counter,
      platform: UBER_EATS,
      details: { ...NO_SALE_DETAILS, channel: OrderChannel.UberEats, externalOrderNumber: '4F2A1', externalOrderId: 'a!' },
    };
    expect(checkSale(uber).platformProblem).not.toBe('');
    expect(checkSale(uber).orderIdError).toBe('');
    expect(checkSale({ ...uber, orderIdChecked: true }).orderIdError).toBe(checkSale(uber).orderIdProblem);
    expect(checkSale({ ...uber, details: { ...uber.details, externalOrderId: 'abc!!!' } }).orderIdError).not.toBe('');
  });

  it('stops an order of a platform with services before its folio', () => {
    const rappi = { ...counter, platform: RAPPI, hasServices: true, details: { ...NO_SALE_DETAILS, channel: OrderChannel.Rappi } };
    expect(checkSale(rappi).platformProblem).toBe(PLATFORM_ONLY_PRODUCTS_MESSAGE);
  });

  it('says what stops a delivery, in the order the cashier fixes it', () => {
    const delivering = { ...counter, delivery: address, deliveryZone: zone };
    expect(checkSale({ ...delivering, hasServices: true }).deliveryProblem).toBe(DELIVERY_ONLY_PRODUCTS_MESSAGE);
    expect(checkSale({ ...delivering, hasServices: true, zones: { pending: true, failed: false, active: 0 } }).deliveryProblem)
      .toBe(DELIVERY_ONLY_PRODUCTS_MESSAGE);
    expect(checkSale({ ...delivering, zones: { pending: true, failed: false, active: 0 } }).deliveryProblem).toBe(ZONES_LOADING);
    expect(checkSale({ ...delivering, zones: { pending: false, failed: true, active: 0 } }).deliveryProblem).toBe(ZONES_FAILED);
    expect(checkSale({ ...delivering, zones: { pending: false, failed: false, active: 0 } }).deliveryProblem).toBe(NO_ACTIVE_ZONES);
    expect(checkSale({ ...delivering, delivery: { ...address, postalCode: '067' }, deliveryZone: null }).deliveryProblem).toBe(POSTAL_CODE_MISSING);
    expect(checkSale({ ...delivering, delivery: { ...address, postalCode: '01000' }, deliveryZone: null }).deliveryProblem)
      .toBe(notCoveredMessage('01000'));
    expect(checkSale({ ...delivering, totals: { ...totals, productsTotal: 100, total: 130 } }).deliveryProblem)
      .toBe(belowMinimumMessage('06700', 150));
    expect(checkSale({ ...delivering, delivery: { ...address, addressLine: '  ' } }).deliveryProblem).toBe(ADDRESS_LINE_REQUIRED_MESSAGE);
    // «Paga con» that does not cover what the courier collects.
    expect(checkSale({ ...delivering, delivery: { ...address, paysWith: '100' } }).deliveryProblem).not.toBe('');
    expect(checkSale({ ...delivering, delivery: { ...address, paysWith: '500' } }).deliveryProblem).toBe('');
    expect(checkSale(delivering).deliveryProblem).toBe('');
  });

  it('says the problem of a delivery once the postal code is whole, and what is wrong with the zones right away', () => {
    const typing = { ...counter, delivery: { ...address, postalCode: '067' }, deliveryZone: null };
    expect(checkSale(typing).deliveryProblem).toBe(POSTAL_CODE_MISSING);
    expect(checkSale(typing).deliveryHint).toBe('');
    expect(checkSale({ ...typing, zones: { pending: false, failed: false, active: 0 } }).deliveryHint).toBe(NO_ACTIVE_ZONES);
    expect(checkSale({ ...typing, delivery: { ...address, postalCode: '01000' } }).deliveryHint).toBe(notCoveredMessage('01000'));
  });
});
