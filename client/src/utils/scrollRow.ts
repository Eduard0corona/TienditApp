// Where a row that scrolls sideways (the section tabs) moves to: to show the open tab when the page loads, and a page
// at a time with its buttons. Positions run along the row's content, from its start, in pixels: `scrolled`, `shown`
// and `length` are scrollLeft, clientWidth and scrollWidth, and `padding` is the room each faded edge (and its button)
// takes, the row's scroll-padding.

/** Where one item of the row starts and ends. */
export interface RowItem {
  start: number;
  end: number;
}

export interface RowPadding {
  start: number;
  end: number;
}

const clamp = (position: number, length: number, shown: number) => Math.min(Math.max(position, 0), Math.max(length - shown, 0));

/** The scroll that shows the whole item clear of the faded edges, or the same one when it already shows. */
export function revealScroll(item: RowItem, scrolled: number, shown: number, length: number, padding: RowPadding): number {
  if (item.start < scrolled + padding.start) return clamp(item.start - padding.start, length, shown);
  if (item.end > scrolled + shown - padding.end) return clamp(item.end - shown + padding.end, length, shown);
  return scrolled;
}

/**
 * The scroll a page further toward `direction` (1, the end; -1, the start): the first item cut by that edge lands just
 * clear of the other one, so no item is skipped and the page starts with a whole one. An item wider than a page (what
 * shows between the edges) moves the row by a page.
 */
export function pageScroll(direction: 1 | -1, items: RowItem[], scrolled: number, shown: number, length: number, padding: RowPadding): number {
  const page = Math.max(shown - padding.start - padding.end, 1);
  const cut = direction === 1
    ? items.find((item) => item.end > scrolled + shown - padding.end + 1)
    : [...items].reverse().find((item) => item.start < scrolled + padding.start - 1);
  if (!cut || cut.end - cut.start > page) return clamp(scrolled + direction * page, length, shown);
  return clamp(direction === 1 ? cut.start - padding.start : cut.end - shown + padding.end, length, shown);
}
