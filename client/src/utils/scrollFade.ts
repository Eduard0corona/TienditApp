// A row that scrolls sideways (the categories of Vender and of the menu) fades the edge that hides more chips, so
// it shows there is more to slide to. The row measures itself (useScrollFade); this decides which edges fade.

export interface ScrollFade {
  start: boolean;
  end: boolean;
}

/** The edges hiding chips, with a pixel of slack for the rounding of a zoomed screen. */
export function scrollFade(scrollLeft: number, clientWidth: number, scrollWidth: number): ScrollFade {
  const hidden = scrollWidth - clientWidth;
  if (hidden <= 1) return { start: false, end: false };
  return { start: scrollLeft > 1, end: scrollLeft < hidden - 1 };
}

/** The classes of the edges to fade (.ta-scroll-fade in theme.css). */
export function scrollFadeClass(fade: ScrollFade): string {
  return [fade.start ? 'fade-start' : '', fade.end ? 'fade-end' : ''].filter(Boolean).join(' ');
}
