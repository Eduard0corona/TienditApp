// What DataTable's activeFilters takes (Fase 30G): on a phone «Filtros» says how many of the screen's filters are set.

/**
 * How many filters have a value: what is not empty, null, undefined or false (a switch that is off). Zero counts: it is
 * the first value of many enums. A screen whose «Todos» is another value passes null for it.
 */
export function countActiveFilters(...values: unknown[]): number {
  return values.filter((value) => value !== '' && value !== null && value !== undefined && value !== false).length;
}
