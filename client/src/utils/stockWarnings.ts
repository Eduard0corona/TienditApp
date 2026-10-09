/**
 * The toast text for the recipe supplies a sale took below zero, when the business sells and warns instead of
 * refusing (Configuración → Impuestos y ventas). Null when nothing ran short.
 */
export function stockWarningMessage(warnings: string[] | undefined): string | null {
  if (!warnings || warnings.length === 0) return null;
  return `${warnings.join('; ')}. Registra la entrada en Inventario.`;
}

export const STOCK_WARNING_TITLE = 'Vendido sin existencias suficientes';

export const ROUND_STOCK_WARNING_TITLE = 'Enviado a cocina sin existencias suficientes';
