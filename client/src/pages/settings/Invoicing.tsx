import { useState } from 'react';
import { Link } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import { getApiErrorMessage } from '../../api/errors';
import {
  useChangeInvoiceSerie,
  useInvoicingSettings,
  useRemoveInvoicingCertificate,
  useSetSelfInvoicing,
  useUploadInvoicingCertificate,
  type InvoicingSettings,
} from '../../api/invoicing';
import { selfInvoiceUrl } from '../../api/selfInvoiceApi';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { formatDate } from '../../utils/dates';

// The .cer and .key the SAT gives weigh a couple of kilobytes; the API refuses anything past 16 KB.
const MAX_FILE_BYTES = 16 * 1024;

/** A file read as base64, without the data URL prefix. */
async function toBase64(file: File): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

/**
 * Configuración › Facturación (Fase 28A, ADR 0045), for owners and admins: what the invoice of an order needs from the
 * business (its fiscal data in Mi negocio and its CSD, which the platform's PAC keeps), whether its customers invoice their
 * tickets online (Fase 28F, ADR 0050), the serie of its invoices and the one of its payment complements (Fase 28C, ADR 0047).
 */
export default function Invoicing() {
  const settings = useInvoicingSettings();
  const data = settings.data;

  return (
    <>
      <PageTitle title="Facturación" />
      {settings.isPending ? (
        <p className="text-muted" aria-busy="true">Cargando…</p>
      ) : settings.isError || !data ? (
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(settings.error, 'No se pudo cargar la facturación.')}</div>
      ) : !data.available ? (
        <div className="card">
          <div className="card-body">
            <h2 className="h5">La facturación no está disponible por ahora</h2>
            <p className="text-muted mb-0">
              Cuando TienditApp la active, aquí subes el certificado de sello digital de tu negocio para timbrar las facturas de
              tus pedidos. Mientras tanto puedes ver la vista previa de cada factura en Pedidos.
            </p>
          </div>
        </div>
      ) : (
        <div className="row g-3">
          <div className="col-lg-7">
            <StatusCard data={data} />
            <CertificateCard data={data} />
            <SelfInvoicingCard data={data} />
            <SerieCard data={data} />
          </div>
          <div className="col-lg-5">
            <div className="card h-100 mb-0">
              <div className="card-body small">
                <h2 className="h6">Cómo funciona</h2>
                <ul className="mb-0 ps-3">
                  <li>
                    Facturas desde el detalle de un pedido, con los datos fiscales de tu cliente: en una sola exhibición si está pagado
                    por completo, o en parcialidades si se lo llevó fiado o ya se le entregó.
                  </li>
                  <li>
                    Con una factura en parcialidades, TienditApp emite por su cuenta el complemento de pago de cada cobro, también de los
                    que ya recibiste. El SAT lo pide a más tardar el día 5 del mes siguiente al cobro.
                  </li>
                  <li>
                    TienditApp arma el CFDI 4.0 con los importes de la venta y un PAC autorizado por el SAT lo sella con tu CSD y
                    lo timbra. Descargas el XML (la factura) y el PDF.
                  </li>
                  <li>Si el PAC no responde, TienditApp lo vuelve a pedir con la misma solicitud: nunca timbra dos veces.</li>
                  <li>
                    El dueño o un administrador cancela una factura con su motivo. Si el SAT pide que el cliente acepte, la factura
                    queda «Cancelándose» hasta su respuesta.
                  </li>
                  <li>
                    Cada reembolso y cada condonación de un pedido facturado lleva su nota de crédito, que TienditApp emite por su
                    cuenta. El dueño o un administrador la cancela.
                  </li>
                  <li>
                    Con la autofactura activa, tus clientes facturan su compra en línea con el folio y el código de su ticket, hasta
                    el último día del mes en que la pagaron.
                  </li>
                  <li>Las ventas a público en general van en la factura global (Reportes › Factura global).</li>
                  <li>Tu llave privada y su contraseña las guarda el PAC; TienditApp solo guarda el número y la vigencia del CSD.</li>
                </ul>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

function StatusCard({ data }: { data: InvoicingSettings }) {
  return (
    <div className="card">
      <div className="card-body">
        <div className="d-flex align-items-center gap-2 mb-2">
          <h2 className="h5 mb-0">Tu negocio</h2>
          {data.sandbox && <span className="badge text-bg-warning">Modo de pruebas</span>}
          {data.ready
            ? <span className="ta-chip ta-chip-ready">Listo para facturar</span>
            : <span className="ta-chip ta-chip-new">Falta algo</span>}
        </div>
        <dl className="row small mb-2">
          <dt className="col-sm-5">RFC</dt>
          <dd className="col-sm-7 ta-money">{data.rfc || '—'}</dd>
          <dt className="col-sm-5">Razón social</dt>
          <dd className="col-sm-7">{data.legalName || '—'}</dd>
          <dt className="col-sm-5">Régimen fiscal</dt>
          <dd className="col-sm-7">{data.fiscalRegime || '—'}</dd>
          <dt className="col-sm-5">Código postal fiscal</dt>
          <dd className="col-sm-7">{data.postalCode || '—'}</dd>
        </dl>
        {data.missingData.length > 0 && (
          <div className="alert alert-warning small mb-0">
            <div className="fw-semibold mb-1">Completa en <Link to="/settings/business">Configuración › Negocio</Link>:</div>
            <ul className="mb-0">{data.missingData.map((item) => <li key={item}>{item}</li>)}</ul>
          </div>
        )}
        {data.sandbox && (
          <p className="small text-muted mb-0 mt-2">TienditApp timbra en el ambiente de pruebas del PAC: las facturas no tienen validez fiscal.</p>
        )}
      </div>
    </div>
  );
}

function CertificateCard({ data }: { data: InvoicingSettings }) {
  const toast = useToast();
  const { confirm } = useConfirm();
  const upload = useUploadInvoicingCertificate();
  const remove = useRemoveInvoicingCertificate();
  const [certificate, setCertificate] = useState<File | null>(null);
  const [key, setKey] = useState<File | null>(null);
  const [password, setPassword] = useState('');
  const [formKey, setFormKey] = useState(0);
  const certificateInfo = data.certificate;

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!certificate || !key) return;
    if (certificate.size > MAX_FILE_BYTES || key.size > MAX_FILE_BYTES) {
      toast.error('Esos archivos no son un certificado de sello digital: el .cer y el .key pesan unos cuantos kilobytes.');
      return;
    }
    try {
      await upload.mutateAsync({ certificate: await toBase64(certificate), privateKey: await toBase64(key), password });
      toast.success('Certificado de sello digital guardado.');
      setCertificate(null);
      setKey(null);
      setPassword('');
      setFormKey((value) => value + 1);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo guardar el certificado.'));
    } finally {
      setPassword('');
      // The mutation kept the key and its password as its variables: reset, they leave memory with it.
      upload.reset();
    }
  };

  const removeCertificate = async () => {
    const accepted = await confirm({
      title: 'Quitar el certificado',
      message: 'El PAC deja de sellar con él y ya no podrás timbrar ni cancelar facturas hasta subir otro. Las facturas timbradas siguen vigentes.',
      confirmLabel: 'Quitar',
      danger: true,
    });
    if (!accepted) return;
    try {
      await remove.mutateAsync();
      toast.success('Certificado quitado.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo quitar el certificado.'));
    }
  };

  return (
    <div className="card">
      <div className="card-body">
        <h2 className="h5">Certificado de sello digital (CSD)</h2>
        {certificateInfo ? (
          <>
            <dl className="row small mb-2">
              <dt className="col-sm-5">Número</dt>
              <dd className="col-sm-7 ta-money">{certificateInfo.number}</dd>
              <dt className="col-sm-5">RFC</dt>
              <dd className="col-sm-7 ta-money">{certificateInfo.rfc}</dd>
              <dt className="col-sm-5">Vigencia</dt>
              <dd className="col-sm-7">{formatDate(certificateInfo.validFrom)} al {formatDate(certificateInfo.validTo)}</dd>
              <dt className="col-sm-5">Subido el</dt>
              <dd className="col-sm-7">{formatDate(certificateInfo.uploadedAt)}</dd>
            </dl>
            {certificateInfo.expired && (
              <div className="alert alert-danger small">Venció: tramita uno nuevo en el SAT y súbelo aquí.</div>
            )}
            {!certificateInfo.expired && certificateInfo.otherRfc && (
              <div className="alert alert-danger small">No es del RFC de tu negocio: sube el CSD del negocio.</div>
            )}
            {certificateInfo.expiresSoon && !certificateInfo.expired && (
              <div className="alert alert-warning small">Vence pronto: tramita uno nuevo en el SAT y súbelo antes de que venza.</div>
            )}
            <button type="button" className="btn btn-sm btn-outline-danger mb-3" onClick={() => void removeCertificate()} disabled={remove.isPending}>
              Quitar certificado
            </button>
            <h3 className="h6">Reemplazarlo</h3>
          </>
        ) : (
          <p className="small">
            Sube el CSD de tu negocio (no tu e.firma): el archivo .cer, el .key y la contraseña de la llave. Lo tramitas en el SAT
            con tu e.firma (CertiSAT web).
          </p>
        )}
        <form key={formKey} onSubmit={submit} autoComplete="off">
          <div className="row g-2">
            <div className="col-md-6">
              <label className="form-label small mb-1" htmlFor="csd-cer">Certificado (.cer)</label>
              <input id="csd-cer" type="file" accept=".cer" className="form-control"
                onChange={(event) => setCertificate(event.target.files?.[0] ?? null)} />
            </div>
            <div className="col-md-6">
              <label className="form-label small mb-1" htmlFor="csd-key">Llave privada (.key)</label>
              <input id="csd-key" type="file" accept=".key" className="form-control"
                onChange={(event) => setKey(event.target.files?.[0] ?? null)} />
            </div>
            <div className="col-md-6">
              <label className="form-label small mb-1" htmlFor="csd-password">Contraseña de la llave privada</label>
              <input id="csd-password" type="password" className="form-control" required maxLength={100} autoComplete="new-password"
                value={password} onChange={(event) => setPassword(event.target.value)} />
            </div>
          </div>
          <button type="submit" className="btn btn-primary mt-3" disabled={upload.isPending || !certificate || !key || !password}>
            {upload.isPending ? 'Revisando…' : certificateInfo ? 'Reemplazar certificado' : 'Subir certificado'}
          </button>
        </form>
      </div>
    </div>
  );
}

// As the API takes a serie: 1 to 10 letters or digits (spaces around are trimmed).
const SERIE_PATTERN = '\\s*[A-Za-z0-9]{1,10}\\s*';
const SERIE_HINT = 'De 1 a 10 letras o números, sin espacios ni guiones.';

function SelfInvoicingCard({ data }: { data: InvoicingSettings }) {
  const toast = useToast();
  const change = useSetSelfInvoicing();
  const url = selfInvoiceUrl(data.selfInvoicePath);

  const toggle = async (on: boolean) => {
    try {
      await change.mutateAsync(on);
      toast.success(on
        ? 'Autofactura activa: tus tickets traen el folio y el código para facturar en línea.'
        : 'Apagaste la autofactura: solo tu negocio factura. Los tickets impresos vuelven a servir si la activas otra vez.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cambiar la autofactura.'));
    }
  };

  return (
    <div className="card">
      <div className="card-body">
        <h2 className="h5">Autofactura</h2>
        <div className="form-check form-switch">
          <input id="self-invoicing" className="form-check-input" type="checkbox" role="switch" checked={data.selfInvoicing}
            disabled={change.isPending || (!data.selfInvoicing && !data.ready)} onChange={(event) => toggle(event.target.checked)} />
          <label className="form-check-label" htmlFor="self-invoicing">Autofactura activa</label>
        </div>
        <p className="small mt-2 mb-2">
          Tus clientes facturan su compra en línea con el folio y el código que imprime cada ticket, y les llegan el XML y el PDF
          por correo. Pueden hacerlo hasta el último día del mes en que pagaron; después, te la piden a ti.
        </p>
        {!data.selfInvoicing && !data.ready && (
          <p className="small text-muted mb-0">Para activarla, tu negocio debe poder facturar: completa Mi negocio y sube tu CSD.</p>
        )}
        {data.selfInvoicing && (
          <p className="small mb-0">Dirección para facturar: <a href={url} target="_blank" rel="noreferrer">{url}</a></p>
        )}
      </div>
    </div>
  );
}

function SerieCard({ data }: { data: InvoicingSettings }) {
  const toast = useToast();
  const change = useChangeInvoiceSerie();
  const [serie, setSerie] = useState(data.serie);
  const [paymentSerie, setPaymentSerie] = useState(data.paymentSerie);
  const [creditNoteSerie, setCreditNoteSerie] = useState(data.creditNoteSerie);
  const unchanged = serie.trim().toUpperCase() === data.serie && paymentSerie.trim().toUpperCase() === data.paymentSerie
    && creditNoteSerie.trim().toUpperCase() === data.creditNoteSerie;

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      const updated = await change.mutateAsync({
        serie: serie.trim(), paymentSerie: paymentSerie.trim(), creditNoteSerie: creditNoteSerie.trim(),
      });
      setSerie(updated.serie);
      setPaymentSerie(updated.paymentSerie);
      setCreditNoteSerie(updated.creditNoteSerie);
      toast.success(`Tus próximas facturas llevan la serie ${updated.serie}; sus complementos de pago, la ${updated.paymentSerie}, `
        + `y sus notas de crédito, la ${updated.creditNoteSerie}.`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cambiar la serie.'));
    }
  };

  return (
    <div className="card mb-0">
      <div className="card-body">
        <h2 className="h5">Series</h2>
        <form className="row g-2 align-items-end" onSubmit={submit}>
          <div className="col-sm-4">
            <label className="form-label small mb-1" htmlFor="invoice-serie">Serie de las facturas</label>
            <input id="invoice-serie" className="form-control text-uppercase" maxLength={10} required pattern={SERIE_PATTERN}
              title={SERIE_HINT} value={serie} onChange={(event) => setSerie(event.target.value)} />
          </div>
          <div className="col-sm-4">
            <label className="form-label small mb-1" htmlFor="payment-serie">Serie de los complementos de pago</label>
            <input id="payment-serie" className="form-control text-uppercase" maxLength={10} required pattern={SERIE_PATTERN}
              title={SERIE_HINT} value={paymentSerie} onChange={(event) => setPaymentSerie(event.target.value)} />
          </div>
          <div className="col-sm-4">
            <label className="form-label small mb-1" htmlFor="credit-note-serie">Serie de las notas de crédito</label>
            <input id="credit-note-serie" className="form-control text-uppercase" maxLength={10} required pattern={SERIE_PATTERN}
              title={SERIE_HINT} value={creditNoteSerie} onChange={(event) => setCreditNoteSerie(event.target.value)} />
          </div>
          <div className="col-12">
            <button type="submit" className="btn btn-outline-secondary" disabled={change.isPending || unchanged}>
              Guardar series
            </button>
          </div>
        </form>
        <p className="small text-muted mb-0 mt-2">
          Cada serie lleva sus propios folios: F-1, F-2…, P-1, P-2… y NC-1, NC-2… (con la misma serie, comparten folios). Cambiarlas
          no toca lo ya timbrado.
        </p>
      </div>
    </div>
  );
}
