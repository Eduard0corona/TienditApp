import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { OrderChannel, PlatformCollectedBy } from '../../types';
import PlatformOrderFields from './PlatformOrderFields';
import { NO_SALE_DETAILS } from './sellModel';

const details = { ...NO_SALE_DETAILS, channel: OrderChannel.Rappi, externalOrderNumber: '152390', collectedBy: PlatformCollectedBy.Platform };

describe('PlatformOrderFields', () => {
  it('names its own fields in each copy of the ticket, beside the catalog and in the sheet of a phone', () => {
    const fields = <PlatformOrderFields platform={OrderChannel.Rappi} details={details} onChange={vi.fn()} folioError="" orderIdError="" onOrderIdBlur={vi.fn()} disabled={false} canCharge />;
    render(<>{fields}{fields}</>);

    const folios = screen.getAllByLabelText('Folio de Rappi');
    expect(new Set(folios).size).toBe(2);
    folios.forEach((folio) => expect(folio).toHaveAccessibleDescription('El número de pedido que ves en Rappi Aliados.'));
    expect(new Set(screen.getAllByLabelText('Nombre para la bolsa (opcional)')).size).toBe(2);
    expect(new Set(screen.getAllByRole('radiogroup', { name: '¿Quién cobró?' })).size).toBe(2);
  });

  it('takes the long id of the order as optional, saying where to find it or what is wrong with it', async () => {
    const onChange = vi.fn();
    const page = render(
      <PlatformOrderFields platform={OrderChannel.UberEats} details={details} onChange={onChange} folioError="" orderIdError="" onOrderIdBlur={vi.fn()} disabled={false} canCharge />,
    );
    const longId = screen.getByLabelText('ID largo del pedido (opcional)');
    expect(longId).not.toBeRequired();
    expect(longId).toHaveAccessibleDescription('El ID del pedido (con guiones) del estado de cuenta de Uber Eats.');
    await userEvent.type(longId, 'a');
    expect(onChange).toHaveBeenLastCalledWith({ externalOrderId: 'a' });
    page.unmount();

    render(
      <PlatformOrderFields
        platform={OrderChannel.UberEats}
        details={{ ...details, externalOrderId: '1234' }}
        onChange={vi.fn()}
        folioError=""
        orderIdError="El ID largo del pedido lleva de 6 a 64 letras, números o guiones."
        onOrderIdBlur={vi.fn()}
        disabled={false}
        canCharge
      />,
    );
    const invalid = screen.getByLabelText('ID largo del pedido (opcional)');
    expect(invalid).toBeInvalid();
    expect(invalid).toHaveAccessibleDescription('El ID largo del pedido lleva de 6 a 64 letras, números o guiones.');
  });

  it('says to charge the courier at the drawer only to whoever can charge', () => {
    const businessCollects = { ...details, collectedBy: PlatformCollectedBy.Business };
    const help = 'El repartidor te paga al recoger: cóbralo en la caja como siempre.';
    const page = render(
      <PlatformOrderFields platform={OrderChannel.DidiFood} details={businessCollects} onChange={vi.fn()} folioError="" orderIdError="" onOrderIdBlur={vi.fn()} disabled={false} canCharge />,
    );
    expect(within(page.container).getByText(help)).toBeInTheDocument();
    page.unmount();

    render(
      <PlatformOrderFields platform={OrderChannel.DidiFood} details={businessCollects} onChange={vi.fn()} folioError="" orderIdError="" onOrderIdBlur={vi.fn()} disabled={false} canCharge={false} />,
    );
    expect(screen.queryByText(help)).not.toBeInTheDocument();
  });
});
