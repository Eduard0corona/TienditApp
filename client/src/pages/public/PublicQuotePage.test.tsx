import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { QuoteStatus } from '../../types';
import PublicQuotePage from './PublicQuotePage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';
const NEWER = 'ZyXwVuTsRqPoNmLkJiHgFeDcBa9876543210_-ZyXwV';

function quote(overrides: Record<string, unknown> = {}) {
  return {
    businessName: 'Plomería Ruiz',
    businessSlug: 'plomeria-ruiz',
    businessContactPhone: '5512345678',
    number: 'COT-000007',
    revision: 1,
    status: QuoteStatus.Sent,
    customerName: 'Valeria Ruiz',
    sentAt: '2026-10-04T17:00:00Z',
    validUntil: '2026-10-19',
    pricesIncludeTax: true,
    lines: [{ description: 'Cambio de calentador', quantity: 1, unitPrice: 1160, total: 1160 }],
    subTotal: 1000,
    tax: 160,
    taxes: [],
    total: 1160,
    toPay: 810,
    visitCredit: { amount: 350, applies: true },
    note: 'Incluye material',
    newerToken: null,
    ...overrides,
  };
}

function renderPage(url: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route path="/cotizacion" element={<PublicQuotePage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PublicQuotePage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('shows the quote of the link, with the visit taken off and a way to answer', async () => {
    vi.mocked(apiRequest).mockResolvedValue(quote() as never);
    renderPage(`/cotizacion#${LINK}`);

    expect(await screen.findByRole('heading', { name: 'Cotización COT-000007' })).toBeInTheDocument();
    expect(apiRequest).toHaveBeenCalledWith('post', '/api/v1/public/quotes/view', expect.objectContaining({ body: { token: LINK } }));
    expect(screen.getByText('Plomería Ruiz · para Valeria Ruiz')).toBeInTheDocument();
    expect(screen.getByText(/Vale hasta el 19 de octubre de 2026/)).toBeInTheDocument();
    expect(screen.getByText('−$350.00')).toBeInTheDocument();
    expect(screen.getByText('$810.00')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Responder al negocio' }).getAttribute('href')).toContain('https://wa.me/525512345678');
  });

  it('leads from a replaced version to the new one', async () => {
    vi.mocked(apiRequest).mockResolvedValueOnce(quote({ status: QuoteStatus.Superseded, newerToken: NEWER }) as never)
      .mockResolvedValueOnce(quote({ revision: 2, total: 990, toPay: 990, visitCredit: null }) as never);
    renderPage(`/cotizacion#${LINK}`);

    await userEvent.click(await screen.findByRole('link', { name: 'Ver la versión nueva' }));

    expect(await screen.findByRole('heading', { name: 'Cotización COT-000007 · versión 2' })).toBeInTheDocument();
    expect(apiRequest).toHaveBeenLastCalledWith('post', '/api/v1/public/quotes/view', expect.objectContaining({ body: { token: NEWER } }));
    expect(screen.queryByRole('link', { name: 'Responder al negocio' })).toBeInTheDocument();
  });

  it('does not offer to answer an expired quote', async () => {
    vi.mocked(apiRequest).mockResolvedValue(quote({ status: QuoteStatus.Expired }) as never);
    renderPage(`/cotizacion#${LINK}`);

    expect(await screen.findByText(/Venció el 19 de octubre de 2026/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Responder al negocio' })).not.toBeInTheDocument();
  });

  it('says what the server says when the link no longer opens, and asks for it without one', async () => {
    const headers = new AxiosHeaders();
    vi.mocked(apiRequest).mockRejectedValue(new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
      status: 404, data: { detail: 'Esta cotización no existe o su enlace ya venció.' }, statusText: '', headers, config: { headers },
    } as AxiosResponse));
    renderPage(`/cotizacion#${LINK}`);
    expect(await screen.findByRole('alert')).toHaveTextContent('Esta cotización no existe o su enlace ya venció.');
  });

  it('asks for the link of the message when there is none', () => {
    renderPage('/cotizacion');

    expect(screen.getByText('Abre el enlace de tu mensaje')).toBeInTheDocument();
    expect(apiRequest).not.toHaveBeenCalled();
  });
});
