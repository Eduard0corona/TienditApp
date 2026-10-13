import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import { DeliveryCourier, OrderStatus, OrderType, type OrderDto } from '../../types';
import OrderDeliveryPanel from './OrderDeliveryPanel';

vi.mock('../../api/ordersApi', () => ({
  ordersApi: { registerCourierShipment: vi.fn() },
}));

const api = vi.mocked(ordersApi);

const order = (delivery: Partial<NonNullable<OrderDto['delivery']>> = {}, status: OrderStatus = OrderStatus.Confirmed): OrderDto => ({
  id: 'o1',
  createdByBusiness: false,
  orderNumber: 'ORD-000042',
  businessType: 0 as OrderDto['businessType'],
  orderType: OrderType.Retail,
  status,
  canCancel: true,
  allowedTransitions: [],
  customerName: 'Ana',
  pricesIncludeTax: true,
  subTotal: 237.07,
  tax: 37.93,
  total: 275,
  taxBreakdown: [],
  paidAmount: 0,
  balance: 275,
  createdAt: '2026-10-13T19:00:00Z',
  items: [],
  statusHistory: [],
  servicePieces: [],
  payments: [],
  delivery: {
    postalCode: '72000',
    addressLine: 'Av. Reforma 123, Centro',
    references: 'Portón verde',
    zoneName: 'Centro',
    fee: 35,
    feeTaxRate: 16,
    ...delivery,
  },
});

describe('OrderDeliveryPanel', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows where the order goes and what it charged, and writes down the trip of Paquetenvia', async () => {
    api.registerCourierShipment.mockResolvedValue({} as never);
    const onSaved = vi.fn();
    render(<OrderDeliveryPanel order={order()} canManage onSaved={onSaved} />);

    expect(screen.getByText('Av. Reforma 123, Centro')).toBeInTheDocument();
    expect(screen.getByText('Referencias: Portón verde')).toBeInTheDocument();
    expect(screen.getByText(/CP 72000 · zona Centro · envío/)).toHaveTextContent('$35.00');

    await userEvent.click(screen.getByRole('button', { name: 'Registrar envío de Paquetenvia' }));
    await userEvent.type(screen.getByLabelText('Folio de Paquetenvia'), ' pq 1234 ');
    await userEvent.type(screen.getByLabelText('Enlace para seguirlo (opcional)'), 'https://paquetenvia.mx/r/PQ1234');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar envío' }));

    expect(api.registerCourierShipment).toHaveBeenCalledWith('o1', {
      courier: DeliveryCourier.Paquetenvia,
      folio: 'pq 1234',
      trackingUrl: 'https://paquetenvia.mx/r/PQ1234',
    });
    expect(onSaved).toHaveBeenCalled();
    expect(screen.queryByLabelText('Folio de Paquetenvia')).not.toBeInTheDocument();
  });

  it('refuses an insecure link before sending it, and shows what the server answers', async () => {
    api.registerCourierShipment.mockRejectedValue({
      isAxiosError: true,
      response: { status: 400, data: { detail: 'El folio no es válido.' } },
    });
    render(<OrderDeliveryPanel order={order()} canManage onSaved={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: 'Registrar envío de Paquetenvia' }));
    await userEvent.type(screen.getByLabelText('Folio de Paquetenvia'), 'PQ1234');
    const link = screen.getByLabelText('Enlace para seguirlo (opcional)');
    await userEvent.type(link, 'http://paquetenvia.mx/r/PQ1234');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar envío' }));
    expect(screen.getByRole('alert')).toHaveTextContent('El enlace del envío debe empezar con https://.');
    expect(api.registerCourierShipment).not.toHaveBeenCalled();

    await userEvent.clear(link);
    await userEvent.click(screen.getByRole('button', { name: 'Guardar envío' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('El folio no es válido.');
  });

  it('shows the trip with its link, and lets only whoever takes orders correct it while the order stands', () => {
    const shipped = order({
      courier: DeliveryCourier.Paquetenvia, courierFolio: 'PQ1234', trackingUrl: 'https://paquetenvia.mx/r/PQ1234', fee: 0,
    });
    const { rerender } = render(<OrderDeliveryPanel order={shipped} canManage onSaved={vi.fn()} />);

    expect(screen.getByText('Paquetenvia:').parentElement).toHaveTextContent('Paquetenvia: folio PQ1234');
    expect(screen.getByRole('link', { name: 'Seguir envío' })).toHaveAttribute('href', 'https://paquetenvia.mx/r/PQ1234');
    expect(screen.getByText(/CP 72000/)).toHaveTextContent('envío Gratis');
    expect(screen.getByRole('button', { name: 'Corregir envío de Paquetenvia' })).toBeInTheDocument();

    rerender(<OrderDeliveryPanel order={shipped} canManage={false} onSaved={vi.fn()} />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    rerender(<OrderDeliveryPanel order={{ ...shipped, status: OrderStatus.Cancelled }} canManage onSaved={vi.fn()} />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();

    // A link that is not https is never offered.
    rerender(<OrderDeliveryPanel order={order({ ...shipped.delivery, trackingUrl: 'javascript:alert(1)' })} canManage onSaved={vi.fn()} />);
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
});
