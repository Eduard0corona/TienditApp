import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, type Schemas } from '../../api/typedClient';
import Invoicing from './Invoicing';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));
vi.mock('../../components/layout/SectionTabs', () => ({ default: () => null }));
const confirm = vi.fn();
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm }) }));

type Settings = Schemas['InvoicingSettingsDto'];

const READY_TO_UPLOAD: Settings = {
  available: true, sandbox: false, serie: 'F', rfc: 'EKU9003173C9', legalName: 'ESCUELA KEMPER URGATE', fiscalRegime: '601',
  postalCode: '06000', missingData: [], certificate: undefined, ready: false,
};

const WITH_CERTIFICATE: Settings = {
  ...READY_TO_UPLOAD,
  ready: true,
  certificate: {
    number: '30001000000400002434', rfc: 'EKU9003173C9', validFrom: '2023-05-18T11:43:51Z', validTo: '2027-05-18T11:43:51Z',
    uploadedAt: '2026-10-23T17:00:00Z', usable: true, expiresSoon: false, expired: false, otherRfc: false,
  },
};

function serve(settings: Settings, write: (method: string, path: string, body: unknown) => Promise<unknown> = () => Promise.resolve(settings)) {
  vi.mocked(apiRequest).mockImplementation(((method: string, path: string, options?: { body?: unknown }) => {
    if (method === 'get' && path === '/api/v1/invoicing/settings') return Promise.resolve(settings);
    return write(method, path, options?.body);
  }) as unknown as typeof apiRequest);
}

function renderPage(queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <Invoicing />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Invoicing settings', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    confirm.mockResolvedValue(true);
  });

  it('says invoicing is not offered while the platform has no PAC', async () => {
    serve({ ...READY_TO_UPLOAD, available: false });
    renderPage();

    expect(await screen.findByRole('heading', { name: 'La facturación no está disponible por ahora' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Certificado (.cer)')).not.toBeInTheDocument();
  });

  it('lists what Mi negocio still lacks', async () => {
    serve({ ...READY_TO_UPLOAD, rfc: null, missingData: ['El RFC del negocio.'] });
    renderPage();

    expect(await screen.findByText('El RFC del negocio.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Configuración › Negocio' })).toHaveAttribute('href', '/settings/business');
  });

  it('sends the CSD files in base64 with the password, and forgets the password', async () => {
    let sent: unknown;
    serve(READY_TO_UPLOAD, (method, path, body) => {
      sent = { method, path, body };
      return Promise.resolve(WITH_CERTIFICATE);
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    renderPage(queryClient);

    await userEvent.upload(await screen.findByLabelText('Certificado (.cer)'), new File(['CER'], 'csd.cer'));
    await userEvent.upload(screen.getByLabelText('Llave privada (.key)'), new File(['KEY'], 'csd.key'));
    await userEvent.type(screen.getByLabelText('Contraseña de la llave privada'), '12345678a');
    await userEvent.click(screen.getByRole('button', { name: 'Subir certificado' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Certificado de sello digital guardado.'));
    expect(sent).toEqual({
      method: 'put',
      path: '/api/v1/invoicing/settings/certificate',
      body: { certificate: btoa('CER'), privateKey: btoa('KEY'), password: '12345678a' },
    });
    expect(await screen.findByText('30001000000400002434')).toBeInTheDocument();
    expect(screen.getByLabelText('Contraseña de la llave privada')).toHaveValue('');
    // Nor do the key and its password stay in the cache of mutations, where the page's scripts could read them.
    await waitFor(() => expect(queryClient.getMutationCache().getAll()).toEqual([]));
  });

  it('refuses files that cannot be a CSD before sending them', async () => {
    serve(READY_TO_UPLOAD);
    renderPage();

    await userEvent.upload(await screen.findByLabelText('Certificado (.cer)'), new File([new Uint8Array(20_000)], 'grande.cer'));
    await userEvent.upload(screen.getByLabelText('Llave privada (.key)'), new File(['KEY'], 'csd.key'));
    await userEvent.type(screen.getByLabelText('Contraseña de la llave privada'), 'x');
    await userEvent.click(screen.getByRole('button', { name: 'Subir certificado' }));

    expect(toast.error).toHaveBeenCalled();
    expect(vi.mocked(apiRequest).mock.calls.filter(([method]) => method !== 'get')).toEqual([]);
  });

  it('warns that the certificate expires soon', async () => {
    serve({ ...WITH_CERTIFICATE, certificate: { ...WITH_CERTIFICATE.certificate!, expiresSoon: true } });
    renderPage();

    expect(await screen.findByText('Vence pronto: tramita uno nuevo en el SAT y súbelo antes de que venza.')).toBeInTheDocument();
    expect(screen.getByText('Listo para facturar')).toBeInTheDocument();
  });
});
