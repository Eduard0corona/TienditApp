import type { CreateServiceOrderPieceRequest, OrderType, ProductDto, UploadedFileDto } from '../../types';
import { OrderType as OrderTypeConst, UnitType } from '../../types';
import { resolveProductVat } from '../../utils/orderTotals';

// What Nuevo pedido (Orden detallada) needs besides drawing: the lines of its cart, the catalog it shows and what it sends
// for a service. Nothing here holds React state; the server decides the totals, the stock and the final order.

// Same limit as the API (OrderLimits.MaxQuantityPerLine).
export const MAX_QUANTITY_PER_LINE = 10_000;

export interface CartLine {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  isService: boolean;
  ratePercent: number;
  isExempt: boolean;
}

export interface ServiceChecklistItem {
  id: string;
  label: string;
  checked: boolean;
}

export interface ServicePhotoItem {
  id: string;
  url: string;
  fileName: string;
}

// Quick feedback only; the API normalizes and validates the number (PhoneNormalizationService).
export function isPlausiblePhone(value: string): boolean {
  const digits = value.replace(/\D/g, '');
  return digits.length >= 8 && digits.length <= 15;
}

/**
 * Charged by the hour, the session or the service: it goes to the service catalog and makes the order a service order.
 * Vender has the same rule (sellModel); the copy predates this module, and moving both to utils is a change of its own.
 */
export function isServiceProduct(product: ProductDto): boolean {
  return product.unit === UnitType.Hour ||
    product.unit === UnitType.Session ||
    product.unit === UnitType.Service;
}

export function buildChecklistItem(label = ''): ServiceChecklistItem {
  return {
    id: crypto.randomUUID(),
    label,
    checked: false,
  };
}

export function buildServicePiece(): CreateServiceOrderPieceRequest {
  return {
    itemName: '',
    requestedService: '',
    isTrackable: true,
    notifyCustomer: false,
  };
}

export function buildPhoto(uploaded: UploadedFileDto): ServicePhotoItem {
  return {
    id: crypto.randomUUID(),
    url: uploaded.url,
    fileName: uploaded.fileName,
  };
}

/** One more of a product already in the cart, or its line with the IVA it is sold with. */
export function addProductLine(cart: CartLine[], product: ProductDto, defaultTaxRate: number): CartLine[] {
  const existing = cart.find((line) => line.productId === product.id);
  if (existing) {
    return cart.map((line) =>
      line.productId === product.id
        ? { ...line, quantity: line.quantity + 1 }
        : line,
    );
  }

  const vat = resolveProductVat(product, defaultTaxRate);
  return [
    ...cart,
    {
      productId: product.id,
      productName: product.name,
      unitPrice: product.priceAmount,
      quantity: 1,
      isService: isServiceProduct(product),
      ratePercent: vat.ratePercent,
      isExempt: vat.isExempt,
    },
  ];
}

/** A line with another quantity; none or less takes it out of the cart. */
export function withLineQuantity(cart: CartLine[], productId: string, quantity: number): CartLine[] {
  if (quantity <= 0) return cart.filter((line) => line.productId !== productId);
  return cart.map((line) =>
    line.productId === productId ? { ...line, quantity } : line,
  );
}

/** The products or the services of the catalog, by the filter picked, whose name or description has what was typed. */
export function filterCatalog(products: ProductDto[], orderType: OrderType, productSearch: string): ProductDto[] {
  const search = productSearch.trim().toLowerCase();
  return products.filter((product) => {
    const matchesType = orderType === OrderTypeConst.Service
      ? isServiceProduct(product)
      : !isServiceProduct(product);

    if (!matchesType) return false;
    if (!search) return true;

    return product.name.toLowerCase().includes(search) ||
      (product.description || '').toLowerCase().includes(search);
  });
}

/** The pieces as the API takes them: trimmed, empty texts left out, and a piece without a name not sent. */
export function cleanServicePieces(pieces: CreateServiceOrderPieceRequest[]): CreateServiceOrderPieceRequest[] {
  return pieces
    .map((piece) => ({
      ...piece,
      itemName: piece.itemName.trim(),
      serviceProductId: piece.serviceProductId || undefined,
      isTrackable: piece.isTrackable ?? true,
      notifyCustomer: piece.notifyCustomer ?? false,
      brand: piece.brand?.trim() || undefined,
      model: piece.model?.trim() || undefined,
      color: piece.color?.trim() || undefined,
      size: piece.size?.trim() || undefined,
      material: piece.material?.trim() || undefined,
      conditionNotes: piece.conditionNotes?.trim() || undefined,
      requestedService: piece.requestedService?.trim() || undefined,
      internalNotes: piece.internalNotes?.trim() || undefined,
    }))
    .filter((piece) => piece.itemName);
}

/** The points of the reception checklist that say something, as they are saved (without the ids of the form). */
export function cleanChecklist(items: ServiceChecklistItem[]): Array<{ label: string; checked: boolean }> {
  return items
    .map((item) => ({
      label: item.label.trim(),
      checked: item.checked,
    }))
    .filter((item) => item.label.length > 0);
}
