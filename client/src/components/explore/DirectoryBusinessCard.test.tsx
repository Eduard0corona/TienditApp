import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { BusinessCategory, HomeVisitChargePolicy, OrderConfirmTime, QuoteTime, ResponseTime } from '../../api/generated/enums';
import type { DirectoryBusiness } from '../../api/publicDirectoryApi';
import DirectoryBusinessCard from './DirectoryBusinessCard';

function business(overrides: Partial<DirectoryBusiness>): DirectoryBusiness {
  return {
    name: 'Taquería El Güero',
    slug: 'el-guero',
    category: BusinessCategory.Food,
    theme: 0,
    deliveryOnly: false,
    deliveryAvailable: false,
    pricesIncludeTax: true,
    booksOnline: false,
    receivesVisits: false,
    acceptsOrders: false,
    ordersPaused: false,
    closed: false,
    ...overrides,
  };
}

function renderCard(card: DirectoryBusiness) {
  render(<MemoryRouter><DirectoryBusinessCard business={card} /></MemoryRouter>);
}

describe('DirectoryBusinessCard', () => {
  it('shows the rating of a business from three reviews, read as a sentence', () => {
    renderCard(business({ rating: { count: 23, average: 4.6 } }));

    const link = screen.getByRole('link');
    expect(link).toHaveTextContent('4.6 (23)');
    expect(link).toHaveAccessibleName(expect.stringContaining('Calificación 4.6 de 5, 23 reseñas'));
  });

  it('shows no rating with fewer than three reviews, as the API sends no average', () => {
    renderCard(business({ rating: { count: 2, average: null } }));

    expect(screen.getByRole('link')).not.toHaveTextContent('reseña');
    expect(screen.getByRole('link')).not.toHaveTextContent('(2)');
  });

  it('says how soon it answers the requests made online, and nothing without it (Fase 29J)', () => {
    renderCard(business({ name: 'Plomería Hernández', receivesVisits: true, responseTime: ResponseTime.WithinThreeHours }));
    expect(screen.getByRole('link')).toHaveTextContent('Suele responder en menos de 3 horas');
  });

  it('says how soon a trade sends the quote after the visit, and never for a business without visits (Fase 29S)', () => {
    renderCard(business({ name: 'Plomería Hernández', receivesVisits: true, quoteTime: QuoteTime.WithinThreeDays }));
    renderCard(business({ name: 'Estética Luna', booksOnline: true, receivesVisits: false, quoteTime: QuoteTime.WithinADay }));

    const [trade, salon] = screen.getAllByRole('link');
    expect(trade).toHaveTextContent('Suele mandar la cotización en menos de 3 días');
    expect(salon).not.toHaveTextContent('cotización');
  });

  it('says how soon a business that takes online orders confirms them, paused too, and never for one without orders (Fase 29T)', () => {
    renderCard(business({ name: 'Tacos El Güero', acceptsOrders: true, orderConfirmTime: OrderConfirmTime.WithinFiveMinutes }));
    renderCard(business({ name: 'Pizzería Napoli', acceptsOrders: false, ordersPaused: true, orderConfirmTime: OrderConfirmTime.WithinHalfAnHour }));
    renderCard(business({ name: 'Café Luna', acceptsOrders: false, ordersPaused: false, orderConfirmTime: OrderConfirmTime.WithinFiveMinutes }));

    const [taking, paused, menuOnly] = screen.getAllByRole('link');
    expect(taking).toHaveTextContent('Suele confirmar los pedidos en menos de 5 minutos');
    expect(paused).toHaveTextContent('Suele confirmar los pedidos en menos de media hora');
    expect(menuOnly).not.toHaveTextContent('Suele confirmar');
  });

  it('says nothing of how soon it answers when the server sends none (Fase 29J)', () => {
    renderCard(business({ booksOnline: true, responseTime: undefined }));
    expect(screen.getByRole('link')).not.toHaveTextContent('Suele responder');
  });

  it('shows a trade as what it is: visits to quote and what one costs at the postal code, never a menu (Fase 29A)', () => {
    renderCard(business({
      name: 'Plomería Hernández',
      category: BusinessCategory.HomeRepairs,
      deliveryOnly: true,
      receivesVisits: true,
      ordersPaused: true,
      visitChargePolicy: HomeVisitChargePolicy.CreditedIfHired,
      visitPrice: { amount: 350, isStarting: false },
      visitsPostalCode: true,
      statusLabel: 'Abierto',
      statusDetail: 'cierra a las 19:00',
    }));

    const link = screen.getByRole('link');
    expect(link).toHaveTextContent('Hogar y reparaciones · Solo a domicilio');
    expect(link).toHaveTextContent('Visitas a domicilio');
    expect(link).toHaveTextContent('Va a tu código postal · Visita $350.00, se descuenta si contratas');
    expect(link).toHaveTextContent('Abierto · cierra a las 19:00');
    expect(link).not.toHaveTextContent('Solo menú');
    expect(link).not.toHaveTextContent('en pausa');
  });
});
