import { describe, expect, it } from 'vitest';
import { scrollFade, scrollFadeClass } from './scrollFade';

describe('scrollFade', () => {
  it('fades the edges that hide more chips', () => {
    // Everything fits: nothing to hint.
    expect(scrollFade(0, 400, 400)).toEqual({ start: false, end: false });
    // More to the right at the start, on both sides in the middle, more to the left at the end.
    expect(scrollFade(0, 400, 900)).toEqual({ start: false, end: true });
    expect(scrollFade(250, 400, 900)).toEqual({ start: true, end: true });
    expect(scrollFade(500, 400, 900)).toEqual({ start: true, end: false });
  });

  it('ignores the pixel a zoomed screen rounds', () => {
    expect(scrollFade(0, 400, 401)).toEqual({ start: false, end: false });
    expect(scrollFade(499.5, 400, 900)).toEqual({ start: true, end: false });
  });

  it('names the classes of the edges to fade', () => {
    expect(scrollFadeClass({ start: false, end: false })).toBe('');
    expect(scrollFadeClass({ start: false, end: true })).toBe('fade-end');
    expect(scrollFadeClass({ start: true, end: true })).toBe('fade-start fade-end');
  });
});
