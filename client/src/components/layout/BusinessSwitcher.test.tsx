import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { MyTenantDto } from '../../types';
import BusinessSwitcher from './BusinessSwitcher';

const businesses: MyTenantDto[] = [
  { tenantId: 't1', name: 'Café La Esquina', slug: 'esquina', role: 'Owner', status: 'Active', logoUrl: 'https://cdn/esquina_xs.webp' },
  { tenantId: 't2', name: 'Lavandería Burbujas', slug: 'burbujas', role: 'Staff', status: 'Active', logoUrl: null },
];

function renderSwitcher(overrides: Partial<Parameters<typeof BusinessSwitcher>[0]> = {}) {
  const props = {
    businesses,
    currentId: 't1',
    currentName: 'Café La Esquina',
    currentLogoUrl: 'https://cdn/esquina_xs.webp',
    disabled: false,
    switching: false,
    error: '',
    canOpenBusiness: true,
    onSwitch: vi.fn(),
    onOpenBusiness: vi.fn(),
    ...overrides,
  };
  return { ...render(<BusinessSwitcher {...props} />), props };
}

describe('BusinessSwitcher', () => {
  it('shows the logo of the business it is in, or the store icon while it has none', () => {
    const { rerender, props } = renderSwitcher();
    const button = screen.getByTitle('Cambiar de negocio');
    expect(button).toHaveTextContent('Negocio actual: Café La Esquina');
    expect(button.querySelector('img')).toHaveAttribute('src', 'https://cdn/esquina_xs.webp');

    rerender(<BusinessSwitcher {...props} currentLogoUrl={null} />);
    expect(button.querySelector('img')).toBeNull();
    expect(button.querySelector('.ri-store-2-line')).not.toBeNull();
  });

  it('lists every business with its logo or initial, and switches to the one picked', async () => {
    const { props } = renderSwitcher();
    const menu = screen.getByRole('heading', { name: 'Cambiar de negocio' }).parentElement!;

    const esquina = within(menu).getByRole('button', { name: /Café La Esquina/ });
    const burbujas = within(menu).getByRole('button', { name: /Lavandería Burbujas/ });
    expect(esquina).toHaveAttribute('aria-current', 'true');
    expect(esquina.querySelector('img')).toHaveAttribute('src', 'https://cdn/esquina_xs.webp');
    expect(burbujas.querySelector('.photo-initial')).toHaveTextContent('L');
    expect(burbujas).toHaveTextContent('Empleado');

    await userEvent.click(burbujas);
    expect(props.onSwitch).toHaveBeenCalledWith('t2');
    await userEvent.click(within(menu).getByRole('button', { name: 'Abrir otro negocio' }));
    expect(props.onOpenBusiness).toHaveBeenCalled();
  });
});
