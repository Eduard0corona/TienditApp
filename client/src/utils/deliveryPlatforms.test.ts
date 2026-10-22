import { describe, expect, it } from 'vitest';
import { OrderChannel } from '../types';
import {
  PLATFORM_FOLIO_FORMAT_MESSAGE,
  PLATFORM_FOLIO_MAX_LENGTH,
  PLATFORM_ORDER_ID_FORMAT_MESSAGE,
  getPlatformFolioHint,
  getPlatformFolioProblem,
  getPlatformName,
  getPlatformOrderIdHelp,
  getPlatformOrderIdProblem,
  isDeliveryPlatform,
  isValidPlatformFolio,
  normalizePlatformFolio,
  normalizePlatformOrderId,
} from './deliveryPlatforms';

describe('deliveryPlatforms', () => {
  it('tells the channels of a delivery platform apart from the rest, over the whole enum', () => {
    const platforms = Object.values(OrderChannel).filter((channel) => isDeliveryPlatform(channel));

    expect(platforms).toEqual([OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood]);
    expect(isDeliveryPlatform(undefined)).toBe(false);
    expect(isDeliveryPlatform(null)).toBe(false);
    expect(isDeliveryPlatform(9 as OrderChannel)).toBe(false);
  });

  it('writes a folio as the server keeps it', () => {
    expect(normalizePlatformFolio('  #bc 953 ')).toBe('BC953');
    expect(normalizePlatformFolio('152390')).toBe('152390');
    expect(normalizePlatformFolio('ab-12')).toBe('AB-12');
    // Only one leading # goes away, as on the server.
    expect(normalizePlatformFolio('##12')).toBe('#12');
  });

  it('takes 1 to 30 letters, digits or hyphens, as the server does', () => {
    expect(isValidPlatformFolio('  #bc 953 ')).toBe(true);
    expect(isValidPlatformFolio('12-AB')).toBe(true);
    expect(isValidPlatformFolio('1'.repeat(PLATFORM_FOLIO_MAX_LENGTH))).toBe(true);

    expect(isValidPlatformFolio('')).toBe(false);
    expect(isValidPlatformFolio('   ')).toBe(false);
    expect(isValidPlatformFolio('#')).toBe(false);
    expect(isValidPlatformFolio('1'.repeat(PLATFORM_FOLIO_MAX_LENGTH + 1))).toBe(false);
    expect(isValidPlatformFolio('ÁB1')).toBe(false);
    // Upper-cased in JavaScript "ß" would read "SS": the server refuses it all the same.
    expect(isValidPlatformFolio('ß1')).toBe(false);
    expect(isValidPlatformFolio('##12')).toBe(false);
    expect(isValidPlatformFolio('12/3')).toBe(false);
    // More than the server reads before normalizing, even if it is mostly spaces.
    expect(isValidPlatformFolio(`12${' '.repeat(60)}3`)).toBe(false);
  });

  it('says what is wrong with a folio, naming the platform when it is missing', () => {
    expect(getPlatformFolioProblem(OrderChannel.Rappi, '')).toBe('Escribe el folio de Rappi.');
    expect(getPlatformFolioProblem(OrderChannel.DidiFood, '  ')).toBe('Escribe el folio de DiDi Food.');
    expect(getPlatformFolioProblem(OrderChannel.UberEats, 'BC 95/3')).toBe(PLATFORM_FOLIO_FORMAT_MESSAGE);
    expect(PLATFORM_FOLIO_FORMAT_MESSAGE).toBe('El folio lleva de 1 a 30 letras, números o guiones.');
    expect(getPlatformFolioProblem(OrderChannel.UberEats, '#bc953')).toBe('');
  });

  it('opens the keyboard each platform needs and says where its folio is', () => {
    expect(getPlatformFolioHint(OrderChannel.Rappi)).toEqual({
      label: 'Folio de Rappi',
      placeholder: 'Ej. 152390',
      help: 'El número de pedido que ves en Rappi Aliados.',
      inputMode: 'numeric',
    });
    expect(getPlatformFolioHint(OrderChannel.UberEats)).toEqual({
      label: 'Folio de Uber Eats',
      placeholder: 'Ej. BC953',
      help: 'Los 5 caracteres del pedido en Uber Eats Orders.',
      autoCapitalize: 'characters',
    });
    expect(getPlatformFolioHint(OrderChannel.DidiFood)).toEqual({
      label: 'Folio de DiDi Food',
      placeholder: 'Ej. 12',
      help: 'El número de recolección o de pedido de DiDi Tienda.',
      inputMode: 'numeric',
    });
    expect([OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood].map(getPlatformName))
      .toEqual(['Rappi', 'Uber Eats', 'DiDi Food']);
  });

  it('takes the long id of an order as the server does: optional, 6 to 64 letters, digits or hyphens', () => {
    expect(normalizePlatformOrderId(' #3f9c2b1e-8d4a-4c6f-9b2e-1a7d5e0c3b84 ')).toBe('3F9C2B1E-8D4A-4C6F-9B2E-1A7D5E0C3B84');
    expect(normalizePlatformOrderId('1029384756 102938475')).toBe('1029384756102938475');
    expect(getPlatformOrderIdProblem('')).toBe('');
    expect(getPlatformOrderIdProblem('   ')).toBe('');
    expect(getPlatformOrderIdProblem('abcdef')).toBe('');
    expect(getPlatformOrderIdProblem('1029384756 102938475')).toBe('');
    // Uber Eats' five characters typed in the wrong field, a character the server refuses, more than 64 characters.
    expect(getPlatformOrderIdProblem('BC953')).toBe(PLATFORM_ORDER_ID_FORMAT_MESSAGE);
    expect(getPlatformOrderIdProblem('3f9c_2b1e-8d4a')).toBe(PLATFORM_ORDER_ID_FORMAT_MESSAGE);
    expect(getPlatformOrderIdProblem('9'.repeat(65))).toBe(PLATFORM_ORDER_ID_FORMAT_MESSAGE);
    expect(getPlatformOrderIdProblem('9'.repeat(64))).toBe('');
    // Spaces count toward what the server reads (100), not toward the id.
    expect(getPlatformOrderIdProblem(`${' '.repeat(95)}123456`)).toBe(PLATFORM_ORDER_ID_FORMAT_MESSAGE);
    expect(PLATFORM_ORDER_ID_FORMAT_MESSAGE).toBe('El ID largo del pedido lleva de 6 a 64 letras, números o guiones.');
    expect([OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood].map(getPlatformOrderIdHelp)).toEqual([
      'El número de orden completo de Rappi Aliados o del estado de cuenta.',
      'El ID del pedido (con guiones) del estado de cuenta de Uber Eats.',
      'El ID del pedido de DiDi Tienda o del estado de cuenta.',
    ]);
  });
});
