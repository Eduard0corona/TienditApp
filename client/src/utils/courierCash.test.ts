import { describe, expect, it } from 'vitest';
import { PAYS_WITH_MESSAGE, changeFor, describeCollection, parsePaysWith, paysWithProblem, stillToCollect } from './courierCash';

const plain = (text: string) => text.replace(/\u00a0/g, ' ');

describe('courierCash', () => {
  it('reads «paga con» as nothing, an amount, or something that is not one', () => {
    expect(parsePaysWith('  ')).toBeNull();
    expect(parsePaysWith('$1,000')).toBe(1000);
    expect(parsePaysWith('500.5')).toBe(500.5);
    expect(parsePaysWith('0')).toBe('invalid');
    expect(parsePaysWith('12.345')).toBe('invalid');
    expect(parsePaysWith('100000.01')).toBe('invalid');
    expect(parsePaysWith('quinientos')).toBe('invalid');
    expect(parsePaysWith('100000')).toBe(100000);
  });

  it('says the server\'s words when the bill does not cover what is collected', () => {
    expect(paysWithProblem('', 275)).toBe('');
    expect(paysWithProblem('275', 275)).toBe('');
    expect(plain(paysWithProblem('200', 275))).toBe('Con $200.00 no alcanza: hay que cobrar $275.00.');
    expect(paysWithProblem('-5', 275)).toBe(PAYS_WITH_MESSAGE);
  });

  it('gives the change to the cent, and the line the courier reads', () => {
    expect(changeFor(500, 274.9)).toBe(225.1);
    expect(changeFor(200, 275)).toBe(0);
    expect(plain(describeCollection(275, 500))).toBe('Cobra $275.00 · paga con $500.00 · cambio $225.00');
    expect(plain(describeCollection(275))).toBe('Cobra $275.00');
  });

  it('collects what is still owed: less if part was paid meanwhile, nothing once paid', () => {
    expect(stillToCollect(275, 275)).toBe(275);
    expect(stillToCollect(275, 75.1)).toBe(75.1);
    expect(stillToCollect(275, 0)).toBe(0);
    expect(stillToCollect(275, -5)).toBe(0);
  });
});
