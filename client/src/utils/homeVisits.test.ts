import { describe, expect, it } from 'vitest';
import { AppointmentStatus } from '../types';
import {
  CANCELLED_SALE_MESSAGE,
  NO_SHOW_SALE_MESSAGE,
  appointmentSaleProblem,
  describeVisitFee,
  describeVisitPlace,
  estimateVisitTotals,
  mapsPlaceUrl,
  mapsRouteUrl,
  telUrl,
} from './homeVisits';

const haircut = { unitPrice: 400, quantity: 1, ratePercent: 16, isExempt: false };

describe('estimateVisitTotals', () => {
  it('adds the charge as one more concept with IVA included, as the server does', () => {
    const totals = estimateVisitTotals([haircut], true, 80, 16);

    expect(totals.productsTotal).toBe(400);
    expect(totals.fee).toBe(80);
    expect(totals.total).toBe(480);
    expect(totals.tax).toBe(66.21);
  });

  it('adds IVA to the charge of a business whose prices do not include it', () => {
    expect(estimateVisitTotals([haircut], false, 80, 16).total).toBe(556.8);
  });

  it('keeps the rate of the business for the charge beside an exempt service', () => {
    const totals = estimateVisitTotals([{ ...haircut, ratePercent: 0, isExempt: true }], true, 80, 8);

    expect(totals.total).toBe(480);
    expect(totals.breakdown.map((line) => [line.ratePercent, line.taxAmount])).toEqual([[8, 5.93], [0, 0]]);
  });

  it('charges nothing more for a free visit', () => {
    expect(estimateVisitTotals([haircut], true, 0, 16).total).toBe(400);
  });
});

describe('texts', () => {
  it('says the charge like the prices of the business', () => {
    expect(describeVisitFee(80)).toBe('Cargo por visita $80.00');
    expect(describeVisitFee(80, false)).toBe('Cargo por visita $80.00 más IVA');
    expect(describeVisitFee(0)).toBe('Visita sin cargo');
    expect(describeVisitPlace({ postalCode: '72000', zoneName: 'Centro' })).toBe('A domicilio · CP 72000 · Centro');
  });

  it('refuses to charge a cancelled appointment or one the customer missed', () => {
    expect(appointmentSaleProblem(AppointmentStatus.Cancelled)).toBe(CANCELLED_SALE_MESSAGE);
    expect(appointmentSaleProblem(AppointmentStatus.NoShow)).toBe(NO_SHOW_SALE_MESSAGE);
    expect(appointmentSaleProblem(AppointmentStatus.Completed)).toBeNull();
    expect(appointmentSaleProblem(AppointmentStatus.Scheduled)).toBeNull();
  });
});

describe('maps and calls', () => {
  const first = { addressLine: 'Av. Juárez 123, Col. Centro', postalCode: '72000' };
  const second = { addressLine: ' 5 Sur 300 ', postalCode: '72005' };

  it('opens one address', () => {
    expect(mapsPlaceUrl(first)).toBe(
      'https://www.google.com/maps/search/?api=1&query=Av.%20Ju%C3%A1rez%20123%2C%20Col.%20Centro%2C%20CP%2072000%2C%20M%C3%A9xico',
    );
  });

  it('takes the route in order: the last visit is the destination and the others stops on the way', () => {
    const url = new URL(mapsRouteUrl([first, second])!);

    expect(url.searchParams.get('destination')).toBe('5 Sur 300, CP 72005, México');
    expect(url.searchParams.get('waypoints')).toBe('Av. Juárez 123, Col. Centro, CP 72000, México');
    expect(new URL(mapsRouteUrl([first])!).searchParams.get('waypoints')).toBeNull();
    expect(mapsRouteUrl([])).toBeNull();
  });

  it('fits ten visits in one link', () => {
    const many = Array.from({ length: 12 }, (_, i) => ({ addressLine: `Calle ${i}`, postalCode: '72000' }));
    const url = new URL(mapsRouteUrl(many)!);

    expect(url.searchParams.get('destination')).toBe('Calle 9, CP 72000, México');
    expect(url.searchParams.get('waypoints')!.split('|')).toHaveLength(9);
  });

  it('calls the phone with its digits only', () => {
    expect(telUrl('55 1234 5678')).toBe('tel:5512345678');
    expect(telUrl('+52 (55) 1234-5678')).toBe('tel:+525512345678');
  });
});
