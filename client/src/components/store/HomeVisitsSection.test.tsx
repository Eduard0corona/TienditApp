import { render as renderInDom, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { MemoryRouter } from 'react-router-dom';
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

/** The section links to the request page: it needs a router. */
const render = (ui: ReactElement) => renderInDom(<MemoryRouter>{ui}</MemoryRouter>);

describe('HomeVisitsSection', () => {
  it('says what a visit costs and how it is charged, where it goes, and its services as reference prices', () => {
    render(<HomeVisitsSection visits={visits()} slug="plomeria-hernandez" pricesIncludeTax />);

    const section = screen.getByRole('region', { name: 'Visitas a domicilio' });
    expect(within(section).getByText('Visita y diagnóstico')).toBeInTheDocument();
    expect(within(section).getByText('Visita desde $350.00, se descuenta si contratas')).toBeInTheDocument();
    expect(within(section).getByText('Si contratas el trabajo, lo que pagaste de la visita se descuenta.')).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Vamos a estos códigos postales' })).getAllByRole('listitem').map((item) => item.textContent))
      .toEqual(['72000', '72010']);
    expect(within(section).getByText('Destapar drenaje').nextSibling).toHaveTextContent('desde $600.00');
    // Reference prices, never a cart.
    expect(within(section).queryByRole('button', { name: /Agregar/ })).not.toBeInTheDocument();

    // The visit is asked for online (Fase 29B), on its request page.
    expect(within(section).getByRole('link', { name: 'Solicitar visita' })).toHaveAttribute('href', '/m/plomeria-hernandez/visita');
  });

  it('writes a free visit and prices without IVA as the business does, and asks for it online even without a phone', () => {
    render(
      <HomeVisitsSection
        visits={visits({ chargePolicy: HomeVisitChargePolicy.Free, price: { amount: 0, isStarting: false }, visitServiceName: null })}
        slug="fumigaciones-ruiz"
        pricesIncludeTax={false}
      />,
    );

    expect(screen.getByText('Visita sin costo')).toBeInTheDocument();
    expect(screen.getByText('Revisamos y te decimos cuánto cuesta, sin costo.')).toBeInTheDocument();
    expect(screen.getByText('desde $450.00 más IVA')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Solicitar visita' })).toHaveAttribute('href', '/m/fumigaciones-ruiz/visita');
  });

  it('says what the visit costs where it costs nothing only in some postal codes, without calling it free', () => {
    render(
      <HomeVisitsSection
        visits={visits({ chargePolicy: HomeVisitChargePolicy.Charged, price: { amount: 0, isStarting: true } })}
        slug="plomeria-hernandez"
        pricesIncludeTax
      />,
    );

    expect(screen.getByText('Visita desde $0.00')).toBeInTheDocument();
    expect(screen.getByText('Pagas la visita aunque no contrates el trabajo.')).toBeInTheDocument();
    expect(screen.queryByText(/sin costo/)).not.toBeInTheDocument();
  });

  it('words a visit that costs nothing anywhere as free, whatever the policy', () => {
    render(
      <HomeVisitsSection
        visits={visits({ chargePolicy: HomeVisitChargePolicy.CreditedIfHired, price: { amount: 0, isStarting: false } })}
        slug="plomeria-hernandez"
        pricesIncludeTax
      />,
    );

    expect(screen.getByText('Visita sin costo')).toBeInTheDocument();
    expect(screen.getByText('Revisamos y te decimos cuánto cuesta, sin costo.')).toBeInTheDocument();
    expect(screen.queryByText(/se descuenta/)).not.toBeInTheDocument();
  });

  it('shows the first postal codes of a large zone, all of them on request, and says how many more it did not send', async () => {
    const codes = Array.from({ length: 300 }, (_, index) => String(72000 + index));
    render(
      <HomeVisitsSection visits={visits({ postalCodes: codes, postalCodeCount: 340 })} slug="cerrajeria-perez" pricesIncludeTax />,
    );

    const list = screen.getByRole('list', { name: 'Vamos a estos códigos postales' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(18);
    await userEvent.click(screen.getByRole('button', { name: 'Ver todos los códigos postales' }));

    expect(within(list).getAllByRole('listitem')).toHaveLength(300);
    // The button went away: the keyboard is on the list it opened.
    expect(list).toHaveFocus();
    expect(screen.getByText('Y 40 más: pregúntanos por el tuyo.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Ver todos los códigos postales' })).not.toBeInTheDocument();
  });
});
