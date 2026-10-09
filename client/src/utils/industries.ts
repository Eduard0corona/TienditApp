/** The giros a business can pick; the one picked is stored as written here. */
export const INDUSTRY_OPTIONS = [
  'Alimentos y bebidas',
  'Abarrotes',
  'Farmacia',
  'Ropa y accesorios',
  'Electrónica',
  'Ferretería',
  'Papelería',
  'Estética y spa',
  'Taller mecánico',
  'Consultorio',
  'Servicios profesionales',
  'Educación',
  'Gimnasio',
  'Lavandería',
  'Otro',
];

const comparable = (value: string) => value.normalize('NFD').replace(/\p{Diacritic}/gu, '').trim().toLowerCase();

/**
 * The option a stored giro stands for. Giros saved with capitals on every word ("Alimentos y Bebidas") or without
 * accents ("Lavanderia") find theirs; any other text stays as it is, so saving the form never erases it.
 */
export function matchIndustry(value: string | null | undefined): string {
  if (!value) return '';
  return INDUSTRY_OPTIONS.find((option) => comparable(option) === comparable(value)) ?? value;
}
