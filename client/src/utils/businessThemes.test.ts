import { describe, expect, it } from 'vitest';
import themeCssSource from '../assets/css/theme.css?raw';
import { BusinessTheme } from '../types';
import { BUSINESS_THEMES, getBusinessTheme, themeCssVariables } from './businessThemes';
import { contrastRatio } from './contrast';

const AA = 4.5;
// Hyper's surfaces in dark mode: the page, a card and a raised row (attention rows, hovered items).
const DARK_SURFACES = ['#343a40', '#37404a', '#3d4854'];
// A Windows checkout with core.autocrlf has theme.css in CRLF; the rules below are found by their line breaks.
const themeCss = themeCssSource.replace(/\r\n/g, '\n');

/** The custom properties of the first top-level rule whose selector starts with `selector`. */
function block(selector: string): Record<string, string> {
  const start = themeCss.indexOf(`\n${selector}`);
  expect(start, `theme.css has a rule for ${selector}`).toBeGreaterThanOrEqual(0);
  const body = themeCss.slice(themeCss.indexOf('{', start) + 1, themeCss.indexOf('}', start));
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

  it('keeps the colors theme.css paints before a business loads equal to Nopal', () => {
    expect(themeDefaults).toEqual(themeCssVariables(getBusinessTheme(BusinessTheme.Nopal)));
  });
});

describe('theme.css', () => {
  it('sets in the dark block every variable the light block sets', () => {
    // A variable missing from the dark block kept its light value in dark mode: the page stayed light.
    expect(Object.keys(dark).sort()).toEqual(Object.keys(light).sort());
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
});
