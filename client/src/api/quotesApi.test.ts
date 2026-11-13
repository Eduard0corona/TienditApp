import { AxiosError, AxiosHeaders } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import api from './axios';
import { getApiErrorMessage } from './errors';
import { publicQuoteApi, quotesApi } from './quotesApi';

vi.mock('./axios', () => ({ default: { get: vi.fn(), post: vi.fn() } }));

/** A refusal as axios gives it for a download: its body is a blob too. */
function refusal(status: number, body: string) {
  const config = { headers: new AxiosHeaders() };
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', config, null, {
    status, statusText: '', headers: {}, config, data: new Blob([body], { type: 'application/problem+json' }),
  });
}

// The PDF of a quote (Fase 31M): the business's version, and the link's quote with the link in the body.
describe('the PDF of a quote', () => {
  beforeEach(() => vi.resetAllMocks());

  it('downloads a version of the business as a blob, named as the server names it', async () => {
    const pdf = new Blob(['%PDF-1.4']);
    vi.mocked(api.get).mockResolvedValue({ data: pdf, headers: { 'content-disposition': 'attachment; filename=cotizacion-COT-000007-v2.pdf' } });

    const file = await quotesApi.downloadPdf('q 1');

    expect(file).toEqual({ blob: pdf, fileName: 'cotizacion-COT-000007-v2.pdf' });
    expect(api.get).toHaveBeenCalledWith('/quotes/q%201/pdf', { responseType: 'blob' });
  });

  it('downloads the link\'s quote with the link in the body, never in the address, and names it without a name', async () => {
    const pdf = new Blob(['%PDF-1.4']);
    vi.mocked(api.post).mockResolvedValue({ data: pdf, headers: {} });

    const file = await publicQuoteApi.downloadPdf('AbCdEf');

    expect(file).toEqual({ blob: pdf, fileName: 'cotizacion.pdf' });
    expect(api.post).toHaveBeenCalledWith('/public/quotes/pdf', { token: 'AbCdEf' }, { responseType: 'blob' });
  });

  it('reads back the server\'s reason, which comes as a blob, so it reaches the person', async () => {
    const reason = 'Esta cotización no existe o su enlace ya venció. Pídele al negocio que te la mande otra vez.';
    vi.mocked(api.post).mockRejectedValue(refusal(404, JSON.stringify({ status: 404, detail: reason })));
    vi.mocked(api.get).mockRejectedValue(refusal(404, JSON.stringify({ status: 404, detail: 'La cotización no existe.' })));

    const publicError = await publicQuoteApi.downloadPdf('AbCdEf').catch((err: unknown) => err);
    const businessError = await quotesApi.downloadPdf('q1').catch((err: unknown) => err);

    expect(getApiErrorMessage(publicError, 'No se pudo descargar el PDF.')).toBe(reason);
    expect(getApiErrorMessage(businessError, 'No se pudo descargar el PDF.')).toBe('La cotización no existe.');
  });
});
