import { asOption, expect, test, type DemoData } from '../fixtures';
import { SIZES, open, snap } from '../helpers';
import { ACCOUNT_PROFILE } from './cuenta-cliente.mocks';

// Conectar un asistente (Fase 32E, ADR 0097): the permission an AI assistant asks to use the customer's account, where
// /oauth/authorize sends the browser. Who asks, what it will see and the site it returns to; a desktop app on this device;
// an account of a business, which cannot connect one; and a request that cannot be taken.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

// What /oauth/authorize passes on to the page (coverage.spec.ts reads the route from each `open`).
const REQUEST = '?response_type=code&client_id=cl1ent&redirect_uri=https%3A%2F%2Fclaude.ai%2Fapi%2Fmcp%2Fauth_callback'
  + '&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256&scope=cuenta&state=abc';

const DESDE_ESTA_COMPUTADORA = asOption((data: DemoData) => {
  data.assistantAuthorization = {
    valid: true, problem: null, errorRedirectUrl: null, clientName: null, redirectSite: '127.0.0.1:33418', redirectsToThisDevice: true,
  };
});

const NO_REGISTRADO = asOption((data: DemoData) => {
  data.assistantAuthorization = {
    valid: false,
    problem: 'Este asistente no está registrado en TienditApp. Vuelve a agregar el conector desde tu asistente.',
    errorRedirectUrl: null, clientName: null, redirectSite: null, redirectsToThisDevice: false,
  };
});

test.describe('Conectar un asistente', () => {
  test.use({ profile: ACCOUNT_PROFILE });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`autorizar ${look.suffix}`, async ({ page }) => {
        await open(page, '/autorizar' + REQUEST);
        await expect(page.getByRole('heading', { name: 'Claude quiere usar tu cuenta de TienditApp' })).toBeVisible();
        await expect(page.getByRole('button', { name: 'Permitir', exact: true })).toBeVisible();
        await snap(page, `autorizar-${look.suffix}`);
      });
    });
  }

  test.describe('una app de esta computadora', () => {
    test.use({ prepareData: DESDE_ESTA_COMPUTADORA });

    test('autorizar app de esta computadora', async ({ page }) => {
      await open(page, '/autorizar' + REQUEST);
      await expect(page.getByRole('note')).toContainText('Regresa a una aplicación de esta computadora');
      await snap(page, 'autorizar-app-de-esta-computadora');
    });
  });

  test.describe('un asistente que no está registrado', () => {
    test.use({ prepareData: NO_REGISTRADO });

    test('autorizar asistente no registrado', async ({ page }) => {
      await open(page, '/autorizar' + REQUEST);
      await expect(page.getByRole('heading', { name: 'No se puede conectar este asistente' })).toBeVisible();
      await snap(page, 'autorizar-asistente-no-registrado');
    });
  });
});

test.describe('Conectar un asistente con la cuenta de un negocio', () => {
  test('autorizar con cuenta de negocio', async ({ page }) => {
    await open(page, '/autorizar' + REQUEST);
    await expect(page.getByRole('heading', { name: 'Inicia sesión con tu cuenta de cliente' })).toBeVisible();
    await snap(page, 'autorizar-con-cuenta-de-negocio');
  });
});
