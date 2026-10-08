import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { CfdiTaxObject, UnitType, type ProductDto } from '../../types';
import OptionsModal from './OptionsModal';
import type { OptionsRequest } from './sellModel';

const latte: ProductDto = {
  id: 'latte',
  categoryId: 'c1',
  name: 'Latte',
  sortOrder: 1,
  priceAmount: 55,
  priceCurrency: 'MXN',
  isTaxExempt: false,
  hasDiscount: false,
  taxObject: CfdiTaxObject.Subject,
  isAvailable: true,
  isFeatured: false,
  tracksStock: false,
  isLowStock: false,
  isTrackable: false,
  notifyCustomer: false,
  unit: UnitType.Unit,
  optionGroups: [
    { id: 'milk', name: 'Leche', minSelections: 0, maxSelections: 1, options: [
      { id: 'almond', name: 'Almendra', priceDelta: 8, isAvailable: true },
    ] },
    { id: 'extras', name: 'Extras', minSelections: 0, maxSelections: 2, options: [
      { id: 'shot', name: 'Shot extra', priceDelta: 10, isAvailable: true },
      { id: 'cream', name: 'Crema', priceDelta: 7, isAvailable: true },
      { id: 'cinnamon', name: 'Canela', priceDelta: 0, isAvailable: true },
    ] },
  ],
};

function Harness({ onConfirm }: { onConfirm: (product: ProductDto, optionIds: string[], quantity: number) => void }) {
  const [request, setRequest] = useState<OptionsRequest | null>({ product: latte, optionIds: [], quantityText: '1' });
  return <OptionsModal request={request} onChange={setRequest} onConfirm={onConfirm} onClose={() => setRequest(null)} />;
}

describe('OptionsModal', () => {
  it('clears an optional single choice with a second tap and stops each group at its maximum', async () => {
    const onConfirm = vi.fn();
    render(<Harness onConfirm={onConfirm} />);

    const almond = screen.getByLabelText(/Almendra/);
    await userEvent.click(almond);
    expect(almond).toBeChecked();
    await userEvent.click(almond);
    expect(almond).not.toBeChecked();

    await userEvent.click(screen.getByLabelText(/Shot extra/));
    await userEvent.click(screen.getByLabelText(/Crema/));
    expect(screen.getByLabelText(/Canela/)).toBeDisabled();

    await userEvent.clear(screen.getByLabelText('Cantidad'));
    await userEvent.type(screen.getByLabelText('Cantidad'), '2');
    await userEvent.click(screen.getByRole('button', { name: 'Agregar · $144.00' }));

    expect(onConfirm).toHaveBeenCalledWith(latte, ['shot', 'cream'], 2);
  });

  it('refuses a fraction of a piece', async () => {
    const onConfirm = vi.fn();
    render(<Harness onConfirm={onConfirm} />);

    await userEvent.clear(screen.getByLabelText('Cantidad'));
    await userEvent.type(screen.getByLabelText('Cantidad'), '1.5');

    expect(screen.getByRole('button', { name: /^Agregar/ })).toBeDisabled();
  });
});
