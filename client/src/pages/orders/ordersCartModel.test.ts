import { describe, expect, it } from 'vitest';
import { OrderType, UnitType, type CreateServiceOrderPieceRequest, type ProductDto } from '../../types';
import {
  addProductLine,
  cleanChecklist,
  cleanServicePieces,
  filterCatalog,
  isPlausiblePhone,
  isServiceProduct,
  withLineQuantity,
  type CartLine,
} from './ordersCartModel';

const product = (overrides: Partial<ProductDto>) => ({
  id: 'p1', name: 'Café de Chiapas 250 g', description: 'Altura, notas a cacao', priceAmount: 180, unit: UnitType.Unit,
  ...overrides,
}) as unknown as ProductDto;

const coffee = product({});
const maintenance = product({ id: 's1', name: 'Mantenimiento de cafetera', description: 'Limpieza profunda', priceAmount: 450, unit: UnitType.Service });
const workshop = product({ id: 's2', name: 'Taller de barismo', description: 'Tres horas', priceAmount: 650, unit: UnitType.Session });

describe('ordersCartModel', () => {
  it('takes a phone of 8 to 15 digits, whatever separates them', () => {
    expect(isPlausiblePhone('55 1234 5678')).toBe(true);
    expect(isPlausiblePhone('+52 (664) 123-4567')).toBe(true);
    expect(isPlausiblePhone('12345678')).toBe(true);
    expect(isPlausiblePhone('123456789012345')).toBe(true);
    expect(isPlausiblePhone('1234567')).toBe(false);
    expect(isPlausiblePhone('1234567890123456')).toBe(false);
  });

  it('counts as a service what is charged by the hour, the session or the service', () => {
    expect(isServiceProduct(product({ unit: UnitType.Hour }))).toBe(true);
    expect(isServiceProduct(workshop)).toBe(true);
    expect(isServiceProduct(maintenance)).toBe(true);
    expect(isServiceProduct(coffee)).toBe(false);
    expect(isServiceProduct(product({ unit: UnitType.Kg }))).toBe(false);
  });

  it('adds a product with the IVA it is sold with, and one more of it the next time', () => {
    const first = addProductLine([], coffee, 16);
    expect(first).toEqual([{
      productId: 'p1', productName: 'Café de Chiapas 250 g', unitPrice: 180, quantity: 1, isService: false, ratePercent: 16, isExempt: false,
    }]);

    const twice = addProductLine(addProductLine(first, maintenance, 16), coffee, 16);
    expect(twice.map((line) => [line.productId, line.quantity, line.isService])).toEqual([['p1', 2, false], ['s1', 1, true]]);

    expect(addProductLine([], product({ taxRate: 0 }), 16)[0]).toMatchObject({ ratePercent: 0, isExempt: false });
    expect(addProductLine([], product({ isTaxExempt: true }), 16)[0]).toMatchObject({ ratePercent: 0, isExempt: true });
  });

  it('changes the quantity of a line and takes it out at zero or less', () => {
    const cart: CartLine[] = addProductLine(addProductLine([], coffee, 16), maintenance, 16);
    expect(withLineQuantity(cart, 'p1', 5).map((line) => line.quantity)).toEqual([5, 1]);
    expect(withLineQuantity(cart, 'p1', 0).map((line) => line.productId)).toEqual(['s1']);
    expect(withLineQuantity(cart, 's1', -1).map((line) => line.productId)).toEqual(['p1']);
  });

  it('shows the products or the services of the catalog, by what is typed in the name or the description', () => {
    const catalog = [coffee, maintenance, workshop];
    expect(filterCatalog(catalog, OrderType.Retail, '')).toEqual([coffee]);
    expect(filterCatalog(catalog, OrderType.Service, '')).toEqual([maintenance, workshop]);
    expect(filterCatalog(catalog, OrderType.Service, '  LIMPIEZA ')).toEqual([maintenance]);
    expect(filterCatalog(catalog, OrderType.Service, 'barismo')).toEqual([workshop]);
    expect(filterCatalog(catalog, OrderType.Retail, 'barismo')).toEqual([]);
    expect(filterCatalog([product({ description: undefined })], OrderType.Retail, 'cacao')).toEqual([]);
  });

  it('sends the pieces trimmed, without empty texts and without the ones that have no name', () => {
    const pieces: CreateServiceOrderPieceRequest[] = [
      {
        itemName: '  Cafetera Gaggia  ', requestedService: ' Mantenimiento ', serviceProductId: 's1', brand: ' Gaggia ', model: '   ',
        color: '', isTrackable: false, notifyCustomer: true, conditionNotes: ' Golpe en la tapa ', internalNotes: ' ',
      },
      { itemName: '   ', requestedService: 'Descalcificación', isTrackable: true, notifyCustomer: false },
      { itemName: 'Molino', serviceProductId: '' } as CreateServiceOrderPieceRequest,
    ];

    expect(cleanServicePieces(pieces)).toEqual([
      {
        itemName: 'Cafetera Gaggia', requestedService: 'Mantenimiento', serviceProductId: 's1', brand: 'Gaggia', model: undefined,
        color: undefined, size: undefined, material: undefined, isTrackable: false, notifyCustomer: true,
        conditionNotes: 'Golpe en la tapa', internalNotes: undefined,
      },
      {
        itemName: 'Molino', requestedService: undefined, serviceProductId: undefined, brand: undefined, model: undefined,
        color: undefined, size: undefined, material: undefined, isTrackable: true, notifyCustomer: false,
        conditionNotes: undefined, internalNotes: undefined,
      },
    ]);
  });

  it('saves the points of the checklist that say something, without the ids of the form', () => {
    expect(cleanChecklist([
      { id: 'a', label: ' Sin golpes ', checked: true },
      { id: 'b', label: '   ', checked: true },
      { id: 'c', label: 'Con su jarra', checked: false },
    ])).toEqual([{ label: 'Sin golpes', checked: true }, { label: 'Con su jarra', checked: false }]);
  });
});
