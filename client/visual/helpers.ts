import { expect, type Locator, type Page } from '@playwright/test';

/** The sizes the scenes are taken at: a desktop, a tablet (the condensed menu), phones and a tablet held upright. */
export const SIZES = {
  desktop: { width: 1440, height: 900 },
  tablet: { width: 1024, height: 768 },
  upright: { width: 768, height: 1024 },
  phone: { width: 390, height: 844 },
  smallPhone: { width: 360, height: 740 },
} as const;

/**
 * Waits until the screen has drawn: no spinner, no "Cargando…", fonts and pictures loaded and no request pending.
 * Without it a screenshot can catch a screen halfway through loading.
 */
export async function settle(page: Page) {
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => {
    const visible = (element: Element) => {
      const box = (element as HTMLElement).getBoundingClientRect();
      return box.width > 0 || box.height > 0;
    };
    const spinning = [...document.querySelectorAll('.spinner-border, .spinner-grow')].some(visible);
    const loading = [...document.querySelectorAll('body *')].some((element) =>
      element.childElementCount === 0 && visible(element) && /^Cargando…?\.{0,3}$/.test(element.textContent?.trim() ?? ''));
    // An image not drawn at all (display: none, as the favorites past «Ver todos») never loads while lazy: it does not
    // count. One drawn has its box even before it loads.
    const pictures = [...document.images].every((image) => image.complete || image.getClientRects().length === 0);
    return !spinning && !loading && pictures;
  }, null, { timeout: 20_000 });
  await page.evaluate(() => document.fonts.ready);
  // Transitions and finite animations end first (a menu sliding in, a dialog fading in). One still running when the
  // screenshot stops it leaves its element on a layer of its own, with its text smoothed another way: the same screen
  // came out different depending on how busy the machine was.
  await page.waitForFunction(() => document.getAnimations().every((animation) =>
    animation.playState !== 'running' || animation.effect?.getComputedTiming().endTime === Infinity), null, { timeout: 20_000 });
}

/** Opens a route of the app and waits until it has drawn. */
export async function open(page: Page, route: string) {
  await page.goto(route, { waitUntil: 'domcontentloaded' });
  await settle(page);
}

/** Waits for a dialog (modal or side panel) to finish opening. It is found by its name: the text of its title. */
export async function dialog(page: Page, name: string | RegExp): Promise<Locator> {
  const found = page.getByRole('dialog', { name });
  await found.waitFor({ state: 'visible' });
  await settle(page);
  return found;
}

interface SnapOptions {
  /** The whole page, not only what fits on the screen (true by default). */
  fullPage?: boolean;
  /** Parts that change between runs and have nothing to do with the design. */
  mask?: Locator[];
}

/**
 * Compares the screen with the baseline (`npm run visual:baseline`). The pointer leaves any hover first and the page
 * goes back to the top, so the shot does not depend on what the scene did before.
 */
export async function snap(page: Page, name: string, options: SnapOptions = {}) {
  await page.mouse.move(0, 0);
  await page.evaluate(() => window.scrollTo({ top: 0, left: 0, behavior: 'instant' }));
  await settle(page);
  await expect(page).toHaveScreenshot(`${name}.png`, { fullPage: options.fullPage ?? true, mask: options.mask });
}

/** Like `snap`, of one element (a card, a dialog, a ticket). */
export async function snapElement(locator: Locator, name: string) {
  await locator.page().mouse.move(0, 0);
  await settle(locator.page());
  await expect(locator).toHaveScreenshot(`${name}.png`);
}
