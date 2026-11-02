import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { customersApi } from '../../api/customersApi';
import { ordersApi } from '../../api/ordersApi';
import { productsApi } from '../../api/productsApi';
import { useTenant } from '../../hooks/useTenant';
import { UnitType, type CustomerDto, type ProductDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import OrdersCart from './OrdersCart';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => ({ success: vi.fn(), warning: vi.fn(), error: vi.fn() }) }));
vi.mock('../../api/productsApi', () => ({ productsApi: { getAll: vi.fn() } }));
vi.mock('../../api/customersApi', () => ({ customersApi: { getAll: vi.fn() } }));
vi.mock('../../api/ordersApi', () => ({ ordersApi: { create: vi.fn(), uploadServicePhoto: vi.fn() } }));

const repair = {
  id: 's1', name: 'Compostura de calzado', description: 'Suela y tacón', priceAmount: 250, unit: UnitType.Service,
} as unknown as ProductDto;
const publicGeneral = { id: 'c1', name: 'Público en general', isPublicGeneral: true, phone: null } as unknown as CustomerDto;

const page = <T,>(items: T[]) => ({ data: { items, totalCount: items.length } }) as never;

function renderCart() {
  vi.mocked(useTenant).mockReturnValue({
    tenant: { name: 'Zapatería Baja', pricesIncludeTax: true, defaultTaxRate: 16 },
  } as unknown as ReturnType<typeof useTenant>);
  vi.mocked(productsApi.getAll).mockImplementation(((params: { itemType?: string }) =>
    Promise.resolve(page(params.itemType === 'Service' ? [repair] : []))) as unknown as typeof productsApi.getAll);
  vi.mocked(customersApi.getAll).mockResolvedValue(page([publicGeneral]));
  vi.mocked(ordersApi.create).mockResolvedValue({ data: { orderNumber: 'ORD-000010', stockWarnings: [] } } as never);
  return render(
    <MemoryRouter initialEntries={['/orders/cart']}>
      <OrdersCart />
    </MemoryRouter>,
  );
}

async function addTheRepair() {
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Servicio' }));
  await user.click(await screen.findByRole('button', { name: /Compostura de calzado/ }));
  return user;
}

describe('OrdersCart service dates', () => {
  // The business is in Baja California (UTC-7 in October); the device is on Mexico City time (UTC-6).
  beforeEach(() => {
    vi.stubEnv('TZ', 'America/Mexico_City');
    setBusinessTimeZone('America/Tijuana');
  });

  afterEach(() => {
    setBusinessTimeZone(null);
    vi.unstubAllEnvs();
    vi.useRealTimers();
  });

  it('sends the received and promised dates as the clock of the business, not the device', async () => {
    // The device really reads the input in its own zone: 10:30 there would be 16:30 UTC.
    expect(new Date('2026-10-08T10:30').toISOString()).toBe('2026-10-08T16:30:00.000Z');
    renderCart();
    const user = await addTheRepair();

    fireEvent.change(screen.getByLabelText('Fecha de recepción'), { target: { value: '2026-10-08T09:00' } });
    fireEvent.change(screen.getByLabelText('Fecha promesa'), { target: { value: '2026-10-08T10:30' } });
    await user.click(screen.getByRole('button', { name: 'Registrar orden' }));

    expect(ordersApi.create).toHaveBeenCalledTimes(1);
    const { serviceDetail } = vi.mocked(ordersApi.create).mock.calls[0][0];
    expect(serviceDetail?.receivedAt).toBe('2026-10-08T16:00:00.000Z');
    expect(serviceDetail?.promisedAt).toBe('2026-10-08T17:30:00.000Z');
  });

  it('keeps the received date at the instant it started with in the hour the clock repeats', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    // 09:30 UTC on 1 November is 01:30 PST in Tijuana, the second time 01:30 comes that night.
    vi.setSystemTime(new Date('2026-11-01T09:30:00.000Z'));
    renderCart();
    const user = await addTheRepair();

    expect(screen.getByLabelText('Fecha de recepción')).toHaveValue('2026-11-01T01:30');
    await user.click(screen.getByRole('button', { name: 'Registrar orden' }));

    expect(vi.mocked(ordersApi.create).mock.calls[0][0].serviceDetail?.receivedAt).toBe('2026-11-01T09:30:00.000Z');
  });

  it('starts the received date at the current hour of the business', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-08T17:30:00.000Z'));
    renderCart();
    const user = await addTheRepair();

    // 17:30 UTC is 10:30 in Tijuana (11:30 on the device): the field shows the business's hour and sends it back as is.
    expect(screen.getByLabelText('Fecha de recepción')).toHaveValue('2026-10-08T10:30');
    expect(screen.getByLabelText('Fecha promesa')).toHaveValue('');
    await user.click(screen.getByRole('button', { name: 'Registrar orden' }));

    const { serviceDetail } = vi.mocked(ordersApi.create).mock.calls[0][0];
    expect(serviceDetail?.receivedAt).toBe('2026-10-08T17:30:00.000Z');
    expect(serviceDetail?.promisedAt).toBeUndefined();
  });
});
