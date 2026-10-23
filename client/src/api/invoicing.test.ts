import { AxiosError, AxiosHeaders } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import api from './axios';
import { getApiErrorMessage } from './errors';
import { downloadInvoiceFile } from './invoicing';

vi.mock('./axios', () => ({ default: { get: vi.fn() } }));

/** A refusal as axios gives it for a download: its body is a blob too. */
function refusal(status: number, body: string, type = 'application/problem+json') {
  const config = { headers: new AxiosHeaders() };
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', config, null, {
    status, statusText: '', headers: {}, config, data: new Blob([body], { type }),
  });
}

describe('downloadInvoiceFile', () => {
  beforeEach(() => vi.resetAllMocks());

  it('names the file as the server does', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: new Blob(['<cfdi/>']), headers: { 'content-disposition': 'attachment; filename="F-1.xml"' } });

    const file = await downloadInvoiceFile('inv-1', 'xml');

    expect(file.fileName).toBe('F-1.xml');
    expect(api.get).toHaveBeenCalledWith('/invoicing/invoices/inv-1/xml', { responseType: 'blob' });
  });

  it('reads back the server\'s reason, which comes as a blob, so it reaches the person', async () => {
    const reason = 'El PDF no está disponible por ahora: descarga el XML, que es la factura.';
    vi.mocked(api.get).mockRejectedValue(refusal(404, JSON.stringify({ status: 404, detail: reason })));

    const error = await downloadInvoiceFile('inv-1', 'pdf').catch((err: unknown) => err);

    expect(getApiErrorMessage(error, 'No se pudo descargar el PDF.')).toBe(reason);
  });

  it('keeps the generic message when the refusal is not JSON', async () => {
    vi.mocked(api.get).mockRejectedValue(refusal(404, '<html>Not found</html>', 'text/html'));

    const error = await downloadInvoiceFile('inv-1', 'pdf').catch((err: unknown) => err);

    expect(getApiErrorMessage(error, 'No se pudo descargar el PDF.')).toBe('No se pudo descargar el PDF.');
  });
});
