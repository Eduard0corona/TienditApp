import { afterEach, describe, expect, it } from 'vitest';
import { getFocusArea, readFocusedAreas, storeFocusedAreas } from './focusMode';

describe('focus mode', () => {
  afterEach(() => window.localStorage.clear());

  it('applies to Vender and Cocina only', () => {
    expect(getFocusArea('/sell')).toBe('sell');
    expect(getFocusArea('/kitchen')).toBe('kitchen');
    expect(getFocusArea('/orders')).toBeNull();
    expect(getFocusArea('/sell/history')).toBeNull();
  });

  it('is remembered by the device', () => {
    storeFocusedAreas(['kitchen']);

    expect(readFocusedAreas()).toEqual(['kitchen']);
  });

  it('ignores anything else stored under its key', () => {
    window.localStorage.setItem('tienditapp.focusedAreas', '["sell","admin",3]');
    expect(readFocusedAreas()).toEqual(['sell']);

    window.localStorage.setItem('tienditapp.focusedAreas', 'not json');
    expect(readFocusedAreas()).toEqual([]);
  });
});
