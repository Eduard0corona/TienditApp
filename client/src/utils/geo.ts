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
// «19.043577, -98.198626» as Google Maps copies it, «19.043577 -98.198626», or a Google Maps link with the point of a
// place in it (!3d19.04!4d-98.19, ?q=19.04,-98.19). Never the «@19.04,-98.19» of a link: that is where its map was
// looking (a route, a list of results, the side panel), not the place.
const PAIR = new RegExp(String.raw`^\s*(${NUMBER})\s*[,;\s]\s*(${NUMBER})\s*$`);
const IN_LINK = [
  new RegExp(String.raw`!3d(${NUMBER})!4d(${NUMBER})`),
  new RegExp(String.raw`[?&](?:q|query|ll|destination)=(${NUMBER}),\s*(${NUMBER})`),
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
  // A link shared from a phone may write its comma as %2C.
  let decoded = trimmed;
  try {
    decoded = decodeURIComponent(trimmed);
  } catch {
    // Not encoded: as it was written.
  }
  const match = PAIR.exec(decoded) ?? IN_LINK.map((pattern) => pattern.exec(decoded)).find(Boolean);
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

/** «a 300 m», «a 1.2 km», «a 2 km», «a 12 km»: how far a business is, from the kilometers (one decimal) Explorar answers. */
export function formatDistance(km: number): string {
  if (km < 0.1) return 'a menos de 100 m';
  const meters = Math.round(km * 10) * 100;
  if (meters < 1000) return `a ${meters} m`;
  if (km < 10) return `a ${roundTo(km, 1).toLocaleString('es-MX', { maximumFractionDigits: 1 })} km`;
  return `a ${Math.round(km).toLocaleString('es-MX')} km`;
}

/** Why a pasted text is not a point, in words the owner can act on. */
export function coordinatesError(text: string): string {
  // A short link (maps.app.goo.gl) only points to Google, and a link of a map without a place only says where it looked.
  return /goo\.gl|google\.[a-z.]+\/maps|maps\.google\./i.test(text)
    ? 'Ese enlace no trae el punto exacto. Abre tu local en Google Maps, mantén presionado su punto y copia las coordenadas.'
    : 'No entendemos esas coordenadas. Pega las que copiaste de Google Maps, por ejemplo 19.043577, -98.198626.';
}
