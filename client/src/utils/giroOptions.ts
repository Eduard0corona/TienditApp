import { HOME_DELIVERY_INDUSTRY, HOME_TRADES, matchIndustry } from './industries';

/**
 * The options of Mi negocio that only some giros use. The rest of the page (the plan, taxes, the online menu, the hours,
 * the appearance and the business's data) is for every business.
 */
export type GiroOption = 'deliveryPlatforms' | 'onlineBooking' | 'homeVisits';

/** What each option is called on the page. */
export const GIRO_OPTION_LABELS: Record<GiroOption, string> = {
  deliveryPlatforms: 'Plataformas de reparto',
  onlineBooking: 'Citas en línea',
  homeVisits: 'Visitas a domicilio',
};

const FOOD_AND_ERRANDS: readonly GiroOption[] = ['deliveryPlatforms'];
const SHOP: readonly GiroOption[] = [];
// Appointments online are at the premises (ADR 0025): for a trade, the customer asks for a visit to quote instead.
const AT_THE_PREMISES: readonly GiroOption[] = ['onlineBooking'];
const AT_HOME: readonly GiroOption[] = ['homeVisits'];

/**
 * What each giro of the list usually uses. Rappi, Uber Eats and DiDi take orders from kitchens, grocery stores and
 * pharmacies; a business that serves at its premises books appointments; a trade goes to the customer's home.
 */
const OPTIONS_BY_GIRO: Record<string, readonly GiroOption[]> = {
  'Alimentos y bebidas': FOOD_AND_ERRANDS,
  [HOME_DELIVERY_INDUSTRY]: FOOD_AND_ERRANDS,
  Abarrotes: FOOD_AND_ERRANDS,
  Farmacia: FOOD_AND_ERRANDS,
  'Ropa y accesorios': SHOP,
  Electrónica: SHOP,
  Ferretería: SHOP,
  Papelería: SHOP,
  'Estética y spa': AT_THE_PREMISES,
  'Taller mecánico': AT_THE_PREMISES,
  Consultorio: AT_THE_PREMISES,
  'Consultorio dental': AT_THE_PREMISES,
  'Servicios profesionales': AT_THE_PREMISES,
  Educación: AT_THE_PREMISES,
  Gimnasio: AT_THE_PREMISES,
  Lavandería: AT_THE_PREMISES,
  ...Object.fromEntries(HOME_TRADES.map((giro) => [giro, AT_HOME])),
};

/** The giros of the list that say what they use: «Otro» says nothing. */
export const GIROS_WITH_OPTIONS = Object.keys(OPTIONS_BY_GIRO);

/**
 * Whether a business of this giro usually uses the option. A giro that says nothing («Otro», one written by hand or
 * none) uses them all, so its page stays as it was.
 */
export function giroUsesOption(industry: string | null | undefined, option: GiroOption): boolean {
  const options = OPTIONS_BY_GIRO[matchIndustry(industry)];
  return options === undefined || options.includes(option);
}
