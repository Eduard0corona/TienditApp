import { test as base, expect } from '@playwright/test';
import { installMocks, NOW_ISO } from './mocks/api.mjs';

export { expect };

/** The demo data as data.mjs builds it; scenes change what they need through `prepareData`. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export type DemoData = Record<string, any>;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Route = ['GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE', RegExp, (match: RegExpMatchArray, query: URLSearchParams, body: any) => unknown];

export interface SceneOptions {
  /** Signed in as the owner of Café La Esquina (the default), or a visitor. */
  signedIn: boolean;
  /** Changes to the signed-in user: role, tenant role, permissions. */
  profile: Record<string, unknown> | undefined;
  /** The app's light or dark mode (the user's choice, stored in the browser). */
  colorMode: 'light' | 'dark';
  /** The business theme (BusinessTheme 0 to 7). */
  businessTheme: number;
  /** Changes to the demo data before the first request. */
  prepareData: ((data: DemoData) => void) | undefined;
  /** Routes of a scene file, tried before the shared ones. */
  extraRoutes: ((data: DemoData, helpers: { ok: (json: unknown) => { status: number; json: unknown } }) => Route[]) | undefined;
}

/**
 * Every scene runs on the same day (2026-10-04 13:25 in Mexico City), with the API answered from the demo data. A
 * request nothing answers, or an error on the page, fails the scene: a missing answer would otherwise turn into an
 * empty screen that looks fine.
 */
export const test = base.extend<SceneOptions>({
  signedIn: [true, { option: true }],
  profile: [undefined, { option: true }],
  colorMode: ['light', { option: true }],
  businessTheme: [0, { option: true }],
  prepareData: [undefined, { option: true }],
  extraRoutes: [undefined, { option: true }],

  page: async ({ page, context, baseURL, signedIn, profile, colorMode, businessTheme, prepareData, extraRoutes }, use) => {
    const errors: string[] = [];
    page.on('pageerror', (error) => errors.push(`${error.name}: ${error.message}`));

    await context.clock.setFixedTime(new Date(NOW_ISO));
    await context.addInitScript(({ signedIn, colorMode, businessTheme }) => {
      try {
        if (signedIn) window.localStorage.setItem('fpd_has_session', '1');
        window.localStorage.setItem('tienditapp.theme', colorMode);
        window.localStorage.setItem('tienditapp.businessTheme', String(businessTheme));
      } catch {
        // Storage blocked: the app falls back to its defaults.
      }
    }, { signedIn, colorMode, businessTheme });

    const mocks = await installMocks(context, {
      origin: new URL(baseURL!).origin,
      profile,
      prepareData: (data: DemoData) => {
        data.tenant.theme = businessTheme;
        prepareData?.(data);
      },
      extraRoutes,
    });

    await use(page);

    expect(mocks.unmocked, 'llamadas a la API sin respuesta simulada (agrega la ruta en mocks/)').toEqual([]);
    expect(errors, 'errores de JavaScript en la página').toEqual([]);
  },
});
