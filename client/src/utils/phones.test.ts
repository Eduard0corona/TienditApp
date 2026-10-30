import { describe, expect, it } from 'vitest';
import { isSamePhone, isTypedPhone, localPhone } from './phones';

describe('localPhone', () => {
  it('writes a Mexican number with its 10 digits and leaves anything else as it comes', () => {
    expect(localPhone('+525522107765')).toBe('5522107765');
    expect(localPhone('+5215522107765')).toBe('5522107765');
    expect(localPhone('+14155552671')).toBe('+14155552671');
    expect(localPhone(' 55 2210 7765 ')).toBe('55 2210 7765');
    expect(localPhone(null)).toBe('');
  });
});

describe('isTypedPhone', () => {
  it('takes a Mexican number as its 10 digits and any other with its international prefix, as the server does', () => {
    for (const phone of ['5512345678', '55 1234 5678', '55-1234-5678', '(55) 1234 5678', '525512345678', '+52 55 1234 5678', '+1 415 555 0123',
      '0014155550123']) {
      expect(isTypedPhone(phone)).toBe(true);
    }
    // Another length without the prefix is a mistyped number, never one of another country.
    for (const phone of ['', '551234567', '55123456789', '14155550123', '+1234567', '+1234567890123456', 'cinco cinco', '55 1234 567x']) {
      expect(isTypedPhone(phone)).toBe(false);
    }
  });
});

describe('isSamePhone', () => {
  it('compares two numbers as a person in Mexico writes them', () => {
    expect(isSamePhone('55 1234 5678', '+525512345678')).toBe(true);
    expect(isSamePhone('+52 1 55 1234 5678', '5512345678')).toBe(true);
    expect(isSamePhone('0014155550123', '+14155550123')).toBe(true);
    expect(isSamePhone('5512345678', '+525512345679')).toBe(false);
    expect(isSamePhone('', null)).toBe(false);
  });
});
