import type { KeyboardEvent } from 'react';

/**
 * Makes a block element (a card, a list row) work as a button for the keyboard and screen readers: it takes focus,
 * says it is a button and Enter or Space activate it. A real <button> is better when its content allows it.
 */
export function asButton(onActivate: () => void) {
  return {
    role: 'button' as const,
    tabIndex: 0,
    onClick: onActivate,
    onKeyDown: (event: KeyboardEvent<HTMLElement>) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        onActivate();
      }
    },
  };
}
