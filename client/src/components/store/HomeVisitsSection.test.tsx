import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { HomeVisitChargePolicy } from '../../api/generated/enums';
import type { Schemas } from '../../api/typedClient';
import HomeVisitsSection from './HomeVisitsSection';

type PublicHomeVisits = Schemas['PublicHomeVisitsDto'];

function visits(overrides: Partial<PublicHomeVisits> = {}): PublicHomeVisits {
  return {
    chargePolicy: HomeVisitChargePolicy.CreditedIfHired,
    price: { amount: 350, isStarting: true },
    visitServiceName: 'Visita y diagnóstico',
    postalCodes: ['72000', '72010'],
    postalCodeCount: 2,
    services: [
      { name: 'Destapar drenaje', price: 600, durationMinutes: 60 },
      { name: 'Cambiar llave de lavabo', price: 450, durationMinutes: null },
    ],
    ...overrides,
  };
}

describe('HomeVisitsSection', () => {
  it('says what a visit costs and how it is charged, where it goes, and its services as reference prices', () => {
    render(<HomeVisitsSection visits={visits()} businessName="Plomería Hernández" contactPhone="222 123 4567" pricesIncludeTax />);

    const section = screen.getByRole('region', { name: 'Visitas a domicilio' });
    expect(within(section).getByText('Visita y diagnóstico')).toBeInTheDocument();
    expect(within(section).getByText('Visita desde $350.00, se descuenta si contratas')).toBeInTheDocument();
    expect(within(section).getByText('Si contratas el trabajo, lo que pagaste de la visita se descuenta.')).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Vamos a estos códigos postales' })).getAllByRole('listitem').map((item) => item.textContent))
      .toEqual(['72000', '72010']);
    expect(within(section).getByText('Destapar drenaje').nextSibling).toHaveTextContent('desde $600.00');
    // Reference prices, never a cart.
    expect(within(section).queryByRole('button', { name: /Agregar/ })).not.toBeInTheDocument();

    const request = within(section).getByRole('link', { name: 'Solicitar visita' });
    expect(request.getAttribute('href')).toContain('https://wa.me/522221234567?text=');
    expect(decodeURIComponent(request.getAttribute('href') ?? '')).toContain('Hola Plomería Hernández, quiero pedir una visita a domicilio');
  });

  it('writes a free visit and prices without IVA as the business does, with no request without a phone', () => {
    render(
      <HomeVisitsSection
        visits={visits({ chargePolicy: HomeVisitChargePolicy.Free, price: { amount: 0, isStarting: false }, visitServiceName: null })}
        businessName="Fumigaciones Ruiz"
        contactPhone={null}
        pricesIncludeTax={false}
      />,
    );

    expect(screen.getByText('Visita sin costo')).toBeInTheDocument();
    expect(screen.getByText('Revisamos y te decimos cuánto cuesta, sin costo.')).toBeInTheDocument();
    expect(screen.getByText('desde $450.00 más IVA')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Solicitar visita' })).not.toBeInTheDocument();
  });

  it('shows the first postal codes of a large zone, all of them on request, and says how many more it did not send', async () => {
    const codes = Array.from({ length: 300 }, (_, index) => String(72000 + index));
    render(
      <HomeVisitsSection visits={visits({ postalCodes: codes, postalCodeCount: 340 })} businessName="Cerrajería Pérez" pricesIncludeTax />,
    );

    const list = screen.getByRole('list', { name: 'Vamos a estos códigos postales' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(18);
    await userEvent.click(screen.getByRole('button', { name: 'Ver todos los códigos postales' }));

    expect(within(list).getAllByRole('listitem')).toHaveLength(300);
    expect(screen.getByText('Y 40 más: pregúntanos por el tuyo.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Ver todos los códigos postales' })).not.toBeInTheDocument();
  });
});
