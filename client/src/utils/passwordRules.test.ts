import { describe, expect, it } from 'vitest';
import { newPasswordPattern } from './passwordRules';

describe('newPasswordPattern', () => {
  it('accepts 8 characters with an uppercase, a lowercase and a number', () => {
    expect(newPasswordPattern.value.test('Secreto1')).toBe(true);
    expect(newPasswordPattern.value.test('Girasol-Azul27')).toBe(true);
  });

  it('refuses what the API refuses for its form', () => {
    expect(newPasswordPattern.value.test('Secre12')).toBe(false);
    expect(newPasswordPattern.value.test('secreto12')).toBe(false);
    expect(newPasswordPattern.value.test('SECRETO12')).toBe(false);
    expect(newPasswordPattern.value.test('Secretoxx')).toBe(false);
  });
});
