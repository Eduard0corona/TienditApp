import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { cashApi } from '../../api/cashApi';
import { useTenant } from '../../hooks/useTenant';
import type { DailyCashCutDto, TenantProfileDto } from '../../types';
import DailyCut from './DailyCut';

vi.mock('../../api/cashApi', () => ({ cashApi: { getDailyCut: vi.fn() } }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));

const plain = (text: string | null | undefined) => (text ?? '').replace(/\s+/g, ' ').trim();
/** What an element shows, a space between each of its parts (the label, the amount and the lines under them). */
const lines = (element: Element) => [...element.children].map((child) => plain(child.textContent)).join(' ');

/** The day of a café that also took Rappi orders: $350.00 Rappi charged in its app, part of "Cobrado". */
function cut(patch: Partial<DailyCashCutDto> = {}): DailyCashCutDto {
  return {
    date: '2026-10-05',
    salesPaymentsTotal: 1600,
    salesPaymentsCount: 13,
    cashTotal: 800,
    cardTotal: 400,
    transferTotal: 50,
    walletTotal: 0,
    otherTotal: 0,
    platformTotal: 350,
    platformPaymentsCount: 1,
    platformRefundsTotal: 0,
    manualIncomeTotal: 0,
    manualExpenseTotal: 20,
    cashManualIncomeTotal: 0,
    cashManualExpenseTotal: 20,
    netCashFlow: 1230,
    netCashOnlyFlow: 780,
    tipsTotal: 0,
    cashTipsTotal: 0,
    byUser: [{ userName: 'ana@cafelaesquina.mx', paymentsCount: 12, totalAmount: 1250 }],
    byLocation: [],
    byBranch: [],
    ...patch,
  };
}

function serve(day: DailyCashCutDto) {
  vi.mocked(cashApi.getDailyCut).mockResolvedValue({ data: day } as Awaited<ReturnType<typeof cashApi.getDailyCut>>);
}

/** The figure of the cut with that label: its amount and the lines under it. */
async function figure(label: string) {
  const title = await screen.findByText(label, { selector: '.text-muted.small' });
  return title.parentElement as HTMLElement;
}

describe('DailyCut', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useTenant).mockReturnValue({ tenant: { id: 't1', name: 'Café La Esquina' } as TenantProfileDto } as ReturnType<typeof useTenant>);
  });

  it('shows what the platforms charged apart, outside the drawers', async () => {
    serve(cut());
    render(<DailyCut branches={[]} />);

    const platforms = await figure('Plataformas');
    expect(lines(platforms)).toBe('Plataformas $350.00 Lo cobró la plataforma: no entra a la caja');
    expect(within(platforms).queryByText(/Devuelto/)).not.toBeInTheDocument();
    // "Cobrado" includes it; the cash in the drawers does not.
    expect(lines(await figure('Cobrado'))).toBe('Cobrado $1,600.00 13 pagos');
    expect(lines(await figure('Neto en efectivo'))).toContain('$780.00');
  });

  it('says what went back as «Plataforma» under it', async () => {
    serve(cut({ platformRefundsTotal: 350 }));
    render(<DailyCut branches={[]} />);

    expect(lines(await figure('Plataformas'))).toBe('Plataformas $350.00 Lo cobró la plataforma: no entra a la caja Devuelto: $350.00');
  });

  it('shows the platforms when they only gave something back that day', async () => {
    serve(cut({ salesPaymentsTotal: 1250, salesPaymentsCount: 12, platformTotal: 0, platformPaymentsCount: 0, platformRefundsTotal: 350 }));
    render(<DailyCut branches={[]} />);

    expect(lines(await figure('Plataformas'))).toBe('Plataformas $0.00 Lo cobró la plataforma: no entra a la caja Devuelto: $350.00');
  });

  it('leaves the platforms out of a day without them', async () => {
    serve(cut({ salesPaymentsTotal: 1250, salesPaymentsCount: 12, platformTotal: 0, platformPaymentsCount: 0 }));
    render(<DailyCut branches={[]} />);

    await figure('Cobrado');
    expect(screen.queryByText('Plataformas')).not.toBeInTheDocument();
    expect(screen.queryByText('Lo cobró la plataforma: no entra a la caja')).not.toBeInTheDocument();
  });

  it('prints the platform rows of the cut, so the methods add up to what was charged', async () => {
    vi.spyOn(window, 'print').mockImplementation(() => undefined);
    serve(cut({ platformRefundsTotal: 120 }));
    render(<DailyCut branches={[]} />);

    await userEvent.click(await screen.findByRole('button', { name: 'Imprimir corte del día' }));

    const printed = await waitFor(() => {
      const area = document.querySelector('.ta-print-area');
      if (!area) throw new Error('Sin corte impreso');
      return area as HTMLElement;
    });
    const rows = [...printed.querySelectorAll('.ta-ticket-row')].map(lines);
    expect(rows.slice(0, 8)).toEqual([
      'Cobrado $1,600.00',
      'Efectivo $800.00',
      'Tarjeta $400.00',
      'Transferencia $50.00',
      'Otros $0.00',
      'Cobrado por plataformas $350.00',
      'Devuelto por plataformas $120.00',
      'Neto en efectivo $780.00',
    ]);
  });
});
