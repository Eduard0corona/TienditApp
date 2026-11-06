import { asOption, expect, test } from '../fixtures';
import { open, snapElement } from '../helpers';

// Los avisos por correo (Fases 29N y 29O): in Mi perfil, whoever answers the requests online, or works the quotes, chooses
// which notices reach them by email. The owner gets both until she chooses (the profile scene of caja-config shows them on);
// here, the agenda's turned off, the quotes' on, and her email not confirmed yet.

test.describe('Avisos por correo', () => {
  test.use({
    prepareData: asOption((data) => {
      data.requestEmails = { enabled: false, email: data.profile.email, emailConfirmed: false };
      data.quoteReplyEmails = { enabled: true, email: data.profile.email, emailConfirmed: false };
    }),
  });

  test('perfil con los avisos de la Agenda apagados y el correo sin confirmar', async ({ page }) => {
    await open(page, '/profile');
    const card = page.locator('.card', { has: page.getByRole('heading', { name: 'Avisos por correo' }) });
    await expect(card.getByRole('switch', { name: 'Citas en línea' })).not.toBeChecked();
    await expect(card.getByRole('switch', { name: 'Cotizaciones' })).toBeChecked();
    await expect(card.getByText('Tu correo todavía no está confirmado: no te llegan hasta que lo confirmes.')).toBeVisible();
    await snapElement(card, 'perfil-avisos-apagados-sin-confirmar');
  });
});
