import { describe, expect, it } from 'vitest';

// Fase 20F: keyboard focus shows the same everywhere, as theme.css's ring. Hyper took the outline off links and buttons
// and Bootstrap takes it off its components, so for years nothing showed where the keyboard was. The colors of the
// ring are checked in businessThemes.test.ts, what it looks like on screen in the scene visual/scenes/foco.spec.ts.
const ourStyles = import.meta.glob<string>('/src/**/*.{css,scss}', { query: '?raw', import: 'default', eager: true });

/**
 * The rules of a stylesheet as [selector, declarations], without comments and with Sass's #{…} flattened (a rule
 * inside @media comes out alone).
 */
function rulesOf(source: string): [string, string][] {
  const css = source.replace(/\r\n/g, '\n').replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '')
    .replace(/#\{[^}]*\}/g, '_');
  return [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map((match) => [match[1].trim().replace(/\s+/g, ' '), match[2]]);
}

describe('the keyboard focus ring', () => {
  it('reads every stylesheet of ours', () => {
    expect(ourStyles['/src/assets/css/theme.css']).toContain('--ta-focus-ring');
    expect(ourStyles['/src/assets/scss/_additions.scss']).toContain('.btn');
  });

  it('is drawn by one rule for every element, over the outline: 0 of each component', () => {
    const rule = rulesOf(ourStyles['/src/assets/css/theme.css']).find(([selector]) => selector.includes(':focus-visible'));
    expect(rule, 'theme.css has the ring').toBeDefined();
    const [selector, declarations] = rule!;
    expect(selector.split(',').map((part) => part.trim())).toEqual([
      ":focus-visible:not([tabindex='-1'])",
      '.form-control:focus',
      '.form-select:focus',
    ]);
    expect(declarations).toContain('outline: var(--ta-focus-ring-width) solid var(--ta-focus-ring) !important;');
    expect(declarations).toContain('outline-offset: var(--ta-focus-ring-offset) !important;');
  });

  it('is never taken away by another stylesheet', () => {
    // Only the theme card hides it, while what is focused inside it is not the keyboard's (ThemeCard.css).
    const takenAway = Object.entries(ourStyles).flatMap(([file, source]) => rulesOf(source)
      .filter(([selector, declarations]) => /\boutline(-style|-width)?\s*:\s*(0|none)\b/.test(declarations)
        && !selector.includes(':not(:has(:focus-visible))'))
      .map(([selector]) => `${file}: ${selector}`));
    expect(takenAway).toEqual([]);
  });
});
