import { describe, expect, it } from 'vitest';
import { OrderChannel, OrderPaymentMethod } from '../types';
import { ORDER_CHANNEL_LABELS, getChannelLabel, getPaymentMethodLabel } from './labels';

describe('labels', () => {
  it('names every order channel by its number and by its name', () => {
    for (const [name, value] of Object.entries(OrderChannel)) {
      const label = ORDER_CHANNEL_LABELS[value];
      expect(label, name).toBeTruthy();
      expect(getChannelLabel(value)).toBe(label);
      expect(getChannelLabel(name)).toBe(label);
      expect(getChannelLabel(String(value))).toBe(label);
    }
  });

  it('names the channels as the server does', () => {
    expect(Object.values(OrderChannel).map((value) => ORDER_CHANNEL_LABELS[value])).toEqual([
      'Mostrador',
      'WhatsApp',
      'En línea',
      'Rappi',
      'Uber Eats',
      'DiDi Food',
    ]);
  });

  it('names every payment method by its number and by its name', () => {
    for (const [name, value] of Object.entries(OrderPaymentMethod)) {
      const label = getPaymentMethodLabel(value);
      expect(label, name).toBeTruthy();
      expect(label).not.toBe(String(value));
      expect(getPaymentMethodLabel(name)).toBe(label);
    }
  });

  it('calls what a delivery platform charged «Plataforma»', () => {
    expect(getPaymentMethodLabel(OrderPaymentMethod.Platform)).toBe('Plataforma');
    expect(getPaymentMethodLabel('Platform')).toBe('Plataforma');
  });

  it('shows unknown values as they came and nothing for missing ones', () => {
    expect(getChannelLabel(9 as OrderChannel)).toBe('9');
    expect(getChannelLabel('Paquetenvia')).toBe('Paquetenvia');
    expect(getChannelLabel(null)).toBe('');
    expect(getPaymentMethodLabel(undefined)).toBe('');
  });
});
