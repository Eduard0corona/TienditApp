import { useEffect, useState } from 'react';

/**
 * The section of a long page the reader is on, for tabs that follow the scroll (the categories of the online menu, Fase
 * 24E): the last one whose top went past `offset` pixels from the top of the window (what a sticky bar covers), or the
 * first one at the top of the page. `ids` are the ids of the sections' elements, in order.
 */
export function useActiveSection(ids: readonly string[], offset: number): string | null {
  const [active, setActive] = useState<string | null>(ids[0] ?? null);
  const key = ids.join('|');

  useEffect(() => {
    const sections = key ? key.split('|') : [];
    let frame = 0;
    const update = () => {
      frame = 0;
      let current = sections[0] ?? null;
      // At the top of the page the first section is the one, whatever the layout measures.
      if (window.scrollY > 0) {
        for (const id of sections) {
          const element = document.getElementById(id);
          if (element && element.getBoundingClientRect().top - offset <= 1) current = id;
        }
      }
      setActive(current);
    };
    const schedule = () => {
      if (!frame) frame = window.requestAnimationFrame(update);
    };
    update();
    window.addEventListener('scroll', schedule, { passive: true });
    window.addEventListener('resize', schedule);
    return () => {
      window.removeEventListener('scroll', schedule);
      window.removeEventListener('resize', schedule);
      if (frame) window.cancelAnimationFrame(frame);
    };
  }, [key, offset]);

  return active;
}
