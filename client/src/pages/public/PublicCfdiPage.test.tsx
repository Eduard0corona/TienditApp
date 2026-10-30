import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import api from '../../api/axios';
import { apiRequest } from '../../api/typedClient';
import { CfdiEmailDocument, OrderInvoiceFileKind } from '../../types';
import { saveBlob } from '../../utils/cfdi';
import PublicCfdiPage from './PublicCfdiPage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../api/axios', () => ({ default: { post: vi.fn() } }));
vi.mock('../../utils/cfdi', async (importOriginal) => ({ ...await importOriginal<typeof import('../../utils/cfdi')>(), saveBlob: vi.fn() }));

const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';
const NOT_FOUND = 'No encontramos esa factura. El enlace pudo haber vencido: pídesela otra vez al negocio.';

function cfdi(overrides: Record<string, unknown> = {}) {
  return {
    businessName: 'Café Luna',
    businessLogoUrl: null,
    document: CfdiEmailDocument.Invoice,
    number: 'F-7',
    uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF',
    amount: 116,
    issuedOn: '2026-10-27',
    issuerName: 'ESCUELA KEMPER URGATE',
    issuerRfc: 'EKU9003173C9',
    receiverName: 'UNIVERSIDAD ROBOTICA ESPAÑOLA',
    receiverRfc: 'URE180429TM6',
    stands: true,
    linkExpiresAt: '2027-01-25T18:00:00Z',
    ...overrides,
  };
}

function refusal(status: number, detail: string) {
  const headers = new AxiosHeaders();
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
    status, data: { detail }, statusText: '', headers, config: { headers },
  } as AxiosResponse);
}

function renderPage(url: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route path="/factura" element={<PublicCfdiPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PublicCfdiPage', () => {
  beforeEach(() => vi.resetAllMocks());

  it('asks for the link of the message when it is opened without one, and asks nothing of the API', () => {
    renderPage('/factura');

    expect(screen.getByRole('heading', { name: 'Abre el enlace de tu mensaje' })).toBeInTheDocument();
    expect(apiRequest).not.toHaveBeenCalled();
  });

  it('shows the CFDI of the link, sent in the body, and downloads its PDF and XML with the same link', async () => {
    vi.mocked(apiRequest).mockResolvedValue(cfdi() as never);
    vi.mocked(api.post).mockResolvedValue({
      data: new Blob(['%PDF']), headers: { 'content-disposition': 'attachment; filename="F-7_5FB2822E.pdf"' },
    });
    renderPage(`/factura#${LINK}`);

    expect(await screen.findByRole('heading', { name: 'Factura F-7' })).toBeInTheDocument();
    expect(apiRequest).toHaveBeenCalledWith('post', '/api/v1/public/cfdi', { body: { token: LINK }, signal: expect.any(AbortSignal) });
    expect(screen.getByText('Café Luna')).toBeInTheDocument();
    expect(screen.getByText('$116.00')).toBeInTheDocument();
    expect(screen.getByText('UNIVERSIDAD ROBOTICA ESPAÑOLA · URE180429TM6')).toBeInTheDocument();
    expect(screen.getByText('5FB2822E-396D-4725-8521-CDC4BDD20CCF')).toBeInTheDocument();
    expect(screen.getByText(/Este enlace funciona hasta el/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Descargar PDF de la factura F-7' }));
    await waitFor(() => expect(saveBlob).toHaveBeenCalledWith(expect.any(Blob), 'F-7_5FB2822E.pdf'));
    expect(api.post).toHaveBeenCalledWith('/public/cfdi/files', { token: LINK, kind: OrderInvoiceFileKind.Pdf }, { responseType: 'blob' });

    vi.mocked(api.post).mockResolvedValue({ data: new Blob(['<xml/>']), headers: {} });
    await userEvent.click(screen.getByRole('button', { name: 'Descargar XML de la factura F-7' }));
    await waitFor(() => expect(saveBlob).toHaveBeenCalledWith(expect.any(Blob), 'factura.xml'));
    expect(api.post).toHaveBeenLastCalledWith('/public/cfdi/files', { token: LINK, kind: OrderInvoiceFileKind.Xml }, { responseType: 'blob' });
  });

  it('names a payment complement by the payment it records', async () => {
    vi.mocked(apiRequest).mockResolvedValue(cfdi({ document: CfdiEmailDocument.PaymentComplement, number: 'P-3', amount: 50 }) as never);
    renderPage(`/factura#${LINK}`);

    expect(await screen.findByRole('heading', { name: 'Complemento de pago P-3' })).toBeInTheDocument();
    expect(screen.getByText('Pago')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Descargar PDF del complemento de pago P-3' })).toBeInTheDocument();
  });

  it('offers no download of a CFDI that no longer stands', async () => {
    vi.mocked(apiRequest).mockResolvedValue(cfdi({ stands: false }) as never);
    renderPage(`/factura#${LINK}`);

    expect(await screen.findByRole('alert')).toHaveTextContent('Este CFDI ya no está vigente: el negocio pidió cancelarlo.');
    expect(screen.queryByRole('button', { name: /^Descargar/ })).not.toBeInTheDocument();
  });

  it('says what the API answered: a link unknown or expired, or a download refused', async () => {
    vi.mocked(apiRequest).mockRejectedValue(refusal(404, NOT_FOUND));
    renderPage(`/factura#${LINK}`);
    expect(await screen.findByRole('alert')).toHaveTextContent(NOT_FOUND);
  });

  it('says why a download was refused, read back from the blob the refusal comes as', async () => {
    vi.mocked(apiRequest).mockResolvedValue(cfdi() as never);
    const headers = new AxiosHeaders();
    const refused = new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
      status: 400, data: new Blob([JSON.stringify({ detail: 'Este CFDI ya no está vigente: el negocio pidió cancelarlo. Pídele el que lo sustituye.' })]),
      statusText: '', headers, config: { headers },
    } as AxiosResponse);
    vi.mocked(api.post).mockRejectedValue(refused);
    renderPage(`/factura#${LINK}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Descargar PDF de la factura F-7' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Este CFDI ya no está vigente');
    expect(saveBlob).not.toHaveBeenCalled();
  });
});
