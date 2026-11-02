import { afterEach, describe, expect, it, vi } from 'vitest';
import { InventoryMovementReason, InventoryMovementType, UnitType } from '../../types';
import { businessDayStart, setBusinessTimeZone } from '../../utils/dates';
import {
  buildEmptyLocationForm,
  buildEmptyMovementForm,
  buildEmptyMovementLineForm,
  buildEmptyRecipeLineForm,
  buildEmptySupplyForm,
  effectiveRecipeQuantity,
  toLocationRequest,
  toMovementRequest,
  toRecipeRequest,
  toSupplyRequest,
} from './inventoryForms';

describe('inventory forms', () => {
  it('asks for the name of a supply and sends blank fields as missing', () => {
    expect(toSupplyRequest(buildEmptySupplyForm()).error).toBe('El nombre del insumo es obligatorio.');

    const { request } = toSupplyRequest({
      ...buildEmptySupplyForm(),
      name: '  Leche entera ',
      code: ' ',
      unit: UnitType.Liter,
      defaultCost: '24.5',
    });

    expect(request).toMatchObject({
      name: 'Leche entera',
      code: undefined,
      unit: UnitType.Liter,
      defaultCost: 24.5,
      minStock: 0,
      maxStock: undefined,
      taxRate: undefined,
    });
  });

  it('refuses costs, stock limits and taxes the API would refuse', () => {
    const form = { ...buildEmptySupplyForm(), name: 'Leche' };

    expect(toSupplyRequest({ ...form, defaultCost: '-1' }).error).toBe('Costo por defecto inválido.');
    expect(toSupplyRequest({ ...form, minStock: '5', maxStock: '4' }).error)
      .toBe('La existencia máxima debe ser mayor o igual a la mínima.');
    expect(toSupplyRequest({ ...form, taxRate: '101' }).error).toBe('La tasa de impuesto debe estar entre 0 y 100.');
  });

  it('asks for the name and code of a location; without a branch it is the general warehouse', () => {
    expect(toLocationRequest({ ...buildEmptyLocationForm(), name: 'Bodega' }).error)
      .toBe('Nombre y código de ubicación son obligatorios.');
    expect(toLocationRequest({ ...buildEmptyLocationForm(), name: 'Bodega', code: 'BOD' }).request)
      .toMatchObject({ name: 'Bodega', code: 'BOD', branchId: undefined, city: undefined });
  });

  it('names the line of a movement that has a problem', () => {
    const line = { ...buildEmptyMovementLineForm(), supplyItemId: 's1', inventoryLocationId: 'l1' };
    const form = buildEmptyMovementForm();

    expect(toMovementRequest({ ...form, lines: [line, { ...line, quantity: '0' }] }).error)
      .toBe('Línea 2: cantidad inválida.');
    expect(toMovementRequest({ ...form, lines: [{ ...line, inventoryLocationId: '' }] }).error)
      .toBe('Línea 1: insumo y ubicación son obligatorios.');
    expect(toMovementRequest({ ...form, lines: [{ ...line, unitCost: '-3' }] }).error)
      .toBe('Línea 1: costo unitario inválido.');
  });

  it('sends a movement with its quantities, and an expiration as the start of that day of the business', () => {
    const { request } = toMovementRequest({
      ...buildEmptyMovementForm(),
      movementType: InventoryMovementType.Exit,
      movementReason: InventoryMovementReason.Waste,
      referenceNumber: ' ',
      lines: [{
        ...buildEmptyMovementLineForm(),
        supplyItemId: 's1',
        inventoryLocationId: 'l1',
        quantity: '2.5',
        unitCost: '10',
        expirationDate: '2026-10-31',
      }],
    });

    expect(request).toMatchObject({
      movementType: InventoryMovementType.Exit,
      movementReason: InventoryMovementReason.Waste,
      referenceNumber: undefined,
    });
    expect(request?.lines[0]).toMatchObject({
      quantity: 2.5,
      unitCost: 10,
      lotNumber: undefined,
      expirationDate: businessDayStart('2026-10-31'),
    });
  });

  describe('with the business in Tijuana and the device on Mexico City time', () => {
    afterEach(() => {
      setBusinessTimeZone(null);
      vi.unstubAllEnvs();
      vi.useRealTimers();
    });

    it('starts a movement at the hour of the business and sends the one typed as its clock', () => {
      // Tijuana is UTC-7 in October; Mexico City, UTC-6.
      vi.stubEnv('TZ', 'America/Mexico_City');
      setBusinessTimeZone('America/Tijuana');
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-10-08T17:30:00.000Z'));
      const line = { ...buildEmptyMovementLineForm(), supplyItemId: 's1', inventoryLocationId: 'l1' };

      const form = buildEmptyMovementForm();
      expect(form.movementDate).toBe('2026-10-08T10:30');
      expect(toMovementRequest({ ...form, lines: [line] }).request?.movementDate).toBe('2026-10-08T17:30:00.000Z');
      expect(toMovementRequest({ ...form, movementDate: '2026-10-07T21:15', lines: [line] }).request?.movementDate)
        .toBe('2026-10-08T04:15:00.000Z');
    });

    it('keeps the instant a movement started with in the hour the clock repeats', () => {
      setBusinessTimeZone('America/Tijuana');
      vi.useFakeTimers({ toFake: ['Date'] });
      // 09:30 UTC on 1 November is 01:30 PST in Tijuana, the second time 01:30 comes that night.
      vi.setSystemTime(new Date('2026-11-01T09:30:00.000Z'));
      const line = { ...buildEmptyMovementLineForm(), supplyItemId: 's1', inventoryLocationId: 'l1' };

      const form = buildEmptyMovementForm();
      expect(form.movementDate).toBe('2026-11-01T01:30');
      expect(toMovementRequest({ ...form, lines: [line] }).request?.movementDate).toBe('2026-11-01T09:30:00.000Z');
    });
  });

  it('keeps the waste of a recipe between 0 and 100 and previews what a line takes with it', () => {
    const line = { ...buildEmptyRecipeLineForm(), supplyItemId: 's1', quantity: '2', wastePercent: '10' };

    expect(effectiveRecipeQuantity(line)).toBeCloseTo(2.2);
    expect(toRecipeRequest([line, { ...line, wastePercent: '120' }]).error).toBe('Línea 2: merma inválida (0 a 100).');
    expect(toRecipeRequest([{ ...line, supplyItemId: '' }]).error).toBe('Línea 1: el insumo es obligatorio.');
    expect(toRecipeRequest([line]).request?.lines[0])
      .toMatchObject({ supplyItemId: 's1', quantity: 2, wastePercent: 10, inventoryLocationId: undefined });
  });
});
