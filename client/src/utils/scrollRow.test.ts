import { describe, expect, it } from 'vitest';
import { pageScroll, revealScroll, type RowItem } from './scrollRow';

// Seven tabs of 150 px side by side (1050 px) in a row that shows 400, with 50 px of fade and button at each edge.
const TABS: RowItem[] = Array.from({ length: 7 }, (_, index) => ({ start: index * 150, end: index * 150 + 150 }));
const PADDING = { start: 50, end: 50 };
const SHOWN = 400;
const LENGTH = 1050;

describe('revealScroll', () => {
  it('leaves the row where it is when the tab already shows clear of the edges', () => {
    expect(revealScroll(TABS[1], 0, SHOWN, LENGTH, PADDING)).toBe(0);
    expect(revealScroll(TABS[3], 300, SHOWN, LENGTH, PADDING)).toBe(300);
  });

  it('brings a tab hidden past the end just clear of the end edge', () => {
    // Tab 4 ends at 750: 750 - 400 + 50.
    expect(revealScroll(TABS[4], 0, SHOWN, LENGTH, PADDING)).toBe(400);
  });

  it('brings a tab hidden past the start just clear of the start edge', () => {
    expect(revealScroll(TABS[2], 500, SHOWN, LENGTH, PADDING)).toBe(250);
  });

  it('stops at either end of the row', () => {
    // The last tab goes to the end (650), with no faded edge left there; the first, to the start.
    expect(revealScroll(TABS[6], 0, SHOWN, LENGTH, PADDING)).toBe(650);
    expect(revealScroll(TABS[0], 300, SHOWN, LENGTH, PADDING)).toBe(0);
  });
});

describe('pageScroll', () => {
  it('starts the next page with the tab the end edge cut', () => {
    // From the start, tab 2 (300 to 450) is cut at 350: it lands just clear of the start edge, at 300 - 50.
    expect(pageScroll(1, TABS, 0, SHOWN, LENGTH, PADDING)).toBe(250);
    // From there tabs 2 and 3 show whole, up to the edge at 600: the next page starts with tab 4.
    expect(pageScroll(1, TABS, 250, SHOWN, LENGTH, PADDING)).toBe(550);
  });

  it('ends the previous page with the tab the start edge cut', () => {
    // At 400 the start edge is at 450: tab 2 (300 to 450) is cut and lands just clear of the end edge, at 450 - 350.
    expect(pageScroll(-1, TABS, 400, SHOWN, LENGTH, PADDING)).toBe(100);
    expect(pageScroll(-1, TABS, 100, SHOWN, LENGTH, PADDING)).toBe(0);
  });

  it('stops at either end of the row', () => {
    expect(pageScroll(1, TABS, 600, SHOWN, LENGTH, PADDING)).toBe(650);
    expect(pageScroll(-1, TABS, 30, SHOWN, LENGTH, PADDING)).toBe(0);
  });

  it('moves by a page when the cut tab is wider than one', () => {
    const wide: RowItem[] = [{ start: 0, end: 900 }, { start: 900, end: 1000 }];
    expect(pageScroll(1, wide, 0, SHOWN, 1000, PADDING)).toBe(300);
    expect(pageScroll(-1, wide, 600, SHOWN, 1000, PADDING)).toBe(300);
  });
});
