/**
 * A kitchen that only delivers, without premises. Same text as BusinessIndustries.HomeDelivery: a business that signs up
 * with it starts with a main branch that only delivers, whose address is never published.
 */
export const HOME_DELIVERY_INDUSTRY = 'Comida a domicilio';

/** The giros a business can pick; the one picked is stored as written here. */
export const INDUSTRY_OPTIONS = [
  'Alimentos y bebidas',
  HOME_DELIVERY_INDUSTRY,
  'Abarrotes',
  'Farmacia',
  'Ropa y accesorios',
  'Electrónica',
  'Ferretería',
  'Papelería',
  'Estética y spa',
  'Taller mecánico',
  'Carpintería',
  'Herrería y soldadura',
  'Electricista',
  'Plomería',
  'Aire acondicionado',
  'Consultorio',
  'Consultorio dental',
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
