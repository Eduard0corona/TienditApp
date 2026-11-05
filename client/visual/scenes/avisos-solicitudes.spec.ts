import { asOption, expect, test } from '../fixtures';
import { open, snapElement } from '../helpers';

// Los avisos de solicitudes (Fase 29N): in Mi perfil, whoever answers the requests online chooses whether each one reaches
// them by email. The owner gets them until she chooses (the profile scene of caja-config shows it on); here, turned off and
// with her email not confirmed yet.

test.describe('Avisos de solicitudes', () => {
  test.use({
    prepareData: asOption((data) => {
      data.requestEmails = { enabled: false, email: data.profile.email, emailConfirmed: false };
    }),
  });

  test('perfil con los avisos apagados y el correo sin confirmar', async ({ page }) => {
    await open(page, '/profile');
    const card = page.locator('.card', { has: page.getByRole('heading', { name: 'Avisos de solicitudes' }) });
    await expect(card.getByRole('switch', { name: 'Recibir un correo con cada solicitud de cita o de visita' })).not.toBeChecked();
    await expect(card.getByText('Tu correo todavía no está confirmado: no te llegan hasta que lo confirmes.')).toBeVisible();
    await snapElement(card, 'perfil-avisos-apagados-sin-confirmar');
  });
});
