import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { OrderChannel, PlatformCollectedBy } from '../../types';
import PlatformOrderFields from './PlatformOrderFields';
import { NO_SALE_DETAILS } from './sellModel';

const details = { ...NO_SALE_DETAILS, channel: OrderChannel.Rappi, externalOrderNumber: '152390', collectedBy: PlatformCollectedBy.Platform };

describe('PlatformOrderFields', () => {
  it('names its own fields in each copy of the ticket, beside the catalog and in the sheet of a phone', () => {
    const fields = <PlatformOrderFields platform={OrderChannel.Rappi} details={details} onChange={vi.fn()} folioError="" disabled={false} />;
    render(<>{fields}{fields}</>);

    const folios = screen.getAllByLabelText('Folio de Rappi');
    expect(new Set(folios).size).toBe(2);
    folios.forEach((folio) => expect(folio).toHaveAccessibleDescription('El número de pedido que ves en Rappi Aliados.'));
    expect(new Set(screen.getAllByLabelText('Nombre para la bolsa (opcional)')).size).toBe(2);
    expect(new Set(screen.getAllByRole('radiogroup', { name: '¿Quién cobró?' })).size).toBe(2);
  });
});
