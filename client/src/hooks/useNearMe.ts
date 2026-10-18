import { useEffect, useState } from 'react';
import { NEAR_DECIMALS, roundTo, type MapPoint } from '../utils/geo';

/**
 * - `ask`: opened with «Cerca de mí» (a shared link, a reload) and the browser has not allowed it yet: it waits for a tap
 *   instead of asking on its own.
 * - `unsupported`: this browser cannot tell where it is.
 */
export type NearMeStatus = 'off' | 'ask' | 'locating' | 'on' | 'denied' | 'unavailable' | 'unsupported';

export interface NearMe {
  status: NearMeStatus;
  /** Where the customer is, rounded to about 110 m; only while «Cerca de mí» is on and the browser answered. */
  position: MapPoint | null;
}

interface Answer {
  attempt: number;
  status: 'ask' | 'on' | 'denied' | 'unavailable';
  position: MapPoint | null;
}

const OPTIONS: PositionOptions = { enableHighAccuracy: false, timeout: 10_000, maximumAge: 60_000 };

/** Whether the browser already allows the position: then asking does not open its prompt. */
async function allowed(): Promise<boolean> {
  try {
    return (await navigator.permissions?.query({ name: 'geolocation' }))?.state === 'granted';
  } catch {
    return false;
  }
}

/**
 * «Cerca de mí» (Fase 24G): the customer's position, asked of the browser while it is on, once per attempt (each tap is
 * one; another asks again, after the customer allowed it). Attempt 0 is a page opened with «Cerca de mí» on: it asks
 * only if the browser already allows it, so a shared link never opens the prompt by itself. The position lives in
 * memory: never kept on the device nor sent anywhere but Explorar's own search, and only to about 110 m.
 */
export function useNearMe(on: boolean, attempt: number): NearMe {
  const [answer, setAnswer] = useState<Answer | null>(null);
  const supported = typeof navigator !== 'undefined' && 'geolocation' in navigator;

  useEffect(() => {
    if (!on || !supported) return;
    let current = true;
    // An answer for an older attempt never replaces a newer one.
    const keep = (next: Answer) => setAnswer((previous) => (previous && previous.attempt > next.attempt ? previous : next));
    const locate = () => navigator.geolocation.getCurrentPosition(
      ({ coords }) => keep({
        attempt,
        status: 'on',
        position: { latitude: roundTo(coords.latitude, NEAR_DECIMALS), longitude: roundTo(coords.longitude, NEAR_DECIMALS) },
      }),
      (error) => keep({ attempt, status: error.code === error.PERMISSION_DENIED ? 'denied' : 'unavailable', position: null }),
      OPTIONS,
    );
    if (attempt > 0) {
      locate();
    } else {
      void allowed().then((granted) => {
        if (!current) return;
        if (granted) locate();
        else keep({ attempt, status: 'ask', position: null });
      });
    }
    return () => {
      current = false;
    };
  }, [on, attempt, supported]);

  if (!on) return { status: 'off', position: null };
  if (!supported) return { status: 'unsupported', position: null };
  if (answer?.attempt !== attempt) return { status: 'locating', position: null };
  return { status: answer.status, position: answer.position };
}
