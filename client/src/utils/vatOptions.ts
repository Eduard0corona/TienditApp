// The IVA choice of a product in forms: the business rate, a rate of its own or exempt.
export type VatOption = '' | '16' | '8' | '0' | 'exempt';

export function getVatOptions(defaultTaxRate: number): Array<{ value: VatOption; label: string }> {
  return [
    { value: '', label: `Tasa del negocio (${defaultTaxRate} %)` },
    { value: '16', label: 'IVA 16 %' },
    { value: '8', label: 'IVA 8 % (frontera)' },
    { value: '0', label: 'IVA 0 % (alimentos básicos, medicinas)' },
    { value: 'exempt', label: 'Exento de IVA' },
  ];
}

export function toVatOption(product: { taxRate?: number | null; isTaxExempt?: boolean } | null | undefined): VatOption {
  if (!product) return '';
  if (product.isTaxExempt) return 'exempt';
  if (product.taxRate === 16 || product.taxRate === 8 || product.taxRate === 0) return String(product.taxRate) as VatOption;
  return '';
}

export function fromVatOption(option: VatOption | string | undefined): { taxRate?: number; isTaxExempt: boolean } {
  if (option === 'exempt') return { taxRate: undefined, isTaxExempt: true };
  if (option === '16' || option === '8' || option === '0') return { taxRate: Number(option), isTaxExempt: false };
  return { taxRate: undefined, isTaxExempt: false };
}
