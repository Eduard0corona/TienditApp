import { useEffect, useState } from 'react';
import { scrollFade, scrollFadeClass, type ScrollFade } from '../utils/scrollFade';

/**
 * Measures a row that scrolls sideways (or, with `axis` 'y', a list that scrolls down) and says which of its edges to
 * fade: when it scrolls, when the window, the row or what it holds changes size, and when `contentKey` changes (chips
 * that arrive later). What it holds is watched too because it can change without the row changing (the condensed side
 * menu hides its group titles). The ref is a callback, so a row that unmounts and comes back (the menu after an order)
 * is measured again.
 */
export function useScrollFade<T extends HTMLElement>(contentKey: unknown, axis: 'x' | 'y' = 'x') {
  const [element, setElement] = useState<T | null>(null);
  const [fade, setFade] = useState<ScrollFade>({ start: false, end: false });

  useEffect(() => {
    if (!element) return undefined;
    const update = () => {
      const next = axis === 'y'
        ? scrollFade(element.scrollTop, element.clientHeight, element.scrollHeight)
        : scrollFade(element.scrollLeft, element.clientWidth, element.scrollWidth);
      setFade((current) => (current.start === next.start && current.end === next.end ? current : next));
    };
    update();
    element.addEventListener('scroll', update, { passive: true });
    window.addEventListener('resize', update);
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(update);
    observer?.observe(element);
    for (const child of element.children) observer?.observe(child);
    return () => {
      element.removeEventListener('scroll', update);
      window.removeEventListener('resize', update);
      observer?.disconnect();
    };
  }, [element, contentKey, axis]);

  return { ref: setElement, fadeClass: scrollFadeClass(fade) };
}
