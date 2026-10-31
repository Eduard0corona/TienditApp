import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { CfdiMonthEmail } from '../../api/invoicing';
import { CfdiMonthEmailFailure } from '../../types';
import { AccountantEmailStatus } from './AccountantEmails';

function monthEmail(patch: Partial<CfdiMonthEmail> = {}): CfdiMonthEmail {
  return {
    id: 'm-1', year: 2026, month: 10, to: 'contador@despacho.mx', requestedAt: '2026-11-01T14:00:00Z', byTienditApp: false,
    sentAt: null, cfdiCount: null, failedAt: null, failure: undefined, failedAttempts: 0, lastFailedAttemptAt: null,
    ...patch,
  };
}

describe('AccountantEmailStatus', () => {
  it('says nothing while the month went to nobody', () => {
    const { container } = render(<AccountantEmailStatus emails={[]} />);

    expect(container).toBeEmptyDOMElement();
  });

  it('says how many CFDI the latest one carried, and that TienditApp sent it on its own', () => {
    render(<AccountantEmailStatus emails={[
      monthEmail({ sentAt: '2026-11-01T14:01:00Z', cfdiCount: 128, byTienditApp: true }),
      monthEmail({ id: 'm-0', failedAt: '2026-10-30T10:00:00Z', failure: CfdiMonthEmailFailure.Rejected }),
    ]} />);

    const status = screen.getByRole('status');
    expect(status).toHaveTextContent(/^Se mandaron a contador@despacho\.mx el .+, con 128 CFDI\. TienditApp los mandó solo, al empezar el mes\.$/);
  });

  it('says it is on its way, and that TienditApp tries again when the mail server did not take it', () => {
    render(<AccountantEmailStatus emails={[monthEmail({ failedAttempts: 2 })]} />);

    expect(screen.getByRole('status')).toHaveTextContent(
      'Mandando los CFDI a contador@despacho.mx… El servidor de correo no los ha tomado; TienditApp lo vuelve a intentar.');
  });

  it.each([
    [CfdiMonthEmailFailure.Rejected, 'el servidor de correo lo rechazó. Revisa la dirección con tu contador y vuelve a mandarlo.'],
    [CfdiMonthEmailFailure.NotConfigured, 'TienditApp no tiene el envío de correos configurado. Descarga los XML y compártelos.'],
    [CfdiMonthEmailFailure.NotDelivered, 'el servidor de correo no lo tomó después de varios intentos. Vuelve a mandarlo.'],
    [CfdiMonthEmailFailure.TooLarge, 'el archivo del mes pesa más de lo que aguanta un correo. Descarga los XML y compártelos por otro medio.'],
    [CfdiMonthEmailFailure.TooMany, 'el mes tiene más de 5,000 CFDI, los que junta un correo. Pide los XML con la descarga masiva del portal del SAT.'],
  ])('says why it did not go (%s)', (failure, why) => {
    render(<AccountantEmailStatus emails={[monthEmail({ failedAt: '2026-11-01T14:05:00Z', failure })]} />);

    expect(screen.getByRole('status')).toHaveTextContent(`No se mandaron a contador@despacho.mx: ${why}`);
  });
});
