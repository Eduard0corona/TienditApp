import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useOrderSelfInvoice } from '../../api/invoicing';
import { useToast } from '../../hooks/useToast';
import {
  BusinessType,
  OrderChannel,
  OrderPaymentMethod,
  OrderType,
  PlatformCollectedBy,
  UnitType,
  type OrderDto,
} from '../../types';
import CompletedSaleModal from './CompletedSaleModal';
import type { CompletedSale } from './sellModel';

vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../api/chatApi', () => ({ chatApi: { sendMessage: vi.fn() } }));
vi.mock('../../api/invoicing', () => ({ useOrderSelfInvoice: vi.fn() }));

const counterSale: OrderDto = {
  id: 'o1',
  createdByBusiness: true,
  orderNumber: 'ORD-000123',
  businessType: BusinessType.Food,
  orderType: OrderType.Retail,
  status: 6 as OrderDto['status'],
  canCancel: false,
  allowedTransitions: [],
  customerName: 'Público en general',
  pricesIncludeTax: true,
  subTotal: 301.72,
  tax: 48.28,
  total: 350,
  taxBreakdown: [],
  channel: OrderChannel.Counter,
  paidAmount: 350,
  balance: 0,
  createdAt: '2026-10-10T18:00:00Z',
  items: [{
    id: 'i1', productId: 'p1', productName: 'Paquete familiar', quantity: 1, unitPrice: 350, totalPrice: 350,
    taxRate: 16, isTaxExempt: false, unit: UnitType.Unit,
  }],
  statusHistory: [],
  servicePieces: [],
  payments: [{ id: 'p1', amount: 350, method: OrderPaymentMethod.Cash, paidAt: '2026-10-10T18:00:00Z', cashReceived: 350, change: 0 }],
};

function platformSale(channel: OrderChannel, folio: string, collectedBy: PlatformCollectedBy, balance: number): CompletedSale {
  return {
    order: {
      ...counterSale,
      orderNumber: 'ORD-000124',
      channel,
      customerName: 'Juan',
      platformOrder: { externalOrderNumber: folio, collectedBy },
      payments: collectedBy === PlatformCollectedBy.Platform
        ? [{ id: 'p1', amount: 350, method: OrderPaymentMethod.Platform, paidAt: '2026-10-10T18:00:00Z', reference: folio }]
        : balance > 0 ? [] : counterSale.payments,
      paidAmount: 350 - balance,
      balance,
    },
  };
}

function renderModal(sale: CompletedSale, canCharge = true) {
  return render(
    <MemoryRouter>
      <CompletedSaleModal
        sale={sale}
        businessName="Cocina Lupita"
        chatOrigin={null}
        canChat
        canCharge={canCharge}
        hasKitchen
        onNewSale={vi.fn()}
      />
    </MemoryRouter>,
  );
}

describe('CompletedSaleModal', () => {
  beforeEach(() => {
    vi.mocked(useToast).mockReturnValue({ success: vi.fn(), error: vi.fn() } as unknown as ReturnType<typeof useToast>);
    vi.mocked(useOrderSelfInvoice).mockReturnValue({ selfInvoice: null, preparing: false });
  });

  it('prints «Factura en línea» while the business has self-invoicing on, never on a sale recorded without connection', () => {
    vi.mocked(useOrderSelfInvoice).mockReturnValue({
      selfInvoice: { path: '/m/cocina-lupita/facturar', orderNumber: 'ORD-000123', code: 'ABCDE-12345', invoiceBy: '2026-10-31' },
      preparing: false,
    });
    const { unmount } = renderModal({ order: counterSale });

    const ticket = screen.getByLabelText('Ticket ORD-000123');
    expect(within(ticket).getByText('Factura en línea')).toBeInTheDocument();
    expect(within(ticket).getByText('ABCDE-12345')).toBeInTheDocument();
    expect(within(ticket).getByText(/\/m\/cocina-lupita\/facturar$/)).toBeInTheDocument();
    expect(within(ticket).getByText('Hasta el 31 de octubre de 2026')).toBeInTheDocument();
    expect(vi.mocked(useOrderSelfInvoice)).toHaveBeenLastCalledWith(counterSale, true);
    unmount();

    renderModal({ order: counterSale, offline: true });
    expect(vi.mocked(useOrderSelfInvoice)).toHaveBeenLastCalledWith(counterSale, false);
    expect(screen.queryByText('Factura en línea')).not.toBeInTheDocument();
  });

  it('waits for «Factura en línea» before printing, so the paper does not go without it', () => {
    vi.mocked(useOrderSelfInvoice).mockReturnValue({ selfInvoice: null, preparing: true });
    renderModal({ order: counterSale });

    expect(screen.getByRole('button', { name: 'Preparando el ticket…' })).toBeDisabled();
  });

  it('ends a counter sale as always: charged, printed or sent by WhatsApp', () => {
    renderModal({ order: counterSale });

    const dialog = screen.getByRole('dialog', { name: 'Venta ORD-000123 cobrada' });
    expect(within(dialog).getByRole('button', { name: 'Imprimir' })).toBeInTheDocument();
    expect(within(dialog).getByRole('link', { name: 'Enviar por WhatsApp' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Nueva venta' })).toHaveFocus();
  });

  it('prints the bag of an order Rappi charged, with no prices and nothing to send', () => {
    renderModal(platformSale(OrderChannel.Rappi, 'BC953', PlatformCollectedBy.Platform, 0));

    const dialog = screen.getByRole('dialog', { name: 'Pedido ORD-000124 de Rappi registrado' });
    expect(within(dialog).getByRole('button', { name: 'Imprimir para la bolsa' })).toBeInTheDocument();
    // There is no phone to send it to.
    expect(within(dialog).queryByRole('link', { name: 'Enviar por WhatsApp' })).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Nueva venta' })).toHaveFocus();
    const ticket = within(dialog).getByLabelText('Ticket ORD-000124');
    expect(ticket).toHaveTextContent('Rappi · Folio BC953');
    expect(ticket).toHaveTextContent('Bolsa: Juan');
    expect(ticket).toHaveTextContent('Pagado en la app de Rappi');
    expect(ticket).not.toHaveTextContent('$350.00');
    expect(within(dialog).queryByText('Cóbralo en Pedidos cuando el repartidor te pague.')).not.toBeInTheDocument();
  });

  it('reminds to charge the courier an order of DiDi Food registered without charging', () => {
    renderModal(platformSale(OrderChannel.DidiFood, '12', PlatformCollectedBy.Business, 350));

    const dialog = screen.getByRole('dialog', { name: 'Pedido ORD-000124 de DiDi Food registrado' });
    expect(within(dialog).getByText('Cóbralo en Pedidos cuando el repartidor te pague.')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Ticket ORD-000124')).toHaveTextContent('Cobrar al repartidor$350.00');
  });

  it('leaves charging the courier to the cashier when whoever registered it cannot charge', () => {
    renderModal(platformSale(OrderChannel.DidiFood, '12', PlatformCollectedBy.Business, 350), false);

    const dialog = screen.getByRole('dialog', { name: 'Pedido ORD-000124 de DiDi Food registrado' });
    expect(within(dialog).getByText('La caja lo cobra desde Pedidos cuando el repartidor pague.')).toBeInTheDocument();
    expect(within(dialog).queryByText('Cóbralo en Pedidos cuando el repartidor te pague.')).not.toBeInTheDocument();
  });

  it('says an order of DiDi Food the courier already paid was charged', () => {
    renderModal(platformSale(OrderChannel.DidiFood, '12', PlatformCollectedBy.Business, 0));

    const dialog = screen.getByRole('dialog', { name: 'Pedido ORD-000124 de DiDi Food cobrado' });
    expect(within(dialog).queryByText('Cóbralo en Pedidos cuando el repartidor te pague.')).not.toBeInTheDocument();
    expect(within(dialog).getByLabelText('Ticket ORD-000124')).toHaveTextContent('DiDi Food · Folio 12');
    expect(within(dialog).getByLabelText('Ticket ORD-000124')).toHaveTextContent('Efectivo$350.00');
  });
});
