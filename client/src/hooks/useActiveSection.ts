import { useEffect, useState } from 'react';

/**
 * The section of a long page the reader is on, for tabs that follow the scroll (the categories of the online menu, Fase
 * 24E): the last one whose top reached its `scroll-margin-top` (where a sticky bar ends and where its tab takes it), the
 * first one at the top of the page and the last one at its bottom, where a short section never reaches the top. `ids`
 * are the ids of the sections' elements, in order; a new list (the sections back after a search) measures again.
 */
export function useActiveSection(ids: readonly string[]): string | null {
  const [active, setActive] = useState<string | null>(ids[0] ?? null);
  const key = ids.join('|');

  useEffect(() => {
    const sections = key ? key.split('|') : [];
    let frame = 0;
    const update = () => {
      frame = 0;
      let current = sections[0] ?? null;
      const root = document.documentElement;
      // At the top of the page the first section is the one, whatever the layout measures.
      if (window.scrollY > 0 && window.innerHeight + window.scrollY >= root.scrollHeight - 2) {
        current = sections[sections.length - 1] ?? null;
      } else if (window.scrollY > 0) {
        for (const id of sections) {
          const element = document.getElementById(id);
          if (!element) continue;
          const margin = parseFloat(getComputedStyle(element).scrollMarginTop) || 0;
          if (element.getBoundingClientRect().top - margin <= 2) current = id;
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
  }, [key]);

  return active;
}
