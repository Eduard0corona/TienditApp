// Form rules for the CFDI codes of a product (the API checks them again with SatCatalog).

export const satProductCodeRules = {
  pattern: { value: /^\s*\d{8}\s*$/, message: 'La clave de producto del SAT tiene 8 dígitos (por ejemplo 50202301).' },
};

export const satUnitCodeRules = {
  pattern: { value: /^\s*[A-Za-z0-9]{2,3}\s*$/, message: 'La clave de unidad tiene 2 o 3 letras o números (por ejemplo H87 o KGM).' },
};

/** Empty text means "use the generic code": the API stores null. */
export function toSatCodePayload(value: string | null | undefined): string | undefined {
  const trimmed = value?.trim();
  return trimmed ? trimmed.toUpperCase() : undefined;
}
