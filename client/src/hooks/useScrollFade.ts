import { useEffect, useState } from 'react';
import { scrollFade, scrollFadeClass, type ScrollFade } from '../utils/scrollFade';

/**
 * Measures a row that scrolls sideways and says which of its edges to fade: when it scrolls, when the window or
 * the row changes size, and when `contentKey` changes (chips that arrive later). The ref is a callback, so a row
 * that unmounts and comes back (the menu after an order) is measured again.
 */
export function useScrollFade<T extends HTMLElement>(contentKey: unknown) {
  const [element, setElement] = useState<T | null>(null);
  const [fade, setFade] = useState<ScrollFade>({ start: false, end: false });

  useEffect(() => {
    if (!element) return undefined;
    const update = () => {
      const next = scrollFade(element.scrollLeft, element.clientWidth, element.scrollWidth);
      setFade((current) => (current.start === next.start && current.end === next.end ? current : next));
    };
    update();
    element.addEventListener('scroll', update, { passive: true });
    window.addEventListener('resize', update);
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(update);
    observer?.observe(element);
    return () => {
      element.removeEventListener('scroll', update);
      window.removeEventListener('resize', update);
      observer?.disconnect();
    };
  }, [element, contentKey]);

  return { ref: setElement, fadeClass: scrollFadeClass(fade) };
}
