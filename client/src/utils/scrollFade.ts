// A row that scrolls sideways (the categories of Vender and of the menu) fades the edge that hides more chips, so
// it shows there is more to slide to; the side menu does the same with the links below. The row measures itself
// (useScrollFade); this decides which edges fade, the same along either axis.

export interface ScrollFade {
  start: boolean;
  end: boolean;
}

/**
 * The edges hiding chips, with a pixel of slack for the rounding of a zoomed screen: how far it scrolled, what shows
 * and the whole length (scrollLeft, clientWidth and scrollWidth, or their Top/Height for a list).
 */
export function scrollFade(scrolled: number, shown: number, length: number): ScrollFade {
  const hidden = length - shown;
  if (hidden <= 1) return { start: false, end: false };
  return { start: scrolled > 1, end: scrolled < hidden - 1 };
}

/** The classes of the edges to fade (.ta-scroll-fade, or .ta-scroll-fade-y for a list, in theme.css). */
export function scrollFadeClass(fade: ScrollFade): string {
  return [fade.start ? 'fade-start' : '', fade.end ? 'fade-end' : ''].filter(Boolean).join(' ');
}
