import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import { DeliveryCourier, OrderStatus, OrderType, type OrderDto } from '../../types';
import OrderDeliveryPanel from './OrderDeliveryPanel';

vi.mock('../../api/ordersApi', () => ({
  ordersApi: { registerCourierShipment: vi.fn(), dispatch: vi.fn() },
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
    awaitsCourierCash: false,
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
    expect(onSaved).toHaveBeenCalledWith('shipment');
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

    // A link that is not written with https:// is never offered.
    rerender(<OrderDeliveryPanel order={order({ ...shipped.delivery, trackingUrl: 'javascript:alert(1)' })} canManage onSaved={vi.fn()} />);
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    rerender(<OrderDeliveryPanel order={order({ ...shipped.delivery, trackingUrl: 'https:\\\\paquetenvia.mx\\r' })} canManage onSaved={vi.fn()} />);
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('sends the order out with what Paquetenvia collects and the bill the customer pays with, for the change', async () => {
    api.dispatch.mockResolvedValue({} as never);
    const onSaved = vi.fn();
    render(<OrderDeliveryPanel order={order({ courier: DeliveryCourier.Paquetenvia, courierFolio: 'PQ1234', paysWith: 500 })} canManage onSaved={onSaved} />);

    // Before it leaves: the bill the customer said they pay with, and its change.
    expect(screen.getByText(/Paga con/)).toHaveTextContent('Paga con $500.00 · cambio $225.00');
    await userEvent.click(screen.getByRole('button', { name: 'Salió a entregar' }));
    expect(screen.getByLabelText('Folio de Paquetenvia')).toHaveValue('PQ1234');
    expect(screen.getByLabelText(/Paquetenvia cobra en efectivo/)).toBeChecked();
    expect(screen.getByLabelText(/Paquetenvia cobra en efectivo/).parentElement).toHaveTextContent('$275.00');
    const bill = screen.getByLabelText('Paga con (opcional)');
    expect(bill).toHaveValue('500');
    expect(screen.getByText(/Cambio:/)).toHaveTextContent('$225.00');

    // A bill that does not cover it is refused before sending it.
    await userEvent.clear(bill);
    await userEvent.type(bill, '200');
    expect(screen.getByText('Con $200.00 no alcanza: hay que cobrar $275.00.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Salió a entregar' }));
    expect(api.dispatch).not.toHaveBeenCalled();

    await userEvent.clear(bill);
    await userEvent.type(bill, '1,000');
    await userEvent.type(screen.getByLabelText('Enlace para seguirlo (opcional)'), 'https://paquetenvia.mx/r/PQ1234');
    await userEvent.click(screen.getByRole('button', { name: 'Salió a entregar' }));

    expect(api.dispatch).toHaveBeenCalledWith('o1', {
      courier: DeliveryCourier.Paquetenvia,
      folio: 'PQ1234',
      trackingUrl: 'https://paquetenvia.mx/r/PQ1234',
      collectsCash: true,
      paysWith: 1000,
    });
    expect(onSaved).toHaveBeenCalledWith('dispatch');
  });

  it('sends a paid order out collecting nothing', async () => {
    api.dispatch.mockResolvedValue({} as never);
    render(<OrderDeliveryPanel order={{ ...order(), paidAmount: 275, balance: 0 }} canManage onSaved={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: 'Salió a entregar' }));
    const cash = screen.getByLabelText(/Paquetenvia cobra en efectivo/);
    expect(cash).not.toBeChecked();
    expect(cash).toBeDisabled();
    expect(screen.getByText('El pedido ya está pagado: el repartidor no cobra nada.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Paga con (opcional)')).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Folio de Paquetenvia'), 'PQ9');
    await userEvent.click(screen.getByRole('button', { name: 'Salió a entregar' }));

    expect(api.dispatch).toHaveBeenCalledWith('o1', expect.objectContaining({ folio: 'PQ9', collectsCash: false, paysWith: undefined }));
  });

  it('shows the order on its way with what the courier collects, and corrects it', async () => {
    api.dispatch.mockRejectedValue({ isAxiosError: true, response: { status: 400, data: { detail: 'El pedido ya se entregó.' } } });
    const onTheWay = order({
      courier: DeliveryCourier.Paquetenvia, courierFolio: 'PQ1234', paysWith: 500,
      dispatchedAt: '2026-10-13T19:05:00Z', dispatchedBy: 'cajero', cashToCollect: 275,
    });
    render(<OrderDeliveryPanel order={onTheWay} canManage onSaved={vi.fn()} />);

    expect(screen.getByText('En camino')).toBeInTheDocument();
    expect(screen.getByText(/Salió a las/)).toHaveTextContent('(cajero)');
    expect(screen.getByText(/Cobra \$275.00/)).toHaveTextContent('Cobra $275.00 · paga con $500.00 · cambio $225.00');
    // On its way, the bill shows with what it collects; the trip is corrected with the way out.
    expect(screen.queryByText(/^Paga con/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /envío de Paquetenvia/ })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Corregir salida' }));
    expect(screen.getByLabelText(/Paquetenvia cobra en efectivo/)).toBeChecked();
    await userEvent.click(screen.getByRole('button', { name: 'Guardar corrección' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('El pedido ya se entregó.');
  });

  it('tells what Paquetenvia owes once delivered, and offers no way out for a delivered or pending order', () => {
    const delivered = order({
      courier: DeliveryCourier.Paquetenvia, courierFolio: 'PQ1234',
      dispatchedAt: '2026-10-13T19:05:00Z', cashToCollect: 275, awaitsCourierCash: true,
    }, OrderStatus.Delivered);
    const { rerender } = render(<OrderDeliveryPanel order={delivered} canManage onSaved={vi.fn()} />);

    expect(screen.queryByText('En camino')).not.toBeInTheDocument();
    expect(screen.getByText(/Paquetenvia te debe/)).toHaveTextContent('Paquetenvia te debe $275.00 de este pedido: se liquida en Caja › Repartidores.');
    expect(screen.queryByRole('button', { name: /salida|Salió/ })).not.toBeInTheDocument();
    // The folio can still be corrected, as before.
    expect(screen.getByRole('button', { name: 'Corregir envío de Paquetenvia' })).toBeInTheDocument();

    rerender(<OrderDeliveryPanel order={order({}, OrderStatus.Pending)} canManage onSaved={vi.fn()} />);
    expect(screen.getByText('Confirma el pedido antes de mandarlo con el repartidor.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Salió a entregar' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Registrar envío de Paquetenvia' })).toBeInTheDocument();
  });
});
