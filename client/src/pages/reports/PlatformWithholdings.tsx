import { useId, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { getApiErrorMessage } from '../../api/errors';
import {
  usePlatformWithholdings,
  useRemovePlatformWithholdingCertificate,
  type PlatformWithholdingCertificate,
  type PlatformWithholdingPeriod,
} from '../../api/platformWithholdings';
import DataTable, { type DataTableColumn } from '../../components/common/DataTable';
import PageTitle from '../../components/common/PageTitle';
import WithholdingUploadModal, { type WithholdingUploadOutcome } from '../../components/reports/WithholdingUploadModal';
import {
  MONTH_NAMES,
  describeCertificatePeriod,
  formatRate,
  monthLabel,
  shortUuid,
  withholdingYears,
} from '../../components/reports/withholdingLabels';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import type { OrderChannel } from '../../types';
import { getAccess } from '../../utils/access';
import { getBusinessToday } from '../../utils/dates';
import { getPlatformName, isDeliveryPlatform, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { DELIVERY_PLATFORM_CHANNELS } from '../../utils/labels';
import { formatMoney, formatSignedMoney } from '../../utils/money';

const PARENT = { label: 'Liquidaciones', to: '/reports/settlements' };

/** The longest reason the server takes to remove a CFDI. */
const MAX_REMOVAL_REASON_LENGTH = 500;

const COLUMNS: DataTableColumn[] = [
  { header: 'Periodo' },
  { header: 'ISR retenido', className: 'text-end' },
  { header: 'IVA retenido', className: 'text-end' },
  { header: 'Total retenido', className: 'text-end' },
];
const COLUMNS_WITH_ACTIONS: DataTableColumn[] = [...COLUMNS, { header: 'Acciones', className: 'text-end' }];

function platformFromQuery(value: string | null): DeliveryPlatform {
  const channel = Number(value) as OrderChannel;
  return isDeliveryPlatform(channel) ? channel : DELIVERY_PLATFORM_CHANNELS[0];
}

/** A difference of the CFDI against the calculation: «Cuadra» at $0, otherwise signed. */
function Difference({ amount }: { amount: number | null | undefined }) {
  if (amount === null || amount === undefined) return <span className="text-muted">—</span>;
  if (amount === 0) return <span className="ta-chip ta-chip-ready">Cuadra</span>;
  return <span className="ta-money fw-semibold">{formatSignedMoney(amount)}</span>;
}

/** A figure of a CFDI: a dash for a month without one. */
function FromCertificate({ period, amount }: { period: PlatformWithholdingPeriod; amount: number | null | undefined }) {
  if (period.certificateCount === 0 || amount === null || amount === undefined) return <span className="text-muted">—</span>;
  return <span className="ta-money">{formatMoney(amount)}</span>;
}

/**
 * Reportes › Liquidaciones › Retenciones (Fase 22J): month by month, the ISR and IVA a platform withheld by its CFDI de
 * retenciones against what TienditApp calculated for the orders it charged, and the CFDI of the year. Owners and admins
 * upload and remove the CFDI.
 */
export default function PlatformWithholdings() {
  const id = useId();
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const access = getAccess(user, capabilities);
  const toast = useToast();
  const { prompt } = useConfirm();
  const [params, setParams] = useSearchParams();
  const today = getBusinessToday();
  const currentYear = Number(today.slice(0, 4));
  const currentMonth = Number(today.slice(5, 7));
  const years = withholdingYears(currentYear);
  const platform = platformFromQuery(params.get('channel'));
  const requestedYear = Number(params.get('year'));
  const year = years.includes(requestedYear) ? requestedYear : currentYear;
  const [uploading, setUploading] = useState(false);
  const withholdings = usePlatformWithholdings(platform, year);
  const removing = useRemovePlatformWithholdingCertificate();
  const data = withholdings.data;
  // Another platform or year keeps the last one on screen while it arrives: what shows is named by what it is, and
  // nothing of it can be removed meanwhile.
  const loadingOther = withholdings.isPlaceholderData;
  const shownName = data && isDeliveryPlatform(data.channel) ? getPlatformName(data.channel) : getPlatformName(platform);
  const shownYear = data?.year ?? year;

  const choose = (patch: { channel?: DeliveryPlatform; year?: number }) => {
    const next = new URLSearchParams(params);
    next.set('channel', String(patch.channel ?? platform));
    next.set('year', String(patch.year ?? year));
    setParams(next, { replace: true });
  };

  const closeUpload = (outcome?: WithholdingUploadOutcome) => {
    setUploading(false);
    // The page follows what went in: the platform chosen in the dialog and the year of its last CFDI.
    if (outcome && (outcome.channel !== platform || outcome.year !== year) && years.includes(outcome.year)) {
      choose({ channel: outcome.channel, year: outcome.year });
    }
  };

  const remove = async (certificate: PlatformWithholdingCertificate) => {
    const period = describeCertificatePeriod(certificate);
    const reason = await prompt({
      title: 'Quitar CFDI',
      message: `El CFDI de ${period} deja de contar en las retenciones. Se queda en la lista como quitado: no se borra.`,
      inputLabel: 'Motivo',
      placeholder: 'Por ejemplo, era de otra plataforma',
      required: true,
      maxLength: MAX_REMOVAL_REASON_LENGTH,
      multiline: true,
      confirmLabel: 'Quitar CFDI',
      danger: true,
    });
    if (reason === null) return;
    try {
      await removing.mutateAsync({ id: certificate.id, reason });
      toast.success(`CFDI de ${period} quitado.`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo quitar el CFDI. Intenta de nuevo.'));
    }
  };

  // A past year shows whole; this one, up to the month of the business.
  const months = (data?.months ?? []).filter((month) => shownYear < currentYear || (month.month ?? 0) <= currentMonth);
  const terms = data && data.isrWithholdingPercent !== null && data.isrWithholdingPercent !== undefined
    ? `${formatRate(data.isrWithholdingPercent)} de ISR y ${formatRate(data.vatWithholdingPercent)} del IVA`
    : null;

  return (
    <>
      <PageTitle title="Retenciones" parent={PARENT} />
      <p className="text-muted">
        Compara el ISR y el IVA que cada plataforma te retuvo, según sus CFDI de retenciones, con lo que TienditApp calculó
        para los pedidos que cobró ella.
      </p>

      <div className="d-flex flex-wrap align-items-end gap-3 mb-3">
        <div>
          <label className="form-label" htmlFor={`${id}-platform`}>Plataforma</label>
          <select
            id={`${id}-platform`}
            className="form-select"
            value={platform}
            onChange={(event) => choose({ channel: Number(event.target.value) as DeliveryPlatform })}
          >
            {DELIVERY_PLATFORM_CHANNELS.map((channel) => <option key={channel} value={channel}>{getPlatformName(channel)}</option>)}
          </select>
        </div>
        <div>
          <label className="form-label" htmlFor={`${id}-year`}>Año</label>
          <select id={`${id}-year`} className="form-select" value={year} onChange={(event) => choose({ year: Number(event.target.value) })}>
            {years.map((option) => <option key={option} value={option}>{option}</option>)}
          </select>
        </div>
        {access.isAdmin && (
          <button type="button" className="btn btn-primary ms-auto" onClick={() => setUploading(true)}>
            Subir CFDI de retenciones
          </button>
        )}
      </div>

      {withholdings.isError && (
        <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
          <span className="me-auto">{getApiErrorMessage(withholdings.error, 'No se pudieron cargar las retenciones.')}</span>
          <button type="button" className="btn btn-sm btn-light" onClick={() => void withholdings.refetch()}>Reintentar</button>
        </div>
      )}
      {(withholdings.isPending || loadingOther) && (
        <p className="text-muted small" aria-busy="true">
          {loadingOther ? `Cargando ${getPlatformName(platform)} de ${year}…` : 'Cargando…'}
        </p>
      )}

      {data && (
        <div aria-busy={loadingOther} style={loadingOther ? { opacity: 0.6 } : undefined}>
          <section className="card" aria-labelledby={`${id}-months`}>
            <div className="card-body">
              <h2 id={`${id}-months`} className="h5 mb-1">{shownName} en {shownYear}</h2>
              <p className="small text-muted">
                {terms === null
                  ? `${shownName} no tiene comisión ni retenciones configuradas: TienditApp no calcula retenciones de sus pedidos.`
                  : (
                    <>
                      TienditApp calcula los pedidos nuevos con {terms}, como en{' '}
                      {access.businessSettings
                        ? <Link to="/settings/business">Configuración › Negocio › Plataformas de reparto</Link>
                        : 'Configuración › Negocio › Plataformas de reparto'}
                      .
                    </>
                  )}
                {' '}Un CFDI de varios meses cuenta completo en su último mes.
              </p>
              <div className="table-responsive">
                <table className="table table-sm align-middle mb-0">
                  <caption className="visually-hidden">Retenciones de {shownName} por mes de {shownYear}</caption>
                  <thead>
                    <tr>
                      <th scope="col">Mes</th>
                      <th scope="col" className="text-end">Pedidos</th>
                      <th scope="col" className="text-end">ISR calculado</th>
                      <th scope="col" className="text-end">ISR del CFDI</th>
                      <th scope="col" className="text-end">Diferencia de ISR</th>
                      <th scope="col" className="text-end">IVA calculado</th>
                      <th scope="col" className="text-end">IVA del CFDI</th>
                      <th scope="col" className="text-end">Diferencia de IVA</th>
                    </tr>
                  </thead>
                  <tbody>
                    {months.map((month) => (
                      <tr key={month.month}>
                        <th scope="row" className="fw-normal">{monthLabel(month.month ?? 0)}</th>
                        <td className="text-end">{month.orderCount}</td>
                        <td className="text-end ta-money">{formatMoney(month.calculatedIsr)}</td>
                        <td className="text-end">
                          <FromCertificate period={month} amount={month.certificateIsr} />
                          {month.certificateCount > 1 && <div className="small text-muted">{month.certificateCount} CFDI</div>}
                          {month.coveredFromMonth !== null && month.coveredFromMonth !== undefined && month.month !== null && month.month !== undefined && (
                            <div className="small text-muted">
                              Compara de {MONTH_NAMES[month.coveredFromMonth - 1]} a {MONTH_NAMES[month.month - 1]}
                            </div>
                          )}
                        </td>
                        <td className="text-end"><Difference amount={month.isrDifference} /></td>
                        <td className="text-end ta-money">{formatMoney(month.calculatedVat)}</td>
                        <td className="text-end"><FromCertificate period={month} amount={month.certificateVat} /></td>
                        <td className="text-end"><Difference amount={month.vatDifference} /></td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot>
                    <tr className="fw-semibold">
                      <th scope="row">Total</th>
                      <td className="text-end">{data.total.orderCount}</td>
                      <td className="text-end ta-money">{formatMoney(data.total.calculatedIsr)}</td>
                      <td className="text-end"><FromCertificate period={data.total} amount={data.total.certificateIsr} /></td>
                      <td className="text-end"><Difference amount={data.total.isrDifference} /></td>
                      <td className="text-end ta-money">{formatMoney(data.total.calculatedVat)}</td>
                      <td className="text-end"><FromCertificate period={data.total} amount={data.total.certificateVat} /></td>
                      <td className="text-end"><Difference amount={data.total.vatDifference} /></td>
                    </tr>
                  </tfoot>
                </table>
              </div>
              <p className="small text-muted mt-2 mb-0">
                Una diferencia positiva es lo que la plataforma retuvo de más; negativa, de menos. Cada mes con CFDI compara
                los meses que cubre, y el total solo los meses con CFDI. Revisa la tasa de cada CFDI abajo.
              </p>
            </div>
          </section>

          <section className="card" aria-labelledby={`${id}-certificates`}>
            <div className="card-body">
              <h2 id={`${id}-certificates`} className="h5 mb-3">CFDI de {shownYear}</h2>
              {data.certificatesTruncated && (
                <div className="alert alert-info">La lista muestra los primeros 200 CFDI; los meses cuentan todos.</div>
              )}
              <DataTable<PlatformWithholdingCertificate>
                data={data.certificates}
                columns={access.isAdmin ? COLUMNS_WITH_ACTIONS : COLUMNS}
                keyExtractor={(certificate) => certificate.id}
                emptyMessage={`Todavía no subes CFDI de retenciones de ${shownName} de ${shownYear}.`}
                searchFilter={(certificate, search) => certificate.uuid.toLowerCase().includes(search.toLowerCase())
                  || describeCertificatePeriod(certificate).includes(search.toLowerCase())}
                searchPlaceholder="Buscar periodo o folio"
                renderRow={(certificate) => {
                  const period = describeCertificatePeriod(certificate);
                  const removed = certificate.removedAt !== null && certificate.removedAt !== undefined;
                  return (
                    <>
                      <td>
                        <div className="fw-semibold">{period.charAt(0).toUpperCase() + period.slice(1)}</div>
                        <div className="small text-muted">{certificate.issuerName} · folio {shortUuid(certificate.uuid)}</div>
                        {certificate.replacesUuid && (
                          <div className="small text-muted">Sustituye al folio {shortUuid(certificate.replacesUuid)}</div>
                        )}
                        {removed && (
                          <div className="small">
                            <span className="ta-chip ta-chip-cancelled me-1">Quitado</span>
                            <span className="text-muted">{certificate.removalReason}</span>
                          </div>
                        )}
                      </td>
                      <td className="text-end">
                        <span className="ta-money">{formatMoney(certificate.isrWithheld)}</span>
                        {certificate.isrRatePercent !== null && certificate.isrRatePercent !== undefined && (
                          <div className="small text-muted">{formatRate(certificate.isrRatePercent)}</div>
                        )}
                      </td>
                      <td className="text-end">
                        <span className="ta-money">{formatMoney(certificate.vatWithheld)}</span>
                        {certificate.vatRatePercent !== null && certificate.vatRatePercent !== undefined && (
                          <div className="small text-muted">{formatRate(certificate.vatRatePercent)} del IVA</div>
                        )}
                      </td>
                      <td className="text-end ta-money">{formatMoney(certificate.totalWithheld)}</td>
                      {access.isAdmin && (
                        <td className="text-end">
                          {!removed && (
                            <button
                              type="button"
                              className="btn btn-light btn-sm"
                              onClick={() => void remove(certificate)}
                              disabled={removing.isPending || loadingOther}
                              aria-label={`Quitar el CFDI de ${period}, folio ${shortUuid(certificate.uuid)}`}
                            >
                              Quitar
                            </button>
                          )}
                        </td>
                      )}
                    </>
                  );
                }}
              />
            </div>
          </section>
        </div>
      )}

      {uploading && <WithholdingUploadModal platform={platform} onHide={closeUpload} />}
    </>
  );
}
