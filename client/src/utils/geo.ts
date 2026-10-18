/** A point on the map in degrees, latitude first, as Google Maps copies it (Fase 24G). */
export interface MapPoint {
  latitude: number;
  longitude: number;
}

/** Decimals a branch keeps: about 11 cm (GeoPoint.Decimals on the server). */
const BRANCH_DECIMALS = 6;
/** Decimals the customer's position travels with: about 110 m (GetPublicDirectoryQuery.NearDecimals). */
export const NEAR_DECIMALS = 3;

const NUMBER = String.raw`[-+]?\d{1,3}(?:\.\d+)?`;
// «19.043577, -98.198626» as Google Maps copies it, «19.043577 -98.198626», or a Google Maps link with the point in it
// (@19.04,-98.19 / ?q=19.04,-98.19 / !3d19.04!4d-98.19).
const PAIR = new RegExp(String.raw`^\s*(${NUMBER})\s*[,;\s]\s*(${NUMBER})\s*$`);
const IN_LINK = [
  new RegExp(String.raw`!3d(${NUMBER})!4d(${NUMBER})`),
  new RegExp(String.raw`[?&](?:q|query|ll|destination)=(${NUMBER}),\s*(${NUMBER})`),
  new RegExp(String.raw`@(${NUMBER}),(${NUMBER})`),
];

/** The value rounded to that many decimals. */
export function roundTo(value: number, decimals: number): number {
  const factor = 10 ** decimals;
  return Math.round(value * factor) / factor;
}

/**
 * The point an owner pasted: the coordinates Google Maps copies, or a link of it with the point inside. Null when the
 * text is empty; 'invalid' when it says no point on the map (or 0, 0, what a device without a position writes).
 */
export function parseCoordinates(text: string): MapPoint | null | 'invalid' {
  const trimmed = text.trim();
  if (!trimmed) return null;
  const match = PAIR.exec(trimmed) ?? IN_LINK.map((pattern) => pattern.exec(trimmed)).find(Boolean);
  if (!match) return 'invalid';
  const latitude = Number(match[1]);
  const longitude = Number(match[2]);
  if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) return 'invalid';
  if (Math.abs(latitude) > 90 || Math.abs(longitude) > 180 || (latitude === 0 && longitude === 0)) return 'invalid';
  return { latitude: roundTo(latitude, BRANCH_DECIMALS), longitude: roundTo(longitude, BRANCH_DECIMALS) };
}

/** «19.043577, -98.198626»: how the field shows a point, as Google Maps copies it. */
export function formatCoordinates(point: MapPoint): string {
  return `${roundTo(point.latitude, BRANCH_DECIMALS)}, ${roundTo(point.longitude, BRANCH_DECIMALS)}`;
}

/** The point in Google Maps: opens the maps app on a phone. */
export function mapPointUrl(point: MapPoint): string {
  return `https://www.google.com/maps/search/?api=1&query=${point.latitude},${point.longitude}`;
}

/** «a 300 m», «a 1.2 km», «a 12 km»: how far a business is, from the kilometers (one decimal) Explorar answers. */
export function formatDistance(km: number): string {
  if (km < 0.1) return 'a menos de 100 m';
  if (km < 1) return `a ${Math.round(km * 10) * 100} m`;
  if (km < 10) return `a ${km.toLocaleString('es-MX', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} km`;
  return `a ${Math.round(km).toLocaleString('es-MX')} km`;
}
