import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { publicQuoteApi } from '../../api/quotesApi';
import { apiRequest } from '../../api/typedClient';
import { QuoteStatus } from '../../types';
import { saveBlob } from '../../utils/cfdi';
import PublicQuotePage from './PublicQuotePage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../utils/cfdi', async (importOriginal) => ({ ...await importOriginal<typeof import('../../utils/cfdi')>(), saveBlob: vi.fn() }));

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
    canReply: false,
    repliedAt: null,
    repliedOn: null,
    repliedFromLink: false,
    response: null,
    ...overrides,
  };
}

function renderPage(url: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
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

  it('downloads the PDF of the link, sending the link in the body (Fase 31M)', async () => {
    const pdf = new Blob(['%PDF-1.4'], { type: 'application/pdf' });
    const download = vi.spyOn(publicQuoteApi, 'downloadPdf').mockResolvedValue({ blob: pdf, fileName: 'cotizacion-COT-000007.pdf' });
    vi.mocked(apiRequest).mockResolvedValue(quote() as never);
    renderPage(`/cotizacion#${LINK}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Descargar PDF de la cotización COT-000007' }));

    await waitFor(() => expect(saveBlob).toHaveBeenCalledWith(pdf, 'cotizacion-COT-000007.pdf'));
    expect(download).toHaveBeenCalledWith(LINK);
    download.mockRestore();
  });

  it('says why the PDF of the link did not download', async () => {
    const headers = new AxiosHeaders();
    const download = vi.spyOn(publicQuoteApi, 'downloadPdf').mockRejectedValue(new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
      status: 404, data: { detail: 'Esta cotización no existe o su enlace ya venció.' }, statusText: '', headers, config: { headers },
    } as AxiosResponse));
    vi.mocked(apiRequest).mockResolvedValue(quote() as never);
    renderPage(`/cotizacion#${LINK}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Descargar PDF de la cotización COT-000007' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Esta cotización no existe o su enlace ya venció.');
    expect(saveBlob).not.toHaveBeenCalled();
    download.mockRestore();
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

  it('lets the customer ask for a new one once it expired, and offers no answer to an accepted one', async () => {
    vi.mocked(apiRequest).mockResolvedValueOnce(quote({ status: QuoteStatus.Expired }) as never);
    const { unmount } = renderPage(`/cotizacion#${LINK}`);

    expect(await screen.findByText(/Venció el 19 de octubre de 2026/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Responder al negocio' })).toBeInTheDocument();
    unmount();

    vi.mocked(apiRequest).mockResolvedValueOnce(quote({ status: QuoteStatus.Accepted }) as never);
    renderPage(`/cotizacion#${LINK}`);
    expect(await screen.findByText('Le dijiste al negocio que la aceptas.')).toBeInTheDocument();
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

  it('lets the customer accept it here, saying what they want, and shows it accepted', async () => {
    vi.mocked(apiRequest).mockResolvedValueOnce(quote({ canReply: true }) as never)
      .mockResolvedValueOnce(quote({
        status: QuoteStatus.Accepted, repliedFromLink: true, repliedAt: '2026-10-06T04:00:00Z', repliedOn: '2026-10-05', response: 'Los espero el jueves',
      }) as never);
    renderPage(`/cotizacion#${LINK}`);

    expect(await screen.findByText(/Respóndela aquí o escríbele al negocio si tienes dudas/)).toBeInTheDocument();
    // One primary action: while the customer can answer here, the business's WhatsApp is for questions.
    expect(screen.getByRole('link', { name: 'Preguntar algo al negocio' })).not.toHaveClass('btn-primary');
    await userEvent.click(screen.getByRole('button', { name: 'Aceptar la cotización' }));
    expect(screen.getByText(/Aceptas la cotización COT-000007 por \$810\.00, ya con la visita descontada/)).toBeInTheDocument();
    // The button that had the focus is gone: the message takes it.
    const message = screen.getByLabelText('Mensaje para Plomería Ruiz (opcional)');
    expect(message).toHaveFocus();
    await userEvent.type(message, '  Los espero el jueves  ');
    expect(screen.getByText('24 de 500 caracteres')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Sí, la acepto' }));

    expect(apiRequest).toHaveBeenLastCalledWith('post', '/api/v1/public/quotes/reply',
      { body: { token: LINK, accepted: true, response: 'Los espero el jueves' } });
    const status = await screen.findByText(/La aceptaste aquí el 5 de octubre de 2026\. Plomería Ruiz ya lo sabe/);
    expect(status).toHaveFocus();
    expect(screen.getByText('Tu mensaje: «Los espero el jueves»')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Aceptar la cotización' })).not.toBeInTheDocument();
  });

  it('lets the customer reject it here without a message, or step back before sending it', async () => {
    vi.mocked(apiRequest).mockResolvedValueOnce(quote({ canReply: true }) as never)
      .mockResolvedValueOnce(quote({ status: QuoteStatus.Rejected, repliedFromLink: true, repliedAt: '2026-10-06T04:00:00Z', repliedOn: '2026-10-05' }) as never);
    renderPage(`/cotizacion#${LINK}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Aceptar la cotización' }));
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(screen.getByRole('button', { name: 'Aceptar la cotización' })).toHaveFocus();
    await userEvent.click(screen.getByRole('button', { name: 'No la acepto' }));
    expect(screen.getByText(/Plomería Ruiz sabrá que no la aceptas/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Enviar mi respuesta' }));

    expect(apiRequest).toHaveBeenLastCalledWith('post', '/api/v1/public/quotes/reply', { body: { token: LINK, accepted: false, response: null } });
    expect(await screen.findByText('Le dijiste aquí que no la aceptas, el 5 de octubre de 2026.')).toBeInTheDocument();
  });

  it('says why an answer was refused and shows the quote as it stands now', async () => {
    const headers = new AxiosHeaders();
    vi.mocked(apiRequest).mockResolvedValueOnce(quote({ canReply: true }) as never)
      .mockRejectedValueOnce(new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
        status: 409, data: { detail: 'La cotización acaba de cambiar: ábrela otra vez para ver cómo quedó.' }, statusText: '', headers, config: { headers },
      } as AxiosResponse))
      .mockResolvedValueOnce(quote({ status: QuoteStatus.Superseded, newerToken: NEWER }) as never);
    renderPage(`/cotizacion#${LINK}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Aceptar la cotización' }));
    await userEvent.click(screen.getByRole('button', { name: 'Sí, la acepto' }));

    expect(await screen.findByRole('link', { name: 'Ver la versión nueva' })).toBeInTheDocument();
    expect(apiRequest).toHaveBeenLastCalledWith('post', '/api/v1/public/quotes/view', expect.objectContaining({ body: { token: LINK } }));
    expect(screen.queryByRole('button', { name: 'Sí, la acepto' })).not.toBeInTheDocument();
    // Why it was not taken stays, also once the quote can no longer be answered here.
    const refusal = screen.getByRole('alert');
    expect(refusal).toHaveTextContent('Tu respuesta no se envió. La cotización acaba de cambiar: ábrela otra vez para ver cómo quedó.');
    expect(refusal).toHaveFocus();
  });

  it('says the business now records the answer itself when it turned them off meanwhile', async () => {
    const headers = new AxiosHeaders();
    vi.mocked(apiRequest).mockResolvedValueOnce(quote({ canReply: true }) as never)
      .mockRejectedValueOnce(new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
        status: 400, data: { detail: 'Este negocio recibe la respuesta por mensaje o en persona: escríbele para responderle.' }, statusText: '', headers, config: { headers },
      } as AxiosResponse))
      .mockResolvedValueOnce(quote({ canReply: false }) as never);
    renderPage(`/cotizacion#${LINK}`);

    await userEvent.click(await screen.findByRole('button', { name: 'No la acepto' }));
    await userEvent.click(screen.getByRole('button', { name: 'Enviar mi respuesta' }));

    expect(await screen.findByRole('link', { name: 'Responder al negocio' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Este negocio recibe la respuesta por mensaje o en persona');
    expect(screen.queryByRole('button', { name: 'No la acepto' })).not.toBeInTheDocument();
  });

  it('keeps the quote and why the answer was refused when reading it again fails for another reason', async () => {
    const headers = new AxiosHeaders();
    vi.mocked(apiRequest).mockResolvedValueOnce(quote({ canReply: true }) as never)
      .mockRejectedValueOnce(new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
        status: 409, data: { detail: 'La cotización acaba de cambiar: ábrela otra vez para ver cómo quedó.' }, statusText: '', headers, config: { headers },
      } as AxiosResponse))
      .mockRejectedValueOnce(new AxiosError('Network Error', 'ERR_NETWORK', { headers }));
    renderPage(`/cotizacion#${LINK}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Aceptar la cotización' }));
    await userEvent.click(screen.getByRole('button', { name: 'Sí, la acepto' }));

    await waitFor(() => expect(screen.getAllByRole('alert')).toHaveLength(2));
    expect(screen.getByRole('heading', { name: 'Cotización COT-000007' })).toBeInTheDocument();
    expect(screen.getByText(/La cotización acaba de cambiar/)).toBeInTheDocument();
  });

  it('announces the same refusal again and takes the focus to it on every attempt', async () => {
    const headers = new AxiosHeaders();
    const offline = () => new AxiosError('Network Error', 'ERR_NETWORK', { headers });
    vi.mocked(apiRequest).mockResolvedValueOnce(quote({ canReply: true }) as never)
      .mockRejectedValueOnce(offline()).mockRejectedValueOnce(offline());
    renderPage(`/cotizacion#${LINK}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Aceptar la cotización' }));
    await userEvent.click(screen.getByRole('button', { name: 'Sí, la acepto' }));
    const first = await screen.findByRole('alert');
    await waitFor(() => expect(first).toHaveFocus());
    expect(first).toHaveTextContent('Tu respuesta no se envió. No hay conexión con el servidor.');

    await userEvent.click(screen.getByRole('button', { name: 'Sí, la acepto' }));

    // A new alert (announced again), with the focus, though it says the same.
    await waitFor(() => expect(screen.getByRole('alert')).not.toBe(first));
    expect(screen.getByRole('alert')).toHaveFocus();
  });

  it('says only that the link no longer opens when it expired while answering', async () => {
    const headers = new AxiosHeaders();
    const gone = () => new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
      status: 404, data: { detail: 'Esta cotización no existe o su enlace ya venció.' }, statusText: '', headers, config: { headers },
    } as AxiosResponse);
    vi.mocked(apiRequest).mockResolvedValueOnce(quote({ canReply: true }) as never)
      .mockRejectedValueOnce(gone()).mockRejectedValueOnce(gone());
    renderPage(`/cotizacion#${LINK}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Aceptar la cotización' }));
    await userEvent.click(screen.getByRole('button', { name: 'Sí, la acepto' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Esta cotización no existe o su enlace ya venció.');
    expect(screen.getAllByRole('alert')).toHaveLength(1);
    expect(screen.queryByRole('heading', { name: 'Cotización COT-000007' })).not.toBeInTheDocument();
  });

  it('keeps the answer to the business while it records it itself', async () => {
    vi.mocked(apiRequest).mockResolvedValueOnce(quote({ canReply: false }) as never);
    renderPage(`/cotizacion#${LINK}`);

    expect(await screen.findByText(/Para aceptarla o preguntar algo, responde al negocio/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Aceptar la cotización' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Responder al negocio' })).toHaveClass('btn-primary');
  });

  it('asks for the link of the message when there is none', () => {
    renderPage('/cotizacion');

    expect(screen.getByText('Abre el enlace de tu mensaje')).toBeInTheDocument();
    expect(apiRequest).not.toHaveBeenCalled();
  });
});
