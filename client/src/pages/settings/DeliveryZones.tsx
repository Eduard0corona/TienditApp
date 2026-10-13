import { useId, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import {
  useDeleteDeliveryZone,
  useDeliveryZones,
  useSaveDeliveryZone,
  type DeliveryZone,
} from '../../api/deliveryZones';
import { getApiErrorCode, getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { formatMoney, parseMoneyInput } from '../../utils/money';
import { isPostalCode, parsePostalCodes } from '../../utils/postalCodes';

const COLUMNS = [
  { header: 'Zona' },
  { header: 'Códigos postales' },
  { header: 'Envío', className: 'text-end' },
  { header: 'Pedido mínimo', className: 'text-end' },
  { header: 'Envío gratis desde', className: 'text-end' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-end' },
];

/** The same limits the server keeps (DeliveryZone), so the form says what is wrong before saving. */
const MAX_NAME_LENGTH = 60;
const MAX_POSTAL_CODES = 300;
const MAX_FEE = 10_000;
const MAX_ORDER_AMOUNT = 100_000;
/** How many postal codes a row shows before «y N más». */
const POSTAL_CODES_SHOWN = 4;

interface ZoneForm {
  name: string;
  fee: string;
  minimumOrder: string;
  freeDeliveryFrom: string;
  postalCodes: string;
  isActive: boolean;
}

const EMPTY_FORM: ZoneForm = { name: '', fee: '', minimumOrder: '', freeDeliveryFrom: '', postalCodes: '', isActive: true };

function toForm(zone: DeliveryZone): ZoneForm {
  return {
    name: zone.name,
    fee: zone.fee.toFixed(2),
    minimumOrder: zone.minimumOrder > 0 ? zone.minimumOrder.toFixed(2) : '',
    freeDeliveryFrom: zone.freeDeliveryFrom != null ? zone.freeDeliveryFrom.toFixed(2) : '',
    postalCodes: zone.postalCodes.join(', '),
    isActive: zone.isActive,
  };
}

/** «72000, 72005, 72010, 72020 y 3 más». */
function postalCodesSummary(codes: string[]): string {
  if (codes.length <= POSTAL_CODES_SHOWN) return codes.join(', ');
  return `${codes.slice(0, POSTAL_CODES_SHOWN).join(', ')} y ${codes.length - POSTAL_CODES_SHOWN} más`;
}

type Checked =
  | { kind: 'covered'; zone: DeliveryZone }
  | { kind: 'inactive'; zone: DeliveryZone }
  | { kind: 'none' };

/** Which zone has a postal code among the ones listed: what the automatic replies would answer. */
function checkPostalCode(zones: DeliveryZone[], postalCode: string): Checked {
  const zone = zones.find((candidate) => candidate.postalCodes.includes(postalCode));
  if (!zone) return { kind: 'none' };
  return zone.isActive ? { kind: 'covered', zone } : { kind: 'inactive', zone };
}

/** «envío $35.00 · pedido mínimo $150.00 · envío gratis desde $400.00». */
function chargesOf(zone: DeliveryZone): string {
  const parts = [zone.fee === 0 ? 'envío gratis' : `envío ${formatMoney(zone.fee)}`];
  if (zone.minimumOrder > 0) parts.push(`pedido mínimo ${formatMoney(zone.minimumOrder)}`);
  if (zone.freeDeliveryFrom != null) parts.push(`envío gratis desde ${formatMoney(zone.freeDeliveryFrom)}`);
  return parts.join(' · ');
}

/**
 * Configuración › Zonas de entrega (owners and admins): where the business delivers and what it charges for it, by postal
 * code. The automatic replies of WhatsApp already use them to say whether it delivers to a postal code and what it costs;
 * the server keeps a name and a postal code in one zone, refuses a save made over an older revision and records every
 * change.
 */
export default function DeliveryZones() {
  const titleId = useId();
  const toast = useToast();
  const { confirm } = useConfirm();
  // The fee is charged like the prices of the business: with IVA included, or IVA added on top (ADR 0018).
  const pricesIncludeTax = useTenant().tenant?.pricesIncludeTax ?? true;
  const zones = useDeliveryZones();
  const save = useSaveDeliveryZone();
  const remove = useDeleteDeliveryZone();
  const [editing, setEditing] = useState<DeliveryZone | null>(null);
  const [form, setForm] = useState<ZoneForm | null>(null);
  const [formError, setFormError] = useState('');
  const [probe, setProbe] = useState('');
  const parsed = useMemo(() => parsePostalCodes(form?.postalCodes ?? ''), [form?.postalCodes]);
  const list = zones.data ?? [];
  const probed = isPostalCode(probe) ? checkPostalCode(list, probe) : null;
  const anyActive = list.some((zone) => zone.isActive);
  // One change at a time: a second click with the same revision would be refused as someone else's change.
  const busy = save.isPending || remove.isPending;

  const openCreate = () => {
    setEditing(null);
    setForm(EMPTY_FORM);
    setFormError('');
  };

  const openEdit = (zone: DeliveryZone) => {
    setEditing(zone);
    setForm(toForm(zone));
    setFormError('');
  };

  const problemOf = (current: ZoneForm): string | null => {
    const name = current.name.trim();
    if (!name) return 'Escribe el nombre de la zona.';
    if (name.length > MAX_NAME_LENGTH) return 'El nombre de la zona admite hasta 60 caracteres.';
    // The server compares names without telling capitals apart, as here.
    const sameName = name.toLocaleLowerCase('es-MX');
    if (list.some((zone) => zone.id !== editing?.id && zone.name.toLocaleLowerCase('es-MX') === sameName)) {
      return `Ya tienes una zona llamada «${name}». Ponle otro nombre.`;
    }
    const fee = parseMoneyInput(current.fee);
    if (fee === null || fee > MAX_FEE) return 'El costo de envío va de $0 a $10,000, con dos decimales.';
    const minimum = current.minimumOrder.trim() ? parseMoneyInput(current.minimumOrder) : 0;
    if (minimum === null || minimum > MAX_ORDER_AMOUNT) return 'El pedido mínimo va de $0 a $100,000, con dos decimales.';
    if (current.freeDeliveryFrom.trim()) {
      const freeFrom = parseMoneyInput(current.freeDeliveryFrom);
      if (freeFrom === null || freeFrom <= 0 || freeFrom > MAX_ORDER_AMOUNT) {
        return '«Envío gratis desde» va de $0.01 a $100,000, con dos decimales.';
      }
      if (fee === 0) return 'El envío ya es gratis: deja vacío «Envío gratis desde».';
    }
    if (parsed.invalid.length > 0) return `«${parsed.invalid[0]}» no es un código postal: lleva 5 dígitos.`;
    if (parsed.codes.length === 0) return 'Agrega al menos un código postal.';
    if (parsed.codes.length > MAX_POSTAL_CODES) return 'Una zona lleva hasta 300 códigos postales.';
    return null;
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!form) return;
    const problem = problemOf(form);
    if (problem) {
      setFormError(problem);
      return;
    }
    setFormError('');
    try {
      await save.mutateAsync({
        id: editing?.id,
        body: {
          name: form.name.trim(),
          fee: parseMoneyInput(form.fee) ?? 0,
          minimumOrder: form.minimumOrder.trim() ? parseMoneyInput(form.minimumOrder) ?? 0 : 0,
          freeDeliveryFrom: form.freeDeliveryFrom.trim() ? parseMoneyInput(form.freeDeliveryFrom) : null,
          postalCodes: parsed.codes,
          isActive: form.isActive,
          revision: editing?.revision ?? 0,
        },
      });
      toast.success(editing ? 'Zona guardada.' : 'Zona agregada.');
      setForm(null);
    } catch (err: unknown) {
      const code = getApiErrorCode(err);
      // Saved or deleted by someone else meanwhile: saving again would fail the same way. The list is read again and
      // the zone opens from it, as it is now.
      if (editing && (code === 'concurrency_conflict' || code === 'not_found')) {
        setForm(null);
        toast.error(code === 'not_found'
          ? `Alguien más eliminó «${editing.name}».`
          : `Alguien más cambió «${editing.name}» al mismo tiempo. Ábrela otra vez para ver lo que quedó guardado.`);
        return;
      }
      setFormError(getApiErrorMessage(err, 'No se pudo guardar la zona.'));
    }
  };

  const toggleActive = async (zone: DeliveryZone) => {
    if (zone.isActive) {
      const accepted = await confirm({
        title: `Desactivar ${zone.name}`,
        message: 'Tus respuestas automáticas dirán que no llegas a sus códigos postales. Los conserva para activarla después.',
        confirmLabel: 'Desactivar',
      });
      if (!accepted) return;
    }
    try {
      await save.mutateAsync({
        id: zone.id,
        body: {
          name: zone.name,
          fee: zone.fee,
          minimumOrder: zone.minimumOrder,
          freeDeliveryFrom: zone.freeDeliveryFrom ?? null,
          postalCodes: zone.postalCodes,
          isActive: !zone.isActive,
          revision: zone.revision,
        },
      });
      toast.success(zone.isActive ? 'Zona desactivada.' : 'Zona activada.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cambiar el estado de la zona.'));
    }
  };

  const deleteZone = async (zone: DeliveryZone) => {
    const accepted = await confirm({
      title: `Eliminar ${zone.name}`,
      message: 'Sus códigos postales quedan libres para otra zona. Tus respuestas automáticas dirán que no llegas a ellos.',
      confirmLabel: 'Eliminar',
    });
    if (!accepted) return;
    try {
      await remove.mutateAsync({ id: zone.id, revision: zone.revision });
      toast.success('Zona eliminada.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo eliminar la zona.'));
    }
  };

  return (
    <>
      <PageTitle title="Zonas de entrega" />

      <div className="card">
        <div className="card-body">
          <p className="text-muted mb-3">
            Los códigos postales a los que entregas y lo que cobras por llevar. Tus respuestas automáticas de WhatsApp los usan
            para decir si llegas y cuánto cuesta el envío, y el menú en línea y Vender para cobrarlo en el pedido a domicilio.{' '}
            {pricesIncludeTax
              ? 'Los montos son lo que paga tu cliente, con IVA incluido.'
              : 'El envío va como tus precios, antes de IVA: el pedido le suma el IVA. El pedido mínimo y el envío gratis se comparan con lo que paga tu cliente en productos, con IVA.'}
          </p>

          {list.length > 0 && (
            <div className="mb-3">
              <label className="form-label" htmlFor="delivery-zone-probe">Probar un código postal</label>
              <input
                id="delivery-zone-probe"
                className="form-control"
                style={{ maxWidth: '12rem' }}
                inputMode="numeric"
                maxLength={5}
                placeholder="72000"
                value={probe}
                onChange={(event) => setProbe(event.target.value.replace(/\D/g, ''))}
                aria-describedby="delivery-zone-probe-result"
              />
              <div id="delivery-zone-probe-result" className="form-text" role="status">
                {probed?.kind === 'covered' && `Sí llegas al ${probe} con «${probed.zone.name}»: ${chargesOf(probed.zone)}.`}
                {probed?.kind === 'inactive' && `El ${probe} está en «${probed.zone.name}», que está desactivada: no llegas por ahora.`}
                {probed?.kind === 'none' && `Ninguna zona llega al ${probe}.`}
              </div>
              {!anyActive && (
                <div className="form-text">
                  Ninguna zona está activa: mientras tanto, tus respuestas automáticas no hablan de entregas.
                </div>
              )}
            </div>
          )}

          {zones.isError && (
            <div className="alert alert-danger">{getApiErrorMessage(zones.error, 'No se pudieron cargar las zonas de entrega.')}</div>
          )}

          <DataTable
            data={list}
            columns={COLUMNS}
            keyExtractor={(item) => item.id}
            loading={zones.isLoading}
            emptyMessage="Todavía no tienes zonas de entrega. Agrega una con los códigos postales a los que llegas y su costo de envío."
            searchPlaceholder="Nombre o código postal"
            toolbarEnd={(
              <button type="button" className="btn btn-primary btn-sm" onClick={openCreate}>
                <i className="ri-add-line me-1" aria-hidden="true"></i>Nueva zona
              </button>
            )}
            searchFilter={(item, search) =>
              item.name.toLowerCase().includes(search) || item.postalCodes.some((code) => code.startsWith(search))
            }
            renderRow={(item) => (
              <>
                <td className="fw-semibold">{item.name}</td>
                <td>
                  <div>{item.postalCodes.length === 1 ? '1 código postal' : `${item.postalCodes.length} códigos postales`}</div>
                  <div className="text-muted small">{postalCodesSummary(item.postalCodes)}</div>
                </td>
                <td className="text-end">{item.fee === 0 ? 'Gratis' : formatMoney(item.fee)}</td>
                <td className="text-end">{item.minimumOrder > 0 ? formatMoney(item.minimumOrder) : 'Sin mínimo'}</td>
                <td className="text-end">{item.freeDeliveryFrom != null ? formatMoney(item.freeDeliveryFrom) : '—'}</td>
                <td className="text-center">
                  <span className={`badge ${item.isActive ? 'text-bg-success' : 'text-bg-secondary'}`}>
                    {item.isActive ? 'Activa' : 'Inactiva'}
                  </span>
                </td>
                <td className="text-end text-nowrap">
                  <button type="button" className="btn btn-sm btn-light me-1" onClick={() => openEdit(item)}
                    disabled={busy} aria-label={`Editar ${item.name}`}>
                    Editar
                  </button>
                  <button type="button" className="btn btn-sm btn-light me-1" onClick={() => void toggleActive(item)}
                    disabled={busy} aria-label={item.isActive ? `Desactivar ${item.name}` : `Activar ${item.name}`}>
                    {item.isActive ? 'Desactivar' : 'Activar'}
                  </button>
                  <button type="button" className="btn btn-sm btn-light" onClick={() => void deleteZone(item)}
                    disabled={busy} aria-label={`Eliminar ${item.name}`}>
                    Eliminar
                  </button>
                </td>
              </>
            )}
          />
        </div>
      </div>

      <Modal show={form !== null} onHide={() => setForm(null)} size="lg" centered aria-labelledby={titleId}>
        {form && (
          <form onSubmit={(event) => void submit(event)} noValidate>
            <Modal.Header closeButton>
              <Modal.Title id={titleId}>{editing ? `Editar ${editing.name}` : 'Nueva zona de entrega'}</Modal.Title>
            </Modal.Header>
            <Modal.Body>
              <div className="row g-2">
                <div className="col-12">
                  <label className="form-label" htmlFor="delivery-zone-name">Nombre</label>
                  <input id="delivery-zone-name" className="form-control" maxLength={MAX_NAME_LENGTH} required
                    placeholder="Ej. Centro" value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} />
                </div>
                <div className="col-md-4">
                  <label className="form-label" htmlFor="delivery-zone-fee">Costo de envío</label>
                  <input id="delivery-zone-fee" className="form-control ta-money" inputMode="decimal" placeholder="$0.00" required
                    aria-describedby="delivery-zone-fee-hint"
                    value={form.fee} onChange={(event) => setForm({ ...form, fee: event.target.value })} />
                  <div id="delivery-zone-fee-hint" className="form-text">$0 si el envío es gratis.</div>
                </div>
                <div className="col-md-4">
                  <label className="form-label" htmlFor="delivery-zone-minimum">Pedido mínimo (opcional)</label>
                  <input id="delivery-zone-minimum" className="form-control ta-money" inputMode="decimal" placeholder="Sin mínimo"
                    value={form.minimumOrder} onChange={(event) => setForm({ ...form, minimumOrder: event.target.value })} />
                </div>
                <div className="col-md-4">
                  <label className="form-label" htmlFor="delivery-zone-free-from">Envío gratis desde (opcional)</label>
                  <input id="delivery-zone-free-from" className="form-control ta-money" inputMode="decimal" placeholder="Nunca"
                    value={form.freeDeliveryFrom} onChange={(event) => setForm({ ...form, freeDeliveryFrom: event.target.value })} />
                </div>
                <div className="col-12">
                  <label className="form-label" htmlFor="delivery-zone-postal-codes">Códigos postales</label>
                  <textarea id="delivery-zone-postal-codes" className="form-control" rows={3}
                    placeholder="72000, 72005, 72010" aria-describedby="delivery-zone-postal-codes-hint"
                    value={form.postalCodes} onChange={(event) => setForm({ ...form, postalCodes: event.target.value })} />
                  <div id="delivery-zone-postal-codes-hint" className="form-text">
                    Sepáralos con comas, espacios o renglones. {parsed.codes.length === 1
                      ? '1 código postal.'
                      : `${parsed.codes.length} códigos postales.`}
                    {parsed.invalid.length > 0 && <span className="text-danger"> Revisa: {parsed.invalid.slice(0, 3).join(', ')}.</span>}
                  </div>
                </div>
                <div className="col-12">
                  <div className="form-check form-switch mt-1">
                    <input id="delivery-zone-active" type="checkbox" className="form-check-input" role="switch" checked={form.isActive}
                      onChange={(event) => setForm({ ...form, isActive: event.target.checked })} />
                    <label className="form-check-label" htmlFor="delivery-zone-active">Activa: tus respuestas automáticas la usan</label>
                  </div>
                </div>
              </div>
              {formError && <div className="alert alert-danger mt-3 mb-0">{formError}</div>}
            </Modal.Body>
            <Modal.Footer>
              <button type="button" className="btn btn-light" onClick={() => setForm(null)}>Cancelar</button>
              <button type="submit" className="btn btn-primary" disabled={save.isPending}>
                {save.isPending ? 'Guardando…' : editing ? 'Guardar cambios' : 'Agregar zona'}
              </button>
            </Modal.Footer>
          </form>
        )}
      </Modal>
    </>
  );
}
