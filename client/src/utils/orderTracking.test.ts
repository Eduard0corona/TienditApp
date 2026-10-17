import { describe, expect, it } from 'vitest';
import { TRACKING_REFRESH_MS, trackingRefresh } from './orderTracking';

const notFound = { isAxiosError: true, response: { status: 404, data: { detail: 'No encontramos ese pedido.' } } };
const offline = { isAxiosError: true, message: 'Network Error' };

describe('trackingRefresh', () => {
  it('asks again every 20 seconds while the order is in progress, and not once it is over', () => {
    expect(trackingRefresh(true, null)).toBe(TRACKING_REFRESH_MS);
    expect(trackingRefresh(false, null)).toBe(false);
  });

  it('stops once the server says the order is gone, and keeps asking after a lost connection', () => {
    expect(trackingRefresh(true, notFound)).toBe(false);
    expect(trackingRefresh(true, offline)).toBe(TRACKING_REFRESH_MS);
  });
});
