import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { TenantContext } from '../../contexts/TenantContextDefinition';
import type { TenantProfileDto } from '../../types';
import Invoicing from './Invoicing';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));
vi.mock('../../components/layout/SectionTabs', () => ({ default: () => null }));
const confirm = vi.fn();
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm }) }));

type Settings = Schemas['InvoicingSettingsDto'];

const READY_TO_UPLOAD: Settings = {
  available: true, sandbox: false, serie: 'F', paymentSerie: 'P', creditNoteSerie: 'NC', rfc: 'EKU9003173C9', legalName: 'ESCUELA KEMPER URGATE', fiscalRegime: '601',
  postalCode: '06000', missingData: [], certificate: undefined, ready: false, selfInvoicing: false,
  selfInvoicePath: '/m/demo/facturar', whatsAppConnected: false, whatsAppTemplates: [], whatsAppDefaultText: 'Hola {nombre}: {negocio} te envía {comprobante} por {monto}, del {fecha}. Descarga el XML y el PDF aquí: {enlace}',
  accountantEmail: null, sendsMonthToAccountant: false,
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

function renderPage(
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
  tenant: Partial<TenantProfileDto> = { id: 'negocio', name: 'Café Luna' },
) {
  return render(
    <QueryClientProvider client={queryClient}>
      <TenantContext.Provider
        value={{ tenant: tenant as TenantProfileDto, capabilities: null, isLoading: false, refreshTenant: () => Promise.resolve() }}
      >
        <MemoryRouter>
          <Invoicing />
        </MemoryRouter>
      </TenantContext.Provider>
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

  it('says the PDF goes without a logo until the business uploads one, and where', async () => {
    serve(WITH_CERTIFICATE);
    renderPage();

    expect(await screen.findByText('Logo en el PDF')).toBeInTheDocument();
    expect(screen.getByText(/Sin logo\./)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sube el logo de tu negocio' })).toHaveAttribute('href', '/settings/business');
  });

  it('shows the logo of the business that goes on the PDF', async () => {
    serve(WITH_CERTIFICATE);
    const logoImages = { original: '/l_original.webp', large: '/l_lg.webp', medium: '/l_md.webp', small: '/l_sm.webp', thumbnail: '/l_xs.webp' };
    const { container } = renderPage(undefined, { id: 'negocio', name: 'Café Luna', logoImages });

    expect(await screen.findByText('El de tu negocio')).toBeInTheDocument();
    expect(container.querySelector('img')).toHaveAttribute('src', '/l_xs.webp');
    expect(screen.queryByRole('link', { name: 'Sube el logo de tu negocio' })).not.toBeInTheDocument();
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

  it('changes the series of the invoices, the payment complements and the credit notes together', async () => {
    let sent: unknown;
    serve(WITH_CERTIFICATE, (method, path, body) => {
      sent = { method, path, body };
      return Promise.resolve({ ...WITH_CERTIFICATE, serie: 'A', paymentSerie: 'CP', creditNoteSerie: 'E' });
    });
    renderPage();

    const save = await screen.findByRole('button', { name: 'Guardar series' });
    expect(save).toBeDisabled();
    await userEvent.clear(screen.getByLabelText('Serie de los complementos de pago'));
    await userEvent.type(screen.getByLabelText('Serie de los complementos de pago'), 'cp ');
    await userEvent.clear(screen.getByLabelText('Serie de las facturas'));
    await userEvent.type(screen.getByLabelText('Serie de las facturas'), 'A');
    await userEvent.clear(screen.getByLabelText('Serie de las notas de crédito'));
    await userEvent.type(screen.getByLabelText('Serie de las notas de crédito'), 'e');
    await userEvent.click(save);

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith(
      'Tus próximas facturas llevan la serie A; sus complementos de pago, la CP, y sus notas de crédito, la E.'));
    expect(sent).toEqual({
      method: 'put', path: '/api/v1/invoicing/settings/serie', body: { serie: 'A', paymentSerie: 'cp', creditNoteSerie: 'e' },
    });
    expect(screen.getByLabelText('Serie de los complementos de pago')).toHaveValue('CP');
    expect(screen.getByLabelText('Serie de las notas de crédito')).toHaveValue('E');
  });

  it('warns that the certificate expires soon', async () => {
    serve({ ...WITH_CERTIFICATE, certificate: { ...WITH_CERTIFICATE.certificate!, expiresSoon: true } });
    renderPage();

    expect(await screen.findByText('Vence pronto: tramita uno nuevo en el SAT y súbelo antes de que venza.')).toBeInTheDocument();
    expect(screen.getByText('Listo para facturar')).toBeInTheDocument();
  });
});

describe('Autofactura', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('turns on only while the business can invoice, and then shows the page for its customers', async () => {
    const write = vi.fn((_method: string, _path: string, body: unknown) =>
      Promise.resolve({ ...WITH_CERTIFICATE, selfInvoicing: (body as { on: boolean }).on }));
    serve(WITH_CERTIFICATE, write);
    renderPage();

    const toggle = await screen.findByRole('switch', { name: 'Autofactura activa' });
    expect(toggle).toBeEnabled();
    expect(screen.queryByText(/Dirección para facturar/)).not.toBeInTheDocument();
    await userEvent.click(toggle);

    await waitFor(() => expect(write).toHaveBeenCalledWith('put', '/api/v1/invoicing/settings/self-invoicing', { on: true }));
    expect(toast.success).toHaveBeenCalledWith('Autofactura activa: tus tickets traen el folio y el código para facturar en línea.');
    expect(await screen.findByRole('link', { name: `${window.location.origin}/m/demo/facturar` })).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Autofactura activa' })).toBeChecked();
  });

  it('cannot be turned on while the business cannot invoice', async () => {
    serve(READY_TO_UPLOAD);
    renderPage();

    expect(await screen.findByRole('switch', { name: 'Autofactura activa' })).toBeDisabled();
    expect(screen.getByText('Para activarla, tu negocio debe poder facturar: completa Mi negocio y sube tu CSD.')).toBeInTheDocument();
  });

  it('warns while it is on and the business cannot invoice, and can still be turned off', async () => {
    const write = vi.fn(() => Promise.resolve({ ...READY_TO_UPLOAD, selfInvoicing: false }));
    serve({ ...READY_TO_UPLOAD, selfInvoicing: true }, write);
    renderPage();

    const toggle = await screen.findByRole('switch', { name: 'Autofactura activa' });
    expect(toggle).toBeEnabled();
    expect(screen.getByRole('alert')).toHaveTextContent('tus clientes tampoco pueden hacerlo en línea hasta que lo resuelvas');
    await userEvent.click(toggle);

    await waitFor(() => expect(write).toHaveBeenCalledWith('put', '/api/v1/invoicing/settings/self-invoicing', { on: false }));
    expect(await screen.findByRole('switch', { name: 'Autofactura activa' })).not.toBeChecked();
  });
});

describe('Invoicing settings: the invoice by WhatsApp (Fase 28M)', () => {
  beforeEach(() => vi.resetAllMocks());

  const TEMPLATE = {
    id: 't-1', name: 'Factura con PDF', body: 'Hola {nombre}: tu factura {comprobante} por {monto}. XML: {enlace}', providerTemplateName: 'factura_pdf',
  };

  it('points to connecting the WhatsApp of the business while it is not', async () => {
    serve(WITH_CERTIFICATE);
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Factura por WhatsApp' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Conectar WhatsApp' })).toHaveAttribute('href', '/settings/whatsapp');
    expect(screen.queryByLabelText('Mensaje')).not.toBeInTheDocument();
  });

  it('goes with TienditApp\'s text, which only arrives within the 24 hours, until a template approved in Meta is chosen', async () => {
    let settings: Settings = { ...WITH_CERTIFICATE, whatsAppConnected: true, whatsAppTemplates: [TEMPLATE, { ...TEMPLATE, id: 't-2', name: 'Sin Meta', providerTemplateName: null }] };
    serve(settings, (method, path, body) => {
      expect([method, path, body]).toEqual(['put', '/api/v1/invoicing/settings/whatsapp-template', { templateId: 't-1' }]);
      settings = { ...settings, whatsAppTemplateId: 't-1' };
      return Promise.resolve(settings);
    });
    renderPage();

    const select = await screen.findByLabelText('Mensaje');
    expect(select).toHaveValue('');
    expect(screen.getByText(settings.whatsAppDefaultText)).toBeInTheDocument();
    // Advice that stays, not an alert announced on every visit.
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByText(/^Sin una plantilla aprobada en Meta, WhatsApp solo entrega la factura en las 24 horas/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Campañas › Plantillas' })).toHaveAttribute('href', '/campaigns?tab=templates');

    await userEvent.selectOptions(select, 't-1');
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Tus facturas van por WhatsApp con esa plantilla.'));
    await waitFor(() => expect(screen.getByLabelText('Mensaje')).toHaveValue('t-1'));
    expect(screen.getByText(TEMPLATE.body)).toBeInTheDocument();
    expect(screen.queryByText(/WhatsApp solo/)).not.toBeInTheDocument();
  });

  it('warns that a template not approved in Meta only arrives within the 24 hours', async () => {
    serve({
      ...WITH_CERTIFICATE, whatsAppConnected: true, whatsAppTemplateId: 't-2',
      whatsAppTemplates: [{ ...TEMPLATE, id: 't-2', name: 'Sin Meta', providerTemplateName: null }],
    });
    renderPage();

    expect(await screen.findByText(
      'Esta plantilla no está aprobada en Meta: WhatsApp solo la entrega en las 24 horas desde el último mensaje del cliente.')).toBeInTheDocument();
  });

  it("keeps the accountant's email, and sends them the month only with it (Fase 28O)", async () => {
    const user = userEvent.setup();
    const write = vi.fn((_method: string, _path: string, body: unknown) => {
      const { email, monthly } = body as { email: string | null; monthly: boolean };
      return Promise.resolve({ ...WITH_CERTIFICATE, accountantEmail: email, sendsMonthToAccountant: monthly });
    });
    serve(WITH_CERTIFICATE, write);
    renderPage();

    const address = await screen.findByLabelText('Correo del contador');
    const monthly = screen.getByRole('switch', { name: 'Mandarle cada mes los CFDI del mes anterior' });
    const save = screen.getByRole('button', { name: 'Guardar contador' });
    // Without the email nothing goes on its own, and nothing changed yet.
    expect(monthly).toBeDisabled();
    expect(save).toBeDisabled();

    await user.type(address, 'Contador <contador@despacho.mx>');
    expect(await screen.findByText('Escribe un correo válido, como nombre@ejemplo.com.')).toBeInTheDocument();
    expect(save).toBeDisabled();
    await user.clear(address);
    await user.type(address, ' contador@despacho.mx ');
    await user.click(monthly);
    await user.click(save);

    await waitFor(() => expect(write).toHaveBeenCalledWith('put', '/api/v1/invoicing/settings/accountant',
      { email: 'contador@despacho.mx', monthly: true }));
    expect(toast.success).toHaveBeenCalledWith('El día 1 de cada mes, contador@despacho.mx recibe los CFDI del mes anterior.');

    // Taking the email away turns the monthly email off too.
    await user.clear(address);
    expect(monthly).not.toBeChecked();
    await user.click(save);
    await waitFor(() => expect(write).toHaveBeenLastCalledWith('put', '/api/v1/invoicing/settings/accountant', { email: null, monthly: false }));
    expect(toast.success).toHaveBeenLastCalledWith('Quitaste el correo de tu contador.');
  });
});
