import { expect, test } from '../fixtures';
import { SIZES, open, snap } from '../helpers';

// Pide con tu asistente de IA (Fase 32D, ADR 0096): the addresses of TienditApp's MCP server, without an account and with the
// customer's (Fase 32E), how to add them to Claude and to other assistants, and what an assistant never does. A visitor's
// page: it asks the API nothing.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.describe('Pide con tu asistente', () => {
  test.use({ signedIn: false });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`asistentes ${look.suffix}`, async ({ page }) => {
        await open(page, '/asistentes');
        await expect(page.getByRole('heading', { level: 1, name: 'Pide con tu asistente de IA' })).toBeVisible();
        await expect(page.getByLabel('Dirección sin cuenta', { exact: true })).toHaveValue(/\/mcp$/);
        await expect(page.getByLabel('Dirección con tu cuenta', { exact: true })).toHaveValue(/\/mcp\/cuenta$/);
        await snap(page, `asistentes-${look.suffix}`);
      });
    });
  }
});
