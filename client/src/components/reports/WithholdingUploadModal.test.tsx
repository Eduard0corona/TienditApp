import { StrictMode } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PlatformWithholdingCertificate } from '../../api/platformWithholdings';
import { apiRequest } from '../../api/typedClient';
import { OrderChannel } from '../../types';
import WithholdingUploadModal, { MAX_WITHHOLDING_FILES } from './WithholdingUploadModal';
import {
  FILE_NOT_READABLE_MESSAGE,
  MAX_WITHHOLDING_XML_BYTES,
  MAX_WITHHOLDING_XML_LENGTH,
  NOT_XML_CONTENT_MESSAGE,
  NOT_XML_FILE_MESSAGE,
  XML_TOO_BIG_MESSAGE,
} from './withholdingLabels';

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
    await user.selectOptions(within(dialog).getByLabelText('Plataforma que emitió los CFDI'), String(OrderChannel.DidiFood));
    await user.upload(within(dialog).getByLabelText('Archivos XML'), [
      file('septiembre.xml', XML),
      file('agosto.xml', `${XML}<!-- agosto -->`),
      file('factura.pdf', '%PDF-1.7', 'application/pdf'),
    ]);
    expect(within(dialog).getByText('3 archivos elegidos.')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Subir' }));

    expect(await within(dialog).findByText('Se registró 1 de 3. Revisa los que no entraron.')).toHaveFocus();
    const items = within(dialog).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent('Registradoseptiembre.xmlRegistrado: septiembre de 2026, ISR $25.00 e IVA $80.00.');
    expect(items[1]).toHaveTextContent('No entróagosto.xmlEste CFDI ya está registrado en DiDi Food, agosto de 2026.');
    expect(items[2]).toHaveTextContent(`No entrófactura.pdf${NOT_XML_FILE_MESSAGE}`);
    // The PDF never reached the server; the two XML went one by one, with the platform chosen.
    expect(request.mock.calls.map(([, , options]) => (options as Options).body?.channel)).toEqual([OrderChannel.DidiFood, OrderChannel.DidiFood]);
    expect((request.mock.calls[0][2] as Options).body?.xml).toBe(XML);
    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: ['platform-withholdings'] }));
  });

  it('refuses before sending a file too big or one that is not text, and reads nothing again when none went in', async () => {
    const user = userEvent.setup();
    const { invalidate } = renderModal();

    await user.upload(screen.getByLabelText('Archivos XML'), [
      // Past the bytes of 512 K characters in UTF-16, and past 512 K characters in UTF-8.
      file('enorme.xml', 'x'.repeat(MAX_WITHHOLDING_XML_BYTES + 1)),
      file('grande.xml', 'x'.repeat(MAX_WITHHOLDING_XML_LENGTH + 1)),
      new File([new Uint8Array([0x3c, 0x00, 0x3f, 0x00, 0x78, 0x00])], 'utf16-sin-marca.xml', { type: 'text/xml' }),
    ]);
    await user.click(screen.getByRole('button', { name: 'Subir' }));

    expect(await screen.findByText('No se registró ninguno de los 3. Revisa por qué abajo.')).toBeInTheDocument();
    const items = screen.getAllByRole('listitem');
    expect(items[0]).toHaveTextContent(XML_TOO_BIG_MESSAGE);
    expect(items[1]).toHaveTextContent(XML_TOO_BIG_MESSAGE);
    expect(items[2]).toHaveTextContent(NOT_XML_CONTENT_MESSAGE);
    expect(request).not.toHaveBeenCalled();
    expect(invalidate).not.toHaveBeenCalled();
  });

  it('says when a CFDI is kept without counting and when a file can no longer be read, also mounted twice by StrictMode', async () => {
    const user = userEvent.setup();
    // The platform already replaced it: the server keeps it in the list, out of the reconciliation, with why.
    request.mockResolvedValue({
      ...registered(9), removedAt: '2026-10-22T18:00:00Z', removalReason: 'Lo sustituye el CFDI con folio 22222222.',
    } as never);
    // Development mounts every component twice: the batch still goes.
    render(
      <StrictMode>
        <QueryClientProvider client={new QueryClient()}>
          <WithholdingUploadModal platform={OrderChannel.UberEats} onHide={vi.fn()} />
        </QueryClientProvider>
      </StrictMode>,
    );
    // Chosen from a USB taken out afterwards: the browser cannot read it.
    const gone = file('desconectado.xml', XML);
    Object.defineProperty(gone, 'arrayBuffer', { value: () => Promise.reject(new DOMException('Gone', 'NotReadableError')) });

    await user.upload(screen.getByLabelText('Archivos XML'), [file('sustituido.xml', XML), gone]);
    await user.click(screen.getByRole('button', { name: 'Subir' }));

    expect(await screen.findByText('Se registró 1 de 2. Revisa los que no entraron.')).toBeInTheDocument();
    const items = screen.getAllByRole('listitem');
    expect(items[0]).toHaveTextContent('No cuentasustituido.xmlSe guardó sin contar (septiembre de 2026): Lo sustituye el CFDI con folio 22222222.');
    expect(items[1]).toHaveTextContent(`No entródesconectado.xml${FILE_NOT_READABLE_MESSAGE}`);
    expect(request).toHaveBeenCalledTimes(1);
    // The dialog is not stuck sending: it closes.
    expect(screen.getByRole('button', { name: 'Listo' })).toBeEnabled();
  });

  it('reads a file with its byte-order mark, in UTF-16 or in Windows-1252, as text', async () => {
    const user = userEvent.setup();
    request.mockResolvedValue(registered(9) as never);
    renderModal();
    const utf16 = new Uint8Array([0xff, 0xfe, ...Array.from('<x/>').flatMap((char) => [char.charCodeAt(0), 0])]);

    await user.upload(screen.getByLabelText('Archivos XML'), [
      new File([new Uint8Array([0xef, 0xbb, 0xbf]), '<x/>'], 'con-marca.xml', { type: 'text/xml' }),
      new File([utf16], 'utf16.xml', { type: 'text/xml' }),
      new File([new Uint8Array([0x3c, 0x78, 0x3e, 0xf1, 0x3c, 0x2f, 0x78, 0x3e])], 'ansi.xml', { type: 'text/xml' }),
    ]);
    await user.click(screen.getByRole('button', { name: 'Subir' }));

    expect(await screen.findByText('Se registraron los 3 CFDI.')).toBeInTheDocument();
    expect(request.mock.calls.map(([, , options]) => (options as Options).body?.xml)).toEqual(['<x/>', '<x/>', '<x>ñ</x>']);
  });

  it('stops the batch when the person leaves, without sending the files after the current one', async () => {
    const user = userEvent.setup();
    let finish: (() => void) | undefined;
    request.mockImplementation((() => new Promise((resolve) => { finish = () => resolve(registered(9)); })) as unknown as typeof apiRequest);
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = render(
      <QueryClientProvider client={queryClient}>
        <WithholdingUploadModal platform={OrderChannel.UberEats} onHide={vi.fn()} />
      </QueryClientProvider>,
    );
    await user.upload(screen.getByLabelText('Archivos XML'), [file('uno.xml', XML), file('dos.xml', XML), file('tres.xml', XML)]);
    await user.click(screen.getByRole('button', { name: 'Subir' }));
    expect(await screen.findByText('Subiendo 1 de 3…')).toBeInTheDocument();
    // Busy, it does not close by its buttons.
    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();

    unmount();
    finish?.();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(request).toHaveBeenCalledTimes(1);
  });

  it('takes up to its limit of files at once', async () => {
    const user = userEvent.setup();
    renderModal();

    await user.upload(screen.getByLabelText('Archivos XML'),
      Array.from({ length: MAX_WITHHOLDING_FILES + 1 }, (_, index) => file(`cfdi-${index}.xml`, XML)));

    expect(screen.getByRole('alert')).toHaveTextContent(`Sube hasta ${MAX_WITHHOLDING_FILES} archivos a la vez.`);
    expect(screen.getByRole('button', { name: 'Subir' })).toBeDisabled();
  });

  it('starts on the platform on screen, and closes with Listo saying what went in', async () => {
    const user = userEvent.setup();
    request.mockResolvedValue(registered(9) as never);
    const { onHide } = renderModal();

    expect(screen.getByLabelText('Plataforma que emitió los CFDI')).toHaveValue(String(OrderChannel.UberEats));
    await user.upload(screen.getByLabelText('Archivos XML'), file('septiembre.xml', XML));
    await user.click(screen.getByRole('button', { name: 'Subir' }));
    expect(await screen.findByText('Se registró el CFDI.')).toBeInTheDocument();
    expect((request.mock.calls[0][2] as Options).body?.channel).toBe(OrderChannel.UberEats);

    await user.click(screen.getByRole('button', { name: 'Subir otros' }));
    expect(screen.getByLabelText('Archivos XML')).toHaveFocus();
    await user.upload(screen.getByLabelText('Archivos XML'), file('factura.xml', XML));
    request.mockRejectedValueOnce(new Error('caída'));
    await user.click(screen.getByRole('button', { name: 'Subir' }));
    expect(await screen.findByText('No se registró el CFDI.')).toBeInTheDocument();

    // The page follows what went in, also after a batch where nothing did.
    await user.click(screen.getByRole('button', { name: 'Listo' }));
    expect(onHide).toHaveBeenCalledWith({ channel: OrderChannel.UberEats, year: 2026 });
  });
});
