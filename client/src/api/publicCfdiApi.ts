import api from './axios';
import { fileNameOf, readBlobError } from './blobDownloads';
import { apiRequest, type Schemas } from './typedClient';
import { OrderInvoiceFileKind } from '../types';

// The CFDI a business sent by WhatsApp (Fase 28M, ADR 0058), opened with the link of its message (/factura#link) without an
// account. The link travels in the body, never in the URL: request telemetry keeps URLs.

export type PublicCfdi = Schemas['PublicCfdiDto'];

export const publicCfdiApi = {
  /** The CFDI of the link; unknown, expired or of a business no longer active, it is not found (404). */
  get: (token: string, signal?: AbortSignal) => apiRequest('post', '/api/v1/public/cfdi', { body: { token }, signal }),

  /** Its XML or its PDF, while it stands; the refusal's message (no longer in force, expired) reaches the person. */
  async download(token: string, kind: 'xml' | 'pdf'): Promise<{ blob: Blob; fileName: string }> {
    let response;
    try {
      response = await api.post<Blob>(
        '/public/cfdi/files',
        { token, kind: kind === 'xml' ? OrderInvoiceFileKind.Xml : OrderInvoiceFileKind.Pdf },
        { responseType: 'blob' });
    } catch (err: unknown) {
      await readBlobError(err);
      throw err;
    }
    return { blob: response.data, fileName: fileNameOf(response.headers['content-disposition'], `factura.${kind}`) };
  },
};
