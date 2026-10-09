import { beforeEach, describe, expect, it } from 'vitest';
import { BusinessTheme } from '../types';
import { getBusinessTheme } from './businessThemes';
import { applyBusinessTheme, previewBusinessTheme, readStoredBusinessTheme, restoreBusinessTheme } from './theme';

const accent = () => document.documentElement.style.getPropertyValue('--tt-accent');
const darkAccent = () => document.documentElement.style.getPropertyValue('--tt-dark-accent');

describe('business theme', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.removeAttribute('style');
    document.head.innerHTML = '<meta name="theme-color" content="#0E7A5C" />';
    previewBusinessTheme(undefined);
    applyBusinessTheme(null);
  });

  it('paints the theme of the business in both modes, the phone bar included, and remembers it', () => {
    applyBusinessTheme(BusinessTheme.RosaMexicano);

    const rosa = getBusinessTheme(BusinessTheme.RosaMexicano);
    expect(accent()).toBe(rosa.light.accent);
    expect(darkAccent()).toBe(rosa.dark.accent);
    expect(document.documentElement.style.getPropertyValue('--tt-accent-rgb')).toBe('194, 24, 91');
    expect(document.documentElement.dataset.businessTheme).toBe(String(BusinessTheme.RosaMexicano));
    expect(document.querySelector('meta[name="theme-color"]')?.getAttribute('content')).toBe(rosa.light.accent);
    expect(readStoredBusinessTheme()).toBe(BusinessTheme.RosaMexicano);
  });

  it('goes back to TienditApp and forgets the business without one', () => {
    applyBusinessTheme(BusinessTheme.Grafito);
    applyBusinessTheme(null);

    expect(accent()).toBe(getBusinessTheme(BusinessTheme.Nopal).light.accent);
    expect(readStoredBusinessTheme()).toBeNull();
  });

  it('shows a preview over the business theme and returns to it, without remembering the preview', () => {
    applyBusinessTheme(BusinessTheme.Talavera);
    previewBusinessTheme(BusinessTheme.Cempasuchil);
    expect(accent()).toBe(getBusinessTheme(BusinessTheme.Cempasuchil).light.accent);
    expect(readStoredBusinessTheme()).toBe(BusinessTheme.Talavera);

    // The business loads while the public menu shows another: the menu keeps its colors.
    applyBusinessTheme(BusinessTheme.Grafito);
    expect(accent()).toBe(getBusinessTheme(BusinessTheme.Cempasuchil).light.accent);

    previewBusinessTheme(undefined);
    expect(accent()).toBe(getBusinessTheme(BusinessTheme.Grafito).light.accent);
  });

  it('restores the last business of the browser before it loads, and ignores what it cannot read', () => {
    window.localStorage.setItem('tienditapp.businessTheme', String(BusinessTheme.Jacaranda));
    document.documentElement.removeAttribute('style');

    restoreBusinessTheme();
    expect(accent()).toBe(getBusinessTheme(BusinessTheme.Jacaranda).light.accent);

    window.localStorage.setItem('tienditapp.businessTheme', 'rosa');
    restoreBusinessTheme();
    expect(accent()).toBe(getBusinessTheme(BusinessTheme.Nopal).light.accent);
  });
});
