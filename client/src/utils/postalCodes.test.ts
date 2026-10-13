import { describe, expect, it } from 'vitest';
import { isPostalCode, parsePostalCodes } from './postalCodes';

describe('postal codes', () => {
  it('are five digits, as the server keeps them', () => {
    expect(isPostalCode('72000')).toBe(true);
    expect(isPostalCode('01000')).toBe(true);
    expect(isPostalCode('7200')).toBe(false);
    expect(isPostalCode('720000')).toBe(false);
    expect(isPostalCode('72a00')).toBe(false);
    expect(isPostalCode(' 72000')).toBe(false);
  });

  it('are read once each, in numeric order, whatever separates them', () => {
    expect(parsePostalCodes('72010, 72000\n72005;72000  72010\t')).toEqual({ codes: ['72000', '72005', '72010'], invalid: [] });
    expect(parsePostalCodes('   ')).toEqual({ codes: [], invalid: [] });
  });

  it('keep what is not one as it was written, once each', () => {
    expect(parsePostalCodes('72000, 7200, abc, 7200, 72 000')).toEqual({ codes: ['72000'], invalid: ['7200', 'abc', '72', '000'] });
  });
});
