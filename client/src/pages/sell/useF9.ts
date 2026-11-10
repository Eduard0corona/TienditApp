import { useEffect, useRef } from 'react';

/**
 * F9 does the main action of the ticket, as it is on the render that last ran; nothing while `blocked` (a dialog of Vender
 * is open, where it would act past what is on screen).
 */
export function useF9(action: () => void, blocked: boolean) {
  const current = useRef<() => void>(() => undefined);
  useEffect(() => {
    current.current = blocked ? () => undefined : action;
  });
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'F9') {
        event.preventDefault();
        current.current();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);
}
