import { useEffect, useState } from 'react';
import { isNewVersionDeployed, runningEntryScript } from '../utils/newVersion';

// While the page is open and in view. A till stays open all day: without this it keeps the version it opened with.
export const CHECK_EVERY_MS = 10 * 60_000;
// Coming back to the tab fires focus and visibilitychange together: they ask once.
export const MIN_GAP_MS = 60_000;

/**
 * True once a newer version of the app is deployed than the one this page runs. It asks when the page comes back
 * into view or online, and every few minutes while it stays in view; never right after loading, which got the
 * current one.
 */
export function useNewVersion(): boolean {
  const [available, setAvailable] = useState(false);

  useEffect(() => {
    const running = runningEntryScript();
    if (!running) return undefined;

    let lastCheck = Date.now();
    let done = false;
    const check = async () => {
      if (done || document.visibilityState === 'hidden' || Date.now() - lastCheck < MIN_GAP_MS) return;
      lastCheck = Date.now();
      try {
        if (await isNewVersionDeployed(running)) {
          done = true;
          setAvailable(true);
        }
      } catch {
        // Offline or the server restarting: the next check asks again.
      }
    };
    const onChance = () => void check();

    document.addEventListener('visibilitychange', onChance);
    window.addEventListener('focus', onChance);
    window.addEventListener('online', onChance);
    const timer = window.setInterval(onChance, CHECK_EVERY_MS);
    return () => {
      done = true;
      document.removeEventListener('visibilitychange', onChance);
      window.removeEventListener('focus', onChance);
      window.removeEventListener('online', onChance);
      window.clearInterval(timer);
    };
  }, []);

  return available;
}
