import { describe, expect, it } from 'vitest';

// Fase 20F: keyboard focus shows the same everywhere, as theme.css's ring. Hyper took the outline off links and buttons
// and Bootstrap takes it off its components, so for years nothing showed where the keyboard was. The colors of the
// ring are checked in businessThemes.test.ts, what it looks like on screen in the scene visual/scenes/foco.spec.ts.
const ourStyles = import.meta.glob<string>('/src/**/*.{css,scss}', { query: '?raw', import: 'default', eager: true });

/**
 * The rules of a stylesheet as [selector, declarations], without comments and with Sass's #{…} flattened. Nested
 * blocks (Sass, @media, @supports) come out one by one, innermost first, and each parent keeps its own declarations.
 */
function rulesOf(source: string): [string, string][] {
  let css = source.replace(/\r\n/g, '\n').replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '')
    .replace(/#\{[^}]*\}/g, '_');
  const rules: [string, string][] = [];
  const innermost = /([^{};]*)\{([^{}]*)\}/g;
  while (innermost.test(css)) {
    css = css.replace(innermost, (_, selector: string, declarations: string) => {
      rules.push([selector.trim().replace(/\s+/g, ' '), declarations]);
      return '';
    });
  }
  return rules;
}

const RING = ['outline: var(--ta-focus-ring-width) solid var(--ta-focus-ring) !important;',
  'outline-offset: var(--ta-focus-ring-offset) !important;'];

describe('the keyboard focus ring', () => {
  const theme = rulesOf(ourStyles['/src/assets/css/theme.css']);
  const ruleFor = (selector: string) => theme.find(([candidate]) => candidate === selector);

  it('reads every stylesheet of ours', () => {
    expect(ourStyles['/src/assets/css/theme.css']).toContain('--ta-focus-ring');
    expect(ourStyles['/src/assets/scss/_additions.scss']).toContain('.btn');
  });

  it('is drawn over the outline: 0 of each component, for the keyboard and on fields', () => {
    for (const selector of [":focus-visible:not([tabindex='-1'])", '.form-control:focus, .form-select:focus',
      '.form-check-input:focus, .page-link:focus']) {
      const rule = ruleFor(selector);
      expect(rule, `theme.css draws the ring for ${selector}`).toBeDefined();
      for (const declaration of RING) expect(rule![1]).toContain(declaration);
    }
    // Checks and page buttons get it on any focus only where :focus-visible is unknown.
    expect(ourStyles['/src/assets/css/theme.css']).toMatch(
      /@supports not selector\(:focus-visible\) \{\s*\.form-check-input:focus,\s*\.page-link:focus \{/);
  });

  it('never shares a selector list with :focus-visible, which Safari before 15.4 drops whole', () => {
    const mixed = Object.entries(ourStyles).flatMap(([file, source]) => rulesOf(source)
      .filter(([selector]) => !selector.startsWith('@'))
      .filter(([selector]) => {
        const parts = selector.split(',');
        return parts.length > 1 && parts.some((part) => part.includes(':focus-visible'));
      })
      .map(([selector]) => `${file}: ${selector}`));
    expect(mixed).toEqual([]);
  });

  it('is never taken away by another stylesheet', () => {
    // Only the theme card hides it, while what is focused inside it is not the keyboard's (ThemeCard.css).
    const takenAway = Object.entries(ourStyles).flatMap(([file, source]) => rulesOf(source)
      .filter(([selector, declarations]) => /\boutline(-style|-width|-color)?\s*:\s*([^;]*\btransparent\b|(0|0px|none)\s*(!important)?\s*(;|$))/m
        .test(declarations) && !selector.includes(':not(:has(:focus-visible))'))
      .map(([selector]) => `${file}: ${selector}`));
    expect(takenAway).toEqual([]);
  });
});
