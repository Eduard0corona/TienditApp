import { describe, expect, it } from 'vitest';
import additionsSource from '../assets/scss/_additions.scss?raw';
import shellCssSource from '../assets/css/shell.css?raw';
import themeCssSource from '../assets/css/theme.css?raw';
import { BusinessTheme } from '../types';
import { BUSINESS_THEMES, getBusinessTheme, themeCssVariables } from './businessThemes';
import { contrastRatio } from './contrast';

const AA = 4.5;
// WCAG 1.4.11: what is not text, like the keyboard's focus ring, needs 3:1 against what is next to it.
const NON_TEXT = 3;
// Hyper's surfaces in dark mode: the page, a card and a raised row (attention rows, hovered items).
const DARK_SURFACES = ['#343a40', '#37404a', '#3d4854'];
// Where else the focus ring can sit: in light mode a hovered menu item or a raised row (#f6f7fb, Bootstrap's tertiary
// background); in dark mode the top bar and the side menu (#3a444e, shell.css) and a field (#404954, _additions.scss).
const LIGHT_RING_SURFACES = ['#f6f7fb'];
const DARK_RING_SURFACES = ['#3a444e', '#404954'];
// A Windows checkout with core.autocrlf has theme.css in CRLF; the rules below are found by their line breaks.
const themeCss = themeCssSource.replace(/\r\n/g, '\n');
const shellCss = shellCssSource.replace(/\r\n/g, '\n');

/** The custom properties of the first top-level rule whose selector starts with `selector`. */
function block(selector: string, css = themeCss): Record<string, string> {
  const start = css.indexOf(`\n${selector}`);
  expect(start, `the stylesheet has a rule for ${selector}`).toBeGreaterThanOrEqual(0);
  const body = css.slice(css.indexOf('{', start) + 1, css.indexOf('}', start));
  return Object.fromEntries([...body.matchAll(/(--[\w-]+):\s*([^;]+);/g)].map((match) => [match[1], match[2].trim()]));
}

const themeDefaults = block(':root {\n  --tt-accent');
const light = block(":root:not([data-bs-theme='dark'])");
const dark = block("[data-bs-theme='dark'] {");

describe('business themes', () => {
  it('has one definition per theme of the API, with Nopal first', () => {
    const values = Object.values(BusinessTheme);
    expect(BUSINESS_THEMES.map((definition) => definition.theme).sort()).toEqual([...values].sort());
    expect(new Set(BUSINESS_THEMES.map((definition) => definition.name)).size).toBe(values.length);
    expect(BUSINESS_THEMES[0].theme).toBe(BusinessTheme.Nopal);
  });

  it('falls back to Nopal for a theme this version does not know', () => {
    expect(getBusinessTheme(undefined).theme).toBe(BusinessTheme.Nopal);
    expect(getBusinessTheme(42 as BusinessTheme).theme).toBe(BusinessTheme.Nopal);
    expect(getBusinessTheme(BusinessTheme.Talavera).name).toBe('Talavera');
  });

  it.each(BUSINESS_THEMES.map((definition) => [definition.name, definition] as const))(
    '%s passes WCAG AA in light and dark',
    (_, { light: l, dark: d }) => {
      for (const background of [l.accent, l.accentHover, l.accentActive]) expect(contrastRatio(l.onAccent, background)).toBeGreaterThanOrEqual(AA);
      // The accent is text too: outline buttons and text in the color of the business.
      expect(contrastRatio(l.accent, '#ffffff')).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(l.accent, l.pageBg)).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(l.link, '#ffffff')).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(l.link, l.pageBg)).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(l.linkHover, '#ffffff')).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(l.accentText, l.accentSubtle)).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(light['--ct-body-color'], l.pageBg)).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(light['--ct-secondary-color'], l.pageBg)).toBeGreaterThanOrEqual(AA);

      for (const background of [d.accent, d.accentHover, d.accentActive]) expect(contrastRatio(d.onAccent, background)).toBeGreaterThanOrEqual(AA);
      for (const surface of DARK_SURFACES) {
        expect(contrastRatio(d.link, surface)).toBeGreaterThanOrEqual(AA);
        expect(contrastRatio(d.accent, surface)).toBeGreaterThanOrEqual(AA);
      }
      expect(contrastRatio(d.accentText, d.accentSubtle)).toBeGreaterThanOrEqual(AA);
    },
  );

  it.each(BUSINESS_THEMES.map((definition) => [definition.name, definition] as const))(
    '%s draws the focus ring with 3:1 on every surface it can sit on',
    (_, { light: l, dark: d }) => {
      // The ring is the accent (theme.css), 2px away from the element: it sits on the surface around it, or on the
      // tint of a selected item (the current page in the menu, a chosen option).
      for (const surface of [light['--ta-surface'], l.pageBg, l.accentSubtle, ...LIGHT_RING_SURFACES]) {
        expect(contrastRatio(l.accent, surface), `${l.accent} on ${surface}`).toBeGreaterThanOrEqual(NON_TEXT);
      }
      for (const surface of [...DARK_SURFACES, dark['--ta-surface'], d.accentSubtle, ...DARK_RING_SURFACES]) {
        expect(contrastRatio(d.accent, surface), `${d.accent} on ${surface}`).toBeGreaterThanOrEqual(NON_TEXT);
      }
      // On a surface painted with the accent (Mi negocio, Perfil, the sign-in header) the ring is the text on it.
      expect(contrastRatio(l.onAccent, l.accent)).toBeGreaterThanOrEqual(NON_TEXT);
    },
  );

  it.each(BUSINESS_THEMES.map((definition) => [definition.name, definition] as const))(
    '%s marks a checked box, radio or switch with 3:1 on its dark accent',
    (_, { dark: d }) => {
      // The marks are an image (_additions.scss), so their color is a literal there instead of a theme variable.
      const mark = /\$ta-dark-check-mark:\s*(#[0-9a-f]{6});/i.exec(additionsSource)?.[1];
      expect(mark, '_additions.scss defines the dark mark').toBeDefined();
      expect(contrastRatio(mark!, d.accent), `${mark} on ${d.accent}`).toBeGreaterThanOrEqual(NON_TEXT);
    },
  );

  it('keeps the colors theme.css paints before a business loads equal to Nopal', () => {
    expect(themeDefaults).toEqual(themeCssVariables(getBusinessTheme(BusinessTheme.Nopal)));
  });
});

describe('theme.css', () => {
  it('sets in the dark block every variable the light block sets', () => {
    // A variable missing from the dark block kept its light value in dark mode: the page stayed light.
    expect(Object.keys(dark).sort()).toEqual(Object.keys(light).sort());
  });

  it('draws the focus ring in the accent of each mode', () => {
    // The pairs checked above are the accent's: a ring in any other color would need its own.
    expect(light['--ta-focus-ring']).toBe('var(--ta-accent)');
    expect(dark['--ta-focus-ring']).toBe('var(--ta-accent)');
  });

  it('paints the colors of states with WCAG AA contrast in both modes', () => {
    for (const mode of [light, dark]) {
      for (const state of ['success', 'danger', 'info']) expect(contrastRatio('#ffffff', mode[`--ct-${state}`])).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio('#3d2c00', mode['--ct-warning'])).toBeGreaterThanOrEqual(AA);
      for (const state of ['success', 'danger', 'warning', 'info']) {
        expect(contrastRatio(mode[`--ct-${state}-text-emphasis`], mode[`--ct-${state}-bg-subtle`])).toBeGreaterThanOrEqual(AA);
      }
      for (const chip of ['new', 'confirmed', 'preparing', 'ready', 'done', 'cancelled']) {
        expect(contrastRatio(mode[`--ta-chip-${chip}-fg`], mode[`--ta-chip-${chip}-bg`])).toBeGreaterThanOrEqual(AA);
      }
      expect(contrastRatio(mode['--ta-warning-fg'], mode['--ta-warning-bg'])).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(mode['--ta-danger-fg'], mode['--ta-danger-bg'])).toBeGreaterThanOrEqual(AA);
    }
    for (const state of ['success', 'danger', 'warning', 'info']) {
      expect(contrastRatio(light[`--ct-${state}-text-emphasis`], '#ffffff')).toBeGreaterThanOrEqual(AA);
      for (const surface of DARK_SURFACES) expect(contrastRatio(dark[`--ct-${state}-text-emphasis`], surface)).toBeGreaterThanOrEqual(AA);
    }
    for (const surface of DARK_SURFACES.slice(0, 2)) {
      expect(contrastRatio(dark['--ct-body-color'], surface)).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(dark['--ct-secondary-color'], surface)).toBeGreaterThanOrEqual(AA);
    }
  });

  it('writes text with WCAG AA contrast on every dark surface, a selected option included', () => {
    // Besides the page, a card and a raised row: the side menu and top bar (#3a444e, shell.css), a field (#404954) and
    // the light fill (#464f5b: a light button or badge, a table header, the search box), from _additions.scss.
    for (const surface of [...DARK_SURFACES, '#3a444e', '#404954', '#464f5b']) {
      expect(contrastRatio(dark['--ct-body-color'], surface), `body on ${surface}`).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(dark['--ct-secondary-color'], surface), `secondary on ${surface}`).toBeGreaterThanOrEqual(AA);
      for (const state of ['success', 'danger', 'warning', 'info']) {
        expect(contrastRatio(dark[`--ct-${state}-text-emphasis`], surface), `${state} on ${surface}`).toBeGreaterThanOrEqual(AA);
      }
    }
    // A selected option (a tax policy, a busy table, a chosen role) keeps its text on the tint of the theme.
    for (const { name, dark: d } of BUSINESS_THEMES) {
      expect(contrastRatio(dark['--ct-body-color'], d.accentSubtle), `body on ${name}`).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(dark['--ct-secondary-color'], d.accentSubtle), `secondary on ${name}`).toBeGreaterThanOrEqual(AA);
    }
    // Pagination, list groups and a hovered light button (#f6f7fb in light mode) write in the secondary color, and so
    // does the search box of the top bar (#eef1f3, shell.css), its key included.
    for (const mode of [light, dark]) expect(mode['--ct-tertiary-color']).toBe('var(--ct-secondary-color)');
    for (const surface of ['#f6f7fb', '#eef1f3']) {
      expect(contrastRatio(light['--ct-secondary-color'], surface), `secondary on ${surface}`).toBeGreaterThanOrEqual(AA);
    }
  });
});

describe('shell.css', () => {
  it('writes the name of a condensed menu link in the text color of the accent it grows on', () => {
    // White read at 1.9:1 on the light accents of the dark mode; onAccent on accent passes AA in every theme (above).
    expect(block(':root {\n  --ta-sidebar-width', shellCss)).toMatchObject({
      '--ta-menu-condensed-link-bg': 'var(--tt-accent)',
      '--ta-menu-condensed-link-color': 'var(--tt-on-accent)',
    });
    expect(block(":root[data-bs-theme='dark']", shellCss)).toMatchObject({
      '--ta-menu-condensed-link-bg': 'var(--tt-dark-accent)',
      '--ta-menu-condensed-link-color': 'var(--tt-dark-on-accent)',
    });
    expect(shellCss).toMatch(/\.ta-nav-item:hover \.ta-nav-link \{[^}]*color: var\(--ta-menu-condensed-link-color\);/);
  });
});
