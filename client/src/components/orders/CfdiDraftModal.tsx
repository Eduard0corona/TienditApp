import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useCfdiDraft, type CfdiReceiver } from '../../api/queries';
import type { Schemas } from '../../api/typedClient';
import { formatMoney } from '../../utils/money';

interface CfdiDraftModalProps {
  orderId: string;
  orderNumber: string;
  onClose: () => void;
}

// c_RegimenFiscal and c_UsoCFDI entries a counter sale usually needs (the API accepts the whole catalog).
const REGIMES: Array<[string, string]> = [
  ['601', 'General de Ley Personas Morales'],
  ['603', 'Personas Morales con Fines no Lucrativos'],
  ['605', 'Sueldos y Salarios'],
  ['606', 'Arrendamiento'],
  ['612', 'Personas Físicas con Actividades Empresariales y Profesionales'],
  ['616', 'Sin obligaciones fiscales'],
  ['621', 'Incorporación Fiscal'],
  ['625', 'Actividades con Plataformas Tecnológicas'],
  ['626', 'Régimen Simplificado de Confianza'],
];

const USES: Array<[string, string]> = [
  ['G01', 'Adquisición de mercancías'],
  ['G03', 'Gastos en general'],
  ['D01', 'Honorarios médicos, dentales y gastos hospitalarios'],
  ['S01', 'Sin efectos fiscales'],
];

const PAYMENT_FORMS: Record<string, string> = {
  '01': 'Efectivo',
  '03': 'Transferencia',
  '04': 'Tarjeta',
  '06': 'Dinero electrónico',
  '99': 'Por definir',
};

const EMPTY_RECEIVER: CfdiReceiver = { rfc: '', name: '', postalCode: '', fiscalRegime: '616', cfdiUse: 'G03' };

type Transfer = Schemas['CfdiTransferDto'];

function formatTransfer(transfer: Transfer | null | undefined): string {
  if (!transfer) return 'No objeto';
  if (transfer.factorType === 'Exento') return 'Exento';
  return `${formatMoney(transfer.amount ?? 0)} (${((transfer.rateOrQuota ?? 0) * 100).toFixed(0)} %)`;
}

/**
 * What the invoice of an order would say: concepts with their SAT codes, IVA per rate and what is still
 * missing to stamp it. Nothing is sent to the SAT.
 */
export default function CfdiDraftModal({ orderId, orderNumber, onClose }: CfdiDraftModalProps) {
  const [form, setForm] = useState<CfdiReceiver>(EMPTY_RECEIVER);
  const [receiver, setReceiver] = useState<CfdiReceiver | undefined>(undefined);
  const [showReceiver, setShowReceiver] = useState(false);
  const draft = useCfdiDraft(orderId, receiver);
  const cfdi = draft.data;

  const update = (field: keyof CfdiReceiver) => (event: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setForm((current) => ({ ...current, [field]: event.target.value }));

  const applyReceiver = (event: React.FormEvent) => {
    event.preventDefault();
    setReceiver({
      rfc: form.rfc.trim().toUpperCase(),
      name: form.name.trim(),
      postalCode: form.postalCode.trim(),
      fiscalRegime: form.fiscalRegime,
      cfdiUse: form.cfdiUse,
    });
  };

  return (
    <Modal show onHide={onClose} size="xl" centered fullscreen="md-down">
      <Modal.Header closeButton>
        <Modal.Title as="h2" className="h5">Factura de {orderNumber} (vista previa)</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        <p className="text-muted small">
          Así quedaría el CFDI 4.0 de esta venta. Es una vista previa: todavía no se timbra ni se envía al SAT.
        </p>

        <button
          type="button"
          className="btn btn-link btn-sm px-0 mb-2"
          onClick={() => setShowReceiver((value) => !value)}
          aria-expanded={showReceiver}
        >
          <i className={`${showReceiver ? 'ri-arrow-up-s-line' : 'ri-user-line'} me-1`} aria-hidden="true"></i>
          {receiver ? `Factura para ${receiver.rfc}` : 'Facturar a un cliente con RFC'}
        </button>
        {showReceiver && (
          <form className="border rounded p-2 mb-3" onSubmit={applyReceiver}>
            <div className="row g-2">
              <div className="col-md-3">
                <label className="form-label small mb-1" htmlFor="cfdi-rfc">RFC</label>
                <input id="cfdi-rfc" className="form-control text-uppercase" maxLength={13} required
                  value={form.rfc} onChange={update('rfc')} />
              </div>
              <div className="col-md-5">
                <label className="form-label small mb-1" htmlFor="cfdi-name">Nombre o razón social</label>
                <input id="cfdi-name" className="form-control" maxLength={254} required
                  value={form.name} onChange={update('name')} />
              </div>
              <div className="col-md-4">
                <label className="form-label small mb-1" htmlFor="cfdi-zip">Código postal fiscal</label>
                <input id="cfdi-zip" className="form-control" inputMode="numeric" maxLength={5} required
                  value={form.postalCode} onChange={update('postalCode')} />
              </div>
              <div className="col-md-6">
                <label className="form-label small mb-1" htmlFor="cfdi-regime">Régimen fiscal</label>
                <select id="cfdi-regime" className="form-select" value={form.fiscalRegime} onChange={update('fiscalRegime')}>
                  {REGIMES.map(([code, label]) => <option key={code} value={code}>{code} · {label}</option>)}
                </select>
              </div>
              <div className="col-md-6">
                <label className="form-label small mb-1" htmlFor="cfdi-use">Uso del CFDI</label>
                <select id="cfdi-use" className="form-select" value={form.cfdiUse} onChange={update('cfdiUse')}>
                  {USES.map(([code, label]) => <option key={code} value={code}>{code} · {label}</option>)}
                </select>
              </div>
            </div>
            <div className="d-flex gap-2 mt-2">
              <button type="submit" className="btn btn-sm btn-primary">Ver con estos datos</button>
              {receiver && (
                <button type="button" className="btn btn-sm btn-light" onClick={() => setReceiver(undefined)}>
                  Público en general
                </button>
              )}
            </div>
          </form>
        )}

        {draft.isPending && <p className="text-muted" aria-busy="true">Calculando…</p>}
        {draft.isError && (
          <div className="alert alert-danger">{getApiErrorMessage(draft.error, 'No se pudo calcular la factura.')}</div>
        )}

        {cfdi && (
          <>
            {cfdi.blockers.length > 0 ? (
              <div className="alert alert-danger">
                <div className="fw-semibold mb-1">Falta para poder timbrarla:</div>
                <ul className="mb-0">{cfdi.blockers.map((item) => <li key={item}>{item}</li>)}</ul>
              </div>
            ) : (
              <div className="alert alert-success py-2">
                <i className="ri-checkbox-circle-line me-1" aria-hidden="true"></i>Lista para timbrar.
              </div>
            )}
            {cfdi.warnings.length > 0 && (
              <div className="alert alert-warning">
                <ul className="mb-0">{cfdi.warnings.map((item) => <li key={item}>{item}</li>)}</ul>
              </div>
            )}

            <div className="row g-3 mb-3 small">
              <div className="col-md-6">
                <div className="fw-semibold">Emisor</div>
                <div>{cfdi.issuer.name || <span className="text-danger">Sin razón social</span>}</div>
                <div>RFC {cfdi.issuer.rfc || '—'} · Régimen {cfdi.issuer.fiscalRegime || '—'}</div>
                <div>Lugar de expedición {cfdi.issuedAtPostalCode || '—'}</div>
              </div>
              <div className="col-md-6">
                <div className="fw-semibold">Receptor</div>
                <div>{cfdi.receiver.name}</div>
                <div>RFC {cfdi.receiver.rfc} · Régimen {cfdi.receiver.fiscalRegime} · Uso {cfdi.receiver.cfdiUse}</div>
                <div>
                  Pago {cfdi.paymentMethod} · Forma {cfdi.paymentForm} {PAYMENT_FORMS[cfdi.paymentForm] ? `(${PAYMENT_FORMS[cfdi.paymentForm]})` : ''}
                </div>
              </div>
            </div>

            <div className="table-responsive">
              <table className="table table-sm align-middle">
                <thead>
                  <tr>
                    <th>Clave</th>
                    <th>Unidad</th>
                    <th>Descripción</th>
                    <th className="text-end">Cantidad</th>
                    <th className="text-end">Valor unitario</th>
                    <th className="text-end">Importe</th>
                    <th className="text-end">IVA</th>
                  </tr>
                </thead>
                <tbody>
                  {cfdi.concepts.map((concept) => (
                    <tr key={concept.orderItemId}>
                      <td className="ta-money">{concept.productCode}</td>
                      <td>{concept.unitCode}</td>
                      <td>{concept.description}</td>
                      <td className="text-end ta-money">{concept.quantity.toLocaleString('es-MX', { maximumFractionDigits: 3 })}</td>
                      <td className="text-end ta-money">
                        {concept.unitValue.toLocaleString('es-MX', { minimumFractionDigits: 2, maximumFractionDigits: 6 })}
                      </td>
                      <td className="text-end ta-money">{formatMoney(concept.amount)}</td>
                      <td className="text-end ta-money small">{formatTransfer(concept.transfer as Transfer | null)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="ms-auto" style={{ maxWidth: '22rem' }}>
              <div className="d-flex justify-content-between"><span>Subtotal</span><span className="ta-money">{formatMoney(cfdi.subTotal)}</span></div>
              {cfdi.transfers.filter((transfer) => transfer.factorType !== 'Exento').map((transfer) => (
                <div className="d-flex justify-content-between small text-muted" key={`${transfer.factorType}-${transfer.rateOrQuota}`}>
                  <span>IVA {((transfer.rateOrQuota ?? 0) * 100).toFixed(0)} % sobre {formatMoney(transfer.base)}</span>
                  <span className="ta-money">{formatMoney(transfer.amount ?? 0)}</span>
                </div>
              ))}
              <div className="d-flex justify-content-between fw-bold border-top pt-1">
                <span>Total</span><span className="ta-money">{formatMoney(cfdi.total)}</span>
              </div>
            </div>
          </>
        )}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose}>Cerrar</button>
      </Modal.Footer>
    </Modal>
  );
}
