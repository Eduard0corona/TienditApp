import { BusinessTheme } from '../types';

/** The colors of one mode of a theme. Every pair the app paints is checked in businessThemes.test.ts (WCAG AA). */
export interface ThemePalette {
  /** Primary buttons, the current menu item, toggles and focus. */
  accent: string;
  accentHover: string;
  accentActive: string;
  /** Text and icons on the accent (a primary button). */
  onAccent: string;
  /** Background of selected things: the menu item, a chosen option, the open cash drawer chip. */
  accentSubtle: string;
  accentBorder: string;
  /** Text on accentSubtle. */
  accentText: string;
  link: string;
  linkHover: string;
}

export interface BusinessThemeDefinition {
  theme: BusinessTheme;
  name: string;
  description: string;
  /** The light mode also tints the page background; the dark mode keeps the app's neutral grays. */
  light: ThemePalette & { pageBg: string };
  dark: ThemePalette;
}

/**
 * The themes an owner can pick (Configuración > Negocio > Apariencia), in the order the picker shows them. The business
 * stores only which one (TenantSettings.Theme); the colors live here. A theme changes the accent and the tint of the
 * page, never the colors of states (ready, pending, cancelled), so nobody misreads a status after a change of theme.
 * Nopal keeps the values TienditApp shipped with.
 */
export const BUSINESS_THEMES: readonly BusinessThemeDefinition[] = [
  {
    theme: BusinessTheme.Nopal,
    name: 'Nopal',
    description: 'El verde de TienditApp. Fresco y confiable.',
    light: {
      accent: '#0e7a5c', accentHover: '#0b6650', accentActive: '#095443', onAccent: '#ffffff',
      accentSubtle: '#e2f1eb', accentBorder: '#a6d6c3', accentText: '#085440', link: '#0e7a5c',
      linkHover: '#0b6650', pageBg: '#f3f5f2',
    },
    dark: {
      accent: '#2fb386', accentHover: '#4cc79c', accentActive: '#8fe0c2', onAccent: '#0b1f18',
      accentSubtle: '#365555', accentBorder: '#1f6e55', accentText: '#8fe0c2', link: '#5fd0a8',
      linkHover: '#8fe0c2',
    },
  },
  {
    theme: BusinessTheme.Talavera,
    name: 'Talavera',
    description: 'Azul cobalto de la loza poblana.',
    light: {
      accent: '#1d4fbf', accentHover: '#0e3fae', accentActive: '#0332a1', onAccent: '#ffffff',
      accentSubtle: '#e9f0ff', accentBorder: '#b0cbfd', accentText: '#08359d', link: '#1d4fbf',
      linkHover: '#0e3fae', pageBg: '#f3f6fb',
    },
    dark: {
      accent: '#9cbefe', accentHover: '#b5cffe', accentActive: '#cedffd', onAccent: '#0f1a2f',
      accentSubtle: '#49576a', accentBorder: '#395898', accentText: '#c4d8fe', link: '#9cbefe',
      linkHover: '#bad2fe',
    },
  },
  {
    theme: BusinessTheme.Jacaranda,
    name: 'Jacaranda',
    description: 'El morado de las jacarandas en primavera.',
    light: {
      accent: '#6a3fb5', accentHover: '#5c2fa5', accentActive: '#522197', onAccent: '#ffffff',
      accentSubtle: '#f1edff', accentBorder: '#cfc0fc', accentText: '#4e208f', link: '#6a3fb5',
      linkHover: '#5c2fa5', pageBg: '#f6f5fa',
    },
    dark: {
      accent: '#c3affd', accentHover: '#d1c3fd', accentActive: '#e0d7fe', onAccent: '#1c162d',
      accentSubtle: '#50546a', accentBorder: '#614c91', accentText: '#dad0fd', link: '#c3affd',
      linkHover: '#d5c7ff',
    },
  },
  {
    theme: BusinessTheme.RosaMexicano,
    name: 'Rosa mexicano',
    description: 'Alegre y festivo; resalta en redes.',
    light: {
      accent: '#c2185b', accentHover: '#ad044e', accentActive: '#990845', onAccent: '#ffffff',
      accentSubtle: '#ffeaee', accentBorder: '#fbb4c2', accentText: '#7c0637', link: '#c2185b',
      linkHover: '#ad044e', pageBg: '#faf4f5',
    },
    dark: {
      accent: '#fd9db3', accentHover: '#ffb6c6', accentActive: '#ffd0d9', onAccent: '#2c1117',
      accentSubtle: '#5b515d', accentBorder: '#8c3d52', accentText: '#fdc7d1', link: '#fd9db3',
      linkHover: '#febcc9',
    },
  },
  {
    theme: BusinessTheme.Jamaica,
    name: 'Jamaica',
    description: 'Vino profundo, como el agua de jamaica.',
    light: {
      accent: '#9b1c47', accentHover: '#8a003a', accentActive: '#780031', onAccent: '#ffffff',
      accentSubtle: '#feeaed', accentBorder: '#fab5c1', accentText: '#7c0734', link: '#9b1c47',
      linkHover: '#8a003a', pageBg: '#faf4f5',
    },
    dark: {
      accent: '#fe9eb1', accentHover: '#fdb9c5', accentActive: '#fed1d8', onAccent: '#2c1117',
      accentSubtle: '#5b515d', accentBorder: '#8c3d50', accentText: '#fdc7d0', link: '#fe9eb1',
      linkHover: '#fdbdc8',
    },
  },
  {
    theme: BusinessTheme.Cempasuchil,
    name: 'Cempasúchil',
    description: 'Naranja cálido de la flor de cempasúchil.',
    light: {
      accent: '#b4510c', accentHover: '#9e470b', accentActive: '#8e3d02', onAccent: '#ffffff',
      accentSubtle: '#feece3', accentBorder: '#f3bda2', accentText: '#6c2d02', link: '#b4510c',
      linkHover: '#9e470b', pageBg: '#faf4f2',
    },
    dark: {
      accent: '#fda579', accentHover: '#ffbd9c', accentActive: '#fed4bf', onAccent: '#2b1307',
      accentSubtle: '#5b5252', accentBorder: '#884622', accentText: '#fdcbb3', link: '#fda579',
      linkHover: '#fdc2a4',
    },
  },
  {
    theme: BusinessTheme.CafeDeOlla,
    name: 'Café de olla',
    description: 'Café tostado; cálido para cafeterías y panaderías.',
    light: {
      accent: '#7a4a2a', accentHover: '#6b3c1c', accentActive: '#5f3110', onAccent: '#ffffff',
      accentSubtle: '#faede5', accentBorder: '#e0c4b3', accentText: '#613617', link: '#7a4a2a',
      linkHover: '#6b3c1c', pageBg: '#f8f5f3',
    },
    dark: {
      accent: '#e7b08e', accentHover: '#f8c09e', accentActive: '#fed4bb', onAccent: '#25170e',
      accentSubtle: '#575456', accentBorder: '#74523d', accentText: '#ecd2c2', link: '#e7b08e',
      linkHover: '#ebc9b4',
    },
  },
  {
    theme: BusinessTheme.Grafito,
    name: 'Grafito',
    description: 'Sobrio y neutro para servicios y despachos.',
    light: {
      accent: '#3d4a57', accentHover: '#303d49', accentActive: '#26323f', onAccent: '#ffffff',
      accentSubtle: '#edf0f4', accentBorder: '#c4cbd3', accentText: '#333f4b', link: '#3d4a57',
      linkHover: '#303d49', pageBg: '#f5f6f7',
    },
    dark: {
      accent: '#b0c0cf', accentHover: '#c0d0df', accentActive: '#d0e0f0', onAccent: '#171b1f',
      accentSubtle: '#4d5762', accentBorder: '#535c65', accentText: '#d1d8df', link: '#b0c0cf',
      linkHover: '#c9d2dc',
    },
  },
];

export const DEFAULT_BUSINESS_THEME = BUSINESS_THEMES[0];

/** The definition of a theme; Nopal for a value this version does not know (a newer API, a missing setting). */
export function getBusinessTheme(theme: BusinessTheme | null | undefined): BusinessThemeDefinition {
  return BUSINESS_THEMES.find((definition) => definition.theme === theme) ?? DEFAULT_BUSINESS_THEME;
}

function rgb(hex: string): string {
  const value = hex.replace('#', '');
  return [0, 2, 4].map((start) => parseInt(value.slice(start, start + 2), 16)).join(', ');
}

/**
 * The CSS custom properties of a theme, as theme.css reads them: --tt-* for the light mode and --tt-dark-* for the
 * dark one (a dark section inside a light page, like the kitchen, takes its accent from the same theme).
 */
export function themeCssVariables(definition: BusinessThemeDefinition): Record<string, string> {
  const { light, dark } = definition;
  return {
    '--tt-accent': light.accent,
    '--tt-accent-rgb': rgb(light.accent),
    '--tt-accent-hover': light.accentHover,
    '--tt-accent-active': light.accentActive,
    '--tt-on-accent': light.onAccent,
    '--tt-accent-subtle': light.accentSubtle,
    '--tt-accent-border': light.accentBorder,
    '--tt-accent-text': light.accentText,
    '--tt-link': light.link,
    '--tt-link-rgb': rgb(light.link),
    '--tt-link-hover': light.linkHover,
    '--tt-link-hover-rgb': rgb(light.linkHover),
    '--tt-page-bg': light.pageBg,
    '--tt-dark-accent': dark.accent,
    '--tt-dark-accent-rgb': rgb(dark.accent),
    '--tt-dark-accent-hover': dark.accentHover,
    '--tt-dark-accent-active': dark.accentActive,
    '--tt-dark-on-accent': dark.onAccent,
    '--tt-dark-accent-subtle': dark.accentSubtle,
    '--tt-dark-accent-border': dark.accentBorder,
    '--tt-dark-accent-text': dark.accentText,
    '--tt-dark-link': dark.link,
    '--tt-dark-link-rgb': rgb(dark.link),
    '--tt-dark-link-hover': dark.linkHover,
    '--tt-dark-link-hover-rgb': rgb(dark.linkHover),
  };
}
