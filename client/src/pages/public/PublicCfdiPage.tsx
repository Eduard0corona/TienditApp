import { useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { publicCfdiApi } from '../../api/publicCfdiApi';
import PageTitle from '../../components/common/PageTitle';
import PhotoOrInitial from '../../components/common/PhotoOrInitial';
import { CfdiEmailDocument } from '../../types';
import { cfdiEmailLabel, saveBlob } from '../../utils/cfdi';
import { formatCalendarDate, formatDate } from '../../utils/dates';
import { formatMoney } from '../../utils/money';
import './PublicCfdiPage.css';

const TITLES: Record<CfdiEmailDocument, string> = {
  [CfdiEmailDocument.Invoice]: 'Factura',
  [CfdiEmailDocument.PaymentComplement]: 'Complemento de pago',
  [CfdiEmailDocument.CreditNote]: 'Nota de crédito',
};

/** «del complemento de pago P-3», «de la factura F-7»: what each download names. */
function ofCfdi(document: CfdiEmailDocument, number: string): string {
  const label = cfdiEmailLabel(document, number);
  return label.startsWith('el ') ? `del ${label.slice(3)}` : `de ${label}`;
}

/**
 * The CFDI a business sent by WhatsApp, opened with the link of its message (/factura#link, Fase 28M, ADR 0058), without an
 * account: which one it is and, while it stands, its XML and PDF. The link lives in the fragment, which the browser never
 * sends to a server; the page sends it in the body of its requests.
 */
export default function PublicCfdiPage() {
  const token = useLocation().hash.replace(/^#/, '');
  const cfdiQuery = useQuery({
    queryKey: ['public', 'cfdi', token],
    queryFn: ({ signal }) => publicCfdiApi.get(token, signal),
    enabled: token.length > 0,
    retry: false,
  });
  const cfdi = cfdiQuery.data;
  const [downloading, setDownloading] = useState<'xml' | 'pdf' | null>(null);
  const [downloadError, setDownloadError] = useState('');

  const download = async (kind: 'xml' | 'pdf') => {
    setDownloading(kind);
    setDownloadError('');
    try {
      const file = await publicCfdiApi.download(token, kind);
      saveBlob(file.blob, file.fileName);
    } catch (err: unknown) {
      setDownloadError(getApiErrorMessage(err, kind === 'pdf' ? 'No se pudo descargar el PDF.' : 'No se pudo descargar el XML.'));
    } finally {
      setDownloading(null);
    }
  };

  return (
    <div className="cl-page">
      <PageTitle title="Tu factura" />

      {!token && (
        <div className="card">
          <div className="card-body text-center py-5">
            <i className="ri-file-text-line fs-1 text-muted d-block mb-2" aria-hidden="true"></i>
            <h2 className="h5">Abre el enlace de tu mensaje</h2>
            <p className="text-muted mb-0">Cuando un negocio te envía tu factura por WhatsApp, el mensaje trae un enlace para descargarla.</p>
          </div>
        </div>
      )}
      {cfdiQuery.isLoading && (
        <div className="text-center py-5"><div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando</span></div></div>
      )}
      {cfdiQuery.error && (
        <div className="alert alert-warning" role="alert">
          {getApiErrorMessage(cfdiQuery.error, 'No se pudo cargar tu factura.')}
        </div>
      )}

      {cfdi && (
        <div className="card mb-0">
          <div className="card-body">
            <div className="d-flex align-items-center gap-3 mb-3">
              <PhotoOrInitial src={cfdi.businessLogoUrl} name={cfdi.businessName} className="pc-logo" loading="eager" />
              <div>
                <h2 className="h5 mb-0">{TITLES[cfdi.document]} {cfdi.number}</h2>
                <small className="text-muted">{cfdi.businessName}</small>
              </div>
            </div>

            <dl className="row small mb-3">
              <dt className="col-5 col-sm-4 text-muted fw-normal">{cfdi.document === CfdiEmailDocument.PaymentComplement ? 'Pago' : 'Total'}</dt>
              <dd className="col-7 col-sm-8 fw-semibold">{formatMoney(cfdi.amount)}</dd>
              {cfdi.issuedOn && (
                <>
                  <dt className="col-5 col-sm-4 text-muted fw-normal">Fecha</dt>
                  <dd className="col-7 col-sm-8">{formatCalendarDate(cfdi.issuedOn)}</dd>
                </>
              )}
              <dt className="col-5 col-sm-4 text-muted fw-normal">Emisor</dt>
              <dd className="col-7 col-sm-8">{cfdi.issuerName} · {cfdi.issuerRfc}</dd>
              <dt className="col-5 col-sm-4 text-muted fw-normal">Receptor</dt>
              <dd className="col-7 col-sm-8">{cfdi.receiverName} · {cfdi.receiverRfc}</dd>
              <dt className="col-5 col-sm-4 text-muted fw-normal">Folio fiscal</dt>
              <dd className="col-7 col-sm-8 text-break mb-0">{cfdi.uuid}</dd>
            </dl>

            {cfdi.stands ? (
              <>
                <div className="d-flex flex-wrap gap-2">
                  <button
                    type="button"
                    className="btn btn-primary"
                    disabled={downloading !== null}
                    aria-label={`Descargar PDF ${ofCfdi(cfdi.document, cfdi.number)}`}
                    onClick={() => void download('pdf')}
                  >
                    <i className="ri-file-pdf-2-line me-1" aria-hidden="true"></i>{downloading === 'pdf' ? 'Descargando…' : 'Descargar PDF'}
                  </button>
                  <button
                    type="button"
                    className="btn btn-light"
                    disabled={downloading !== null}
                    aria-label={`Descargar XML ${ofCfdi(cfdi.document, cfdi.number)}`}
                    onClick={() => void download('xml')}
                  >
                    <i className="ri-file-code-line me-1" aria-hidden="true"></i>{downloading === 'xml' ? 'Descargando…' : 'Descargar XML'}
                  </button>
                </div>
                {downloadError && <div className="alert alert-danger small mt-3 mb-0" role="alert">{downloadError}</div>}
                <p className="small text-muted mt-3 mb-0">
                  El XML es la factura ante el SAT; el PDF es su representación impresa. Este enlace funciona hasta el{' '}
                  {formatDate(cfdi.linkExpiresAt)}.
                </p>
              </>
            ) : (
              <div className="alert alert-warning small mb-0" role="alert">
                Este CFDI ya no está vigente: el negocio pidió cancelarlo. Si lo sustituyó por otro, pídeselo.
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
