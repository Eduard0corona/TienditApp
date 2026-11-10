import { describe, expect, it } from 'vitest';
import { buildSellLink } from '../../utils/sellLink';
import { HomeVisitChargePolicy } from '../../api/generated/enums';
import type { AppointmentDto, ProductDto } from '../../types';
import { CfdiTaxObject, UnitType } from '../../types';
import {
  categoriesInUse,
  preselectedOptions,
  readChatOrigin,
  readCustomerPrefill,
  saleOfAppointment,
  scannedProduct,
  filterCatalog,
} from './sellModel';

function paramsOf(link: string): URLSearchParams {
  return new URLSearchParams(link.split('?')[1] ?? '');
}

describe('readCustomerPrefill', () => {
  it('fills in the customer from the phone and the name', () => {
    expect(readCustomerPrefill(new URLSearchParams('phone=%205512345678%20&name=%20Ana%20'))).toEqual({ phone: '5512345678', name: 'Ana' });
    expect(readCustomerPrefill(new URLSearchParams('phone=5512345678&name=%20'))).toEqual({ phone: '5512345678', name: undefined });
  });

  it('fills in nothing without a phone', () => {
    expect(readCustomerPrefill(new URLSearchParams('name=Ana'))).toBeNull();
    expect(readCustomerPrefill(new URLSearchParams('phone=%20'))).toBeNull();
  });
});

describe('readChatOrigin', () => {
  it('is a chat only with the conversation it was started from', () => {
    expect(readChatOrigin(new URLSearchParams('phone=5512345678&name=Ana&conversation=c1')))
      .toEqual({ phone: '5512345678', name: 'Ana', conversationId: 'c1' });
  });

  it('is not a chat with only the customer (the customer card)', () => {
    expect(readChatOrigin(new URLSearchParams('phone=5512345678&name=Ana'))).toBeNull();
    expect(readChatOrigin(new URLSearchParams('phone=5512345678&conversation=%20'))).toBeNull();
  });

  it('is not a chat without the customer phone', () => {
    expect(readChatOrigin(new URLSearchParams('conversation=c1'))).toBeNull();
  });

  it('tells the customer card link from the chat link', () => {
    const fromCard = paramsOf(buildSellLink({ phone: '5512345678', name: 'Ana' }));
    expect(readCustomerPrefill(fromCard)).toEqual({ phone: '5512345678', name: 'Ana' });
    expect(readChatOrigin(fromCard)).toBeNull();

    const fromChat = paramsOf(buildSellLink({ phone: '5512345678', name: 'Ana', conversationId: 'c1' }));
    expect(readCustomerPrefill(fromChat)).toEqual({ phone: '5512345678', name: 'Ana' });
    expect(readChatOrigin(fromChat)).toEqual({ phone: '5512345678', name: 'Ana', conversationId: 'c1' });
  });
});

describe('the catalog of Vender', () => {
  const product = (overrides: Partial<ProductDto>): ProductDto => ({
    id: 'taco', categoryId: 'tacos', name: 'Taco', sortOrder: 1, priceAmount: 25, priceCurrency: 'MXN', isTaxExempt: false,
    hasDiscount: false, taxObject: CfdiTaxObject.Subject, isAvailable: true, isFeatured: false, tracksStock: false, isLowStock: false,
    isTrackable: false, notifyCustomer: false, unit: UnitType.Unit, ...overrides,
  });
  const category = (id: string, name: string, sortOrder: number) => ({ id, name, sortOrder, isActive: true, productCount: 1 });
  const products = [
    product({ id: 'pastor', name: 'Taco al pastor', sortOrder: 2, sku: 'TP-01', barcode: '7501' }),
    product({ id: 'suadero', name: 'Taco de suadero', sortOrder: 1 }),
    product({ id: 'agua', categoryId: 'bebidas', name: 'Agua de horchata', sortOrder: 1, isFeatured: true }),
  ];

  it('lists only the categories with something to sell, in the business order', () => {
    const categories = [category('bebidas', 'Bebidas', 2), category('postres', 'Postres', 1), category('tacos', 'Tacos', 2)];
    expect(categoriesInUse(categories, products).map((c) => c.id)).toEqual(['bebidas', 'tacos']);
  });

  it('shows the featured first, then by order and name; the category chosen, or the featured ones', () => {
    expect(filterCatalog(products, 'all', '').map((p) => p.id)).toEqual(['agua', 'suadero', 'pastor']);
    expect(filterCatalog(products, 'tacos', '').map((p) => p.id)).toEqual(['suadero', 'pastor']);
    expect(filterCatalog(products, 'featured', '').map((p) => p.id)).toEqual(['agua']);
  });

  it('finds by name or SKU in any case, and by the whole barcode only', () => {
    expect(filterCatalog(products, 'all', ' PASTOR ').map((p) => p.id)).toEqual(['pastor']);
    expect(filterCatalog(products, 'all', 'tp-0').map((p) => p.id)).toEqual(['pastor']);
    expect(filterCatalog(products, 'all', '7501').map((p) => p.id)).toEqual(['pastor']);
    expect(filterCatalog(products, 'all', '750').map((p) => p.id)).toEqual([]);
  });

  it('takes what a scanner typed as the product of that barcode, or of that SKU in any case', () => {
    expect(scannedProduct(products, '7501')?.id).toBe('pastor');
    expect(scannedProduct(products, 'tp-01')?.id).toBe('pastor');
    expect(scannedProduct(products, 'Taco')).toBeUndefined();
  });

  it('starts a product with variants on the first available option of each group that takes exactly one', () => {
    const latte = product({
      optionGroups: [
        { id: 'tamano', name: 'Tamaño', minSelections: 1, maxSelections: 1, options: [
          { id: 'chico', name: 'Chico', priceDelta: 0, isAvailable: false },
          { id: 'mediano', name: 'Mediano', priceDelta: 5, isAvailable: true },
          { id: 'grande', name: 'Grande', priceDelta: 10, isAvailable: true },
        ] },
        { id: 'extras', name: 'Extras', minSelections: 0, maxSelections: 3, options: [{ id: 'shot', name: 'Shot', priceDelta: 8, isAvailable: true }] },
      ],
    });
    expect(preselectedOptions(latte)).toEqual(['mediano']);
    expect(preselectedOptions(product({}))).toEqual([]);
  });
});

describe('saleOfAppointment', () => {
  const corte = { id: 'corte', categoryId: 's', name: 'Corte', sortOrder: 1, priceAmount: 150, priceCurrency: 'MXN', isTaxExempt: false,
    hasDiscount: false, taxObject: CfdiTaxObject.Subject, isAvailable: true, isFeatured: false, tracksStock: false, isLowStock: false,
    isTrackable: false, notifyCustomer: false, unit: UnitType.Service } as ProductDto;
  const appointment = {
    id: 'cita', customerName: 'Ana', customerPhone: '5512345678', serviceName: 'Corte', serviceProductId: 'corte',
    startAt: '2026-11-10T17:00:00Z', visit: null,
  } as unknown as AppointmentDto;

  it('charges the service of the catalog, with the banner of the appointment', () => {
    const sale = saleOfAppointment(appointment, [corte]);
    expect(sale.service?.id).toBe('corte');
    expect(sale.freeVisit).toBe(false);
    expect(sale.appointment).toMatchObject({ id: 'cita', customerName: 'Ana', visitFee: null, zoneName: null });
    expect(sale.appointment.description).toMatch(/^Corte · /);
  });

  it('adds the charge for the visit, and a visit to quote «Sin costo» does not charge its service', () => {
    const visit = { fee: 80, zoneName: 'Centro', chargePolicy: HomeVisitChargePolicy.Free };
    const free = saleOfAppointment({ ...appointment, visit } as unknown as AppointmentDto, [corte]);
    expect(free).toMatchObject({ service: undefined, freeVisit: true, appointment: { visitFee: 80, zoneName: 'Centro' } });
    const charged = saleOfAppointment({ ...appointment, visit: { ...visit, fee: 0, chargePolicy: HomeVisitChargePolicy.Charged } } as unknown as AppointmentDto, [corte]);
    expect(charged).toMatchObject({ service: { id: 'corte' }, freeVisit: false, appointment: { visitFee: null } });
  });

  it('charges no service that is no longer in the catalog', () => {
    expect(saleOfAppointment(appointment, []).service).toBeUndefined();
  });
});
