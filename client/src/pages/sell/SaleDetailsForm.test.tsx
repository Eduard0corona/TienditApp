import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import SaleDetailsForm from './SaleDetailsForm';
import { NO_SALE_DETAILS } from './sellModel';

describe('SaleDetailsForm', () => {
  it('names its own fields in each copy of the ticket, beside the catalog and in the sheet of a phone', () => {
    const form = <SaleDetailsForm details={NO_SALE_DETAILS} onChange={vi.fn()} open onToggle={vi.fn()} isFood phoneError="" />;
    render(<>{form}{form}</>);

    const phones = screen.getAllByLabelText('Teléfono');
    expect(new Set(phones).size).toBe(2);
    phones.forEach((phone) => expect(phone).toHaveAccessibleDescription('Con teléfono la venta queda en su historial.'));
    expect(new Set(screen.getAllByLabelText('Nombre')).size).toBe(2);
    expect(new Set(screen.getAllByLabelText('Mesa o referencia')).size).toBe(2);
    expect(new Set(screen.getAllByLabelText('Notas del pedido')).size).toBe(2);
    // A label or a description goes to the first element with its id: no copy repeats one.
    const ids = Array.from(document.querySelectorAll('[id]'), (element) => element.id);
    expect(new Set(ids).size).toBe(ids.length);
  });
});
