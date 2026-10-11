import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { CashSessionDto, CashTransactionDto } from '../../types';
import { CashSessionStatus, CashTransactionType } from '../../types';
import { formatInBusinessZone } from '../../utils/dates';
import CashCutTicket from './CashCutTicket';

const session = (patch: Partial<CashSessionDto> = {}): CashSessionDto => ({
  id: 's1',
  openedAt: '2026-10-04T13:00:00Z',
  openedBy: 'mariana.ortiz@cafelaesquina.mx',
  openedByName: 'Mariana Ortiz',
  openingAmount: 1500,
  status: CashSessionStatus.Closed,
  closedAt: '2026-10-04T19:25:00Z',
  closedBy: 'carlos.mendez@cafelaesquina.mx',
  closedByName: 'Carlos Méndez',
  closingAmount: 1800,
  inflows: 2582.24,
  outflows: 2275,
  nonCashInflows: 1397.8,
  expectedBalance: 1807.24,
  expectedClosingAmount: 1807.24,
  closingDifference: -7.24,
  isMine: true,
  branchName: 'Roma',
  cashCount: [],
  ...patch,
});

describe('CashCutTicket', () => {
  it('prints who opened and who closed by name on their own lines, apart from the dates', () => {
    render(<CashCutTicket businessName="Café La Esquina" session={session()} movements={[]} />);

    // The labels stay short, so the dates keep their column.
    expect(screen.getByText('Abrió').closest('.ta-ticket-row')).not.toBeNull();
    expect(screen.getByText('Cerró').closest('.ta-ticket-row')).not.toBeNull();
    for (const name of ['Mariana Ortiz', 'Carlos Méndez']) {
      const line = screen.getByText(name);
      expect(line).toHaveClass('ta-ticket-wrap');
      expect(line.closest('.ta-ticket-row')).toBeNull();
    }
    // The name instead of the email, never both.
    expect(screen.queryByText(/@/)).toBeNull();
  });

  it('prints the user of whoever has no name, as before', () => {
    render(<CashCutTicket session={session({ openedByName: null, closedByName: '  ' })} movements={[]} />);

    for (const user of ['mariana.ortiz@cafelaesquina.mx', 'carlos.mendez@cafelaesquina.mx'])
      expect(screen.getByText(user)).toHaveClass('ta-ticket-wrap');
    expect(screen.queryByText(/Mariana Ortiz|Carlos Méndez/)).toBeNull();
  });

  it('heads with the branch and shows each person once, as many people as before', () => {
    render(<CashCutTicket session={session({ closedBy: 'mariana.ortiz@cafelaesquina.mx', closedByName: 'Mariana Ortiz' })} movements={[]} />);

    expect(screen.getByText('Roma')).toHaveClass('text-center');
    // Opened and closed by the same person: once under each date, never in the header.
    const lines = screen.getAllByText('Mariana Ortiz');
    expect(lines).toHaveLength(2);
    lines.forEach((line) => expect(line).not.toHaveClass('text-center'));
    expect(screen.queryByText(/Carlos|carlos/)).toBeNull();
  });

  it('prints only who opened a drawer that is still open', () => {
    render(<CashCutTicket
      session={session({ status: CashSessionStatus.Open, closedAt: undefined, closedBy: undefined, closedByName: undefined })}
      movements={[]}
    />);

    expect(screen.getByText('Corte parcial (caja abierta)')).toBeInTheDocument();
    expect(screen.getByText('Mariana Ortiz')).toHaveClass('ta-ticket-wrap');
    expect(screen.queryByText('Cerró')).toBeNull();
  });

  it('keeps an amount with its sign and a time in one piece on a narrow roll', () => {
    const movements: CashTransactionDto[] = [
      { id: 'm1', cashSessionId: 's1', transactionType: CashTransactionType.Expense, amount: -180, occurredAt: '2026-10-04T16:30:00Z' },
    ];
    render(<CashCutTicket session={session()} movements={movements} />);

    expect(screen.getByText('−$180.00')).toHaveClass('text-nowrap');
    expect(screen.getByText('$1,500.00')).toHaveClass('text-nowrap');
    // The date may go on its own line; the time with its a.m./p.m. stays together.
    const closedRow = screen.getByText('Cerró').closest('.ta-ticket-row')!;
    expect(closedRow).toHaveTextContent(formatInBusinessZone('2026-10-04T19:25:00Z', { dateStyle: 'short', timeStyle: 'short' }));
    expect(within(closedRow as HTMLElement).getByText(formatInBusinessZone('2026-10-04T19:25:00Z', { timeStyle: 'short' })))
      .toHaveClass('text-nowrap');
  });

  it('lets the note break inside the ticket', () => {
    render(<CashCutTicket session={session({ closingNotes: 'Faltaron monedas de cambio' })} movements={[]} />);

    expect(screen.getByText('Nota: Faltaron monedas de cambio')).toHaveClass('ta-ticket-wrap');
  });
});
