import { useEffect, useState } from 'react';
import { NEAR_DECIMALS, roundTo, type MapPoint } from '../utils/geo';

export type NearMeStatus = 'off' | 'locating' | 'on' | 'denied' | 'unavailable';

export interface NearMe {
  status: NearMeStatus;
  /** Where the customer is, rounded to about 110 m; only while «Cerca de mí» is on and the browser answered. */
  position: MapPoint | null;
}

interface Answer {
  attempt: number;
  status: 'on' | 'denied' | 'unavailable';
  position: MapPoint | null;
}

const OPTIONS: PositionOptions = { enableHighAccuracy: false, timeout: 10_000, maximumAge: 5 * 60_000 };

/**
 * «Cerca de mí» (Fase 24G): the customer's position, asked of the browser while it is on, once per attempt (another
 * attempt asks again, after the customer allowed it). It lives in memory: never kept on the device nor sent anywhere
 * but Explorar's own search, and only to about 110 m (three decimals).
 */
export function useNearMe(on: boolean, attempt: number): NearMe {
  const [answer, setAnswer] = useState<Answer | null>(null);

  useEffect(() => {
    if (!on) return;
    // An answer for an older attempt never replaces a newer one.
    const keep = (next: Answer) => setAnswer((current) => (current && current.attempt > next.attempt ? current : next));
    if (!('geolocation' in navigator)) {
      queueMicrotask(() => keep({ attempt, status: 'unavailable', position: null }));
      return;
    }
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => keep({
        attempt,
        status: 'on',
        position: { latitude: roundTo(coords.latitude, NEAR_DECIMALS), longitude: roundTo(coords.longitude, NEAR_DECIMALS) },
      }),
      (error) => keep({ attempt, status: error.code === error.PERMISSION_DENIED ? 'denied' : 'unavailable', position: null }),
      OPTIONS,
    );
  }, [on, attempt]);

  if (!on) return { status: 'off', position: null };
  if (answer?.attempt !== attempt) return { status: 'locating', position: null };
  return { status: answer.status, position: answer.position };
}
