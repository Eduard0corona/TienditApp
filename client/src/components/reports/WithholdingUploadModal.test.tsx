import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PlatformWithholdingCertificate } from '../../api/platformWithholdings';
import { apiRequest } from '../../api/typedClient';
import { OrderChannel } from '../../types';
import WithholdingUploadModal, { MAX_WITHHOLDING_FILES } from './WithholdingUploadModal';
import { MAX_WITHHOLDING_XML_BYTES, NOT_XML_FILE_MESSAGE, XML_TOO_BIG_MESSAGE } from './withholdingLabels';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

type Options = { body?: { channel: OrderChannel; xml: string } };

const request = vi.mocked(apiRequest);
const XML = '<?xml version="1.0"?><retenciones:Retenciones Version="2.0"/>';

function apiError(status: number, code: string, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function registered(monthTo: number): PlatformWithholdingCertificate {
  return {
    id: `c${monthTo}`, channel: OrderChannel.DidiFood, uuid: `0000000${monthTo}-0000-4000-8000-000000000000`, issuerRfc: 'DIF180906TT8',
    issuerName: 'DIDI', year: 2026, monthFrom: monthTo, monthTo, issuedOn: '2026-10-02T00:00:00', operationTotal: 1000, isrWithheld: 25,
    vatWithheld: 80, totalWithheld: 105, createdAt: '2026-10-22T18:00:00Z',
  };
}

function file(name: string, text: string, type = 'text/xml'): File {
  return new File([text], name, { type });
}

function renderModal(onHide = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
  render(
    <QueryClientProvider client={queryClient}>
      <WithholdingUploadModal platform={OrderChannel.UberEats} onHide={onHide} />
    </QueryClientProvider>,
  );
  return { invalidate, onHide };
}

describe('WithholdingUploadModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('sends each XML on its own to the platform chosen, and says what came of each', async () => {
    // A browser lets the person pick «All files» past the accept list: a PDF can still come.
    const user = userEvent.setup({ applyAccept: false });
    request.mockImplementation(((_method: string, _path: string, options: Options) => {
      if (options.body?.xml.includes('agosto')) return Promise.reject(apiError(409, 'conflict', 'Este CFDI ya está registrado en DiDi Food, agosto de 2026.'));
      return Promise.resolve(registered(9));
    }) as unknown as typeof apiRequest);
    const { invalidate } = renderModal();

    const dialog = screen.getByRole('dialog', { name: 'Subir CFDI de retenciones' });
    await user.selectOptions(within(dialog).getByLabelText('Plataforma que lo emitió'), String(OrderChannel.DidiFood));
    await user.upload(within(dialog).getByLabelText('Archivos XML'), [
      file('septiembre.xml', XML),
      file('agosto.xml', `${XML}<!-- agosto -->`),
      file('factura.pdf', '%PDF-1.7', 'application/pdf'),
    ]);
    expect(within(dialog).getByText('3 archivos elegidos.')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Subir' }));

    expect(await within(dialog).findByText('Se registraron 1 de 3. Revisa los que no entraron.')).toBeInTheDocument();
    const items = within(dialog).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent('Registradoseptiembre.xmlRegistrado: septiembre de 2026, ISR $25.00 e IVA $80.00.');
    expect(items[1]).toHaveTextContent('No entróagosto.xmlEste CFDI ya está registrado en DiDi Food, agosto de 2026.');
    expect(items[2]).toHaveTextContent(`No entrófactura.pdf${NOT_XML_FILE_MESSAGE}`);
    // The PDF never reached the server; the two XML went one by one, with the platform chosen.
    expect(request.mock.calls.map(([, , options]) => (options as Options).body?.channel)).toEqual([OrderChannel.DidiFood, OrderChannel.DidiFood]);
    expect((request.mock.calls[0][2] as Options).body?.xml).toBe(XML);
    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: ['platform-withholdings'] }));
  });

  it('refuses a file over 512 KB before sending it, and reads nothing again when none went in', async () => {
    const user = userEvent.setup();
    const { invalidate } = renderModal();

    await user.upload(screen.getByLabelText('Archivos XML'), file('grande.xml', 'x'.repeat(MAX_WITHHOLDING_XML_BYTES + 1)));
    await user.click(screen.getByRole('button', { name: 'Subir' }));

    expect(await screen.findByText(XML_TOO_BIG_MESSAGE)).toBeInTheDocument();
    expect(request).not.toHaveBeenCalled();
    expect(invalidate).not.toHaveBeenCalled();
  });

  it('takes up to its limit of files at once', async () => {
    const user = userEvent.setup();
    renderModal();

    await user.upload(screen.getByLabelText('Archivos XML'),
      Array.from({ length: MAX_WITHHOLDING_FILES + 1 }, (_, index) => file(`cfdi-${index}.xml`, XML)));

    expect(screen.getByRole('alert')).toHaveTextContent(`Sube hasta ${MAX_WITHHOLDING_FILES} archivos a la vez.`);
    expect(screen.getByRole('button', { name: 'Subir' })).toBeDisabled();
  });

  it('starts on the platform on screen, and closes with Listo after a batch', async () => {
    const user = userEvent.setup();
    request.mockResolvedValue(registered(9) as never);
    const { onHide } = renderModal();

    expect(screen.getByLabelText('Plataforma que lo emitió')).toHaveValue(String(OrderChannel.UberEats));
    await user.upload(screen.getByLabelText('Archivos XML'), file('septiembre.xml', XML));
    await user.click(screen.getByRole('button', { name: 'Subir' }));
    expect(await screen.findByText('Se registró el CFDI.')).toBeInTheDocument();
    expect((request.mock.calls[0][2] as Options).body?.channel).toBe(OrderChannel.UberEats);

    await user.click(screen.getByRole('button', { name: 'Listo' }));
    expect(onHide).toHaveBeenCalled();
  });
});
