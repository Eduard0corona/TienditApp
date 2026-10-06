import { useRef, useState } from 'react';
import { Offcanvas } from 'react-bootstrap';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { purchaseOrdersApi } from '../../api/purchaseOrdersApi';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import type { PurchaseOrderDto, PurchaseOrderLineDto, ReceivePurchaseOrderLineRequest } from '../../types';
import { PurchaseOrderStatus } from '../../types';
import { formatDate, formatDateTime } from '../../utils/dates';
import { getPurchaseOrderStatusBadge, getPurchaseOrderStatusLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import { buildPurchaseOrderMessage } from '../../utils/purchaseOrderMessage';
import { formatQuantity, roundQuantity } from '../../utils/units';
import { newUuid } from '../../utils/uuid';
import WhatsAppLink from '../common/WhatsAppLink';

export type PurchaseOrderDetailMode = 'view' | 'receive';

interface PurchaseOrderDetailProps {
  orderId: string | null;
  mode: PurchaseOrderDetailMode;
  onModeChange: (mode: PurchaseOrderDetailMode) => void;
  onHide: () => void;
  /** The order changed (sent, received or cancelled): the list reloads. */
  onChanged: () => void;
}

interface ReceptionInput {
  quantity?: string;
  unitCost?: string;
}

const parseNumber = (text: string) => {
  const value = Number(text.trim().replace(',', '.'));
  return text.trim() !== '' && Number.isFinite(value) ? value : null;
};

/** What came in and what is still missing from a purchase order, and the actions its status allows. */
export default function PurchaseOrderDetail({ orderId, mode, onModeChange, onHide, onChanged }: PurchaseOrderDetailProps) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const { confirm, prompt } = useConfirm();
  const { tenant } = useTenant();
  // Only what the person changed: the rest of the lines receive their pending quantity at the ordered cost.
  const [reception, setReception] = useState<Record<string, ReceptionInput>>({});
  const [busy, setBusy] = useState(false);
  // One key per reception: sending it again (a retry after an error or a lost answer) does not receive twice.
  const receptionKey = useRef(newUuid());

  const orderQuery = useQuery({
    queryKey: ['purchase-orders', 'detail', orderId],
    queryFn: async () => (await purchaseOrdersApi.getById(orderId!)).data,
    enabled: !!orderId,
  });
  const order = orderQuery.data;
  const pendingLines = order?.lines.filter((line) => line.pendingQuantity > 0) ?? [];
  const canReceive = order?.status === PurchaseOrderStatus.Sent || order?.status === PurchaseOrderStatus.PartiallyReceived;
  const canCancel = order?.status === PurchaseOrderStatus.Draft || order?.status === PurchaseOrderStatus.Sent;
  // While something is still to arrive, the order (or what is missing) can go to the supplier by WhatsApp.
  const canMessage = order?.status === PurchaseOrderStatus.Draft || canReceive;
  const receiving = mode === 'receive' && canReceive;

  const quantityText = (line: PurchaseOrderLineDto) => reception[line.id]?.quantity ?? String(line.pendingQuantity);
  const costText = (line: PurchaseOrderLineDto) => reception[line.id]?.unitCost ?? String(line.unitCost);
  const setInput = (lineId: string, patch: ReceptionInput) =>
    setReception((current) => ({ ...current, [lineId]: { ...current[lineId], ...patch } }));

  const refresh = async (updated: PurchaseOrderDto) => {
    queryClient.setQueryData(['purchase-orders', 'detail', updated.id], updated);
    onChanged();
  };

  const run = async (action: () => Promise<PurchaseOrderDto>, success: string, failure: string) => {
    setBusy(true);
    try {
      await refresh(await action());
      toast.success(success);
      return true;
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, failure));
      return false;
    } finally {
      setBusy(false);
    }
  };

  const send = () => order && run(
    async () => (await purchaseOrdersApi.send(order.id)).data,
    `${order.orderNumber} quedó enviada al proveedor.`,
    'No se pudo enviar la compra.',
  );

  const cancel = async () => {
    if (!order) return;
    const accepted = await confirm({
      title: 'Cancelar compra',
      message: `${order.orderNumber} de ${order.supplierName} ya no se recibirá. No cambia existencias.`,
      confirmLabel: 'Cancelar compra',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    await run(
      async () => (await purchaseOrdersApi.cancel(order.id)).data,
      `${order.orderNumber} cancelada.`,
      'No se pudo cancelar la compra.',
    );
  };

  const closeWithoutRest = async () => {
    if (!order) return;
    const reason = await prompt({
      title: 'Cerrar sin esperar el resto',
      message: `${order.orderNumber} se queda con lo que llegó. Lo que falta ya no se espera; las existencias no cambian.`,
      inputLabel: 'Motivo (opcional)',
      placeholder: 'Ej. el proveedor ya no lo surte',
      maxLength: 500,
      confirmLabel: 'Cerrar compra',
      cancelLabel: 'Volver',
    });
    if (reason === null) return;
    await run(
      async () => (await purchaseOrdersApi.close(order.id, reason || undefined)).data,
      `${order.orderNumber} quedó cerrada con lo que llegó.`,
      'No se pudo cerrar la compra.',
    );
  };

  const receive = async () => {
    if (!order) return;
    const lines: ReceivePurchaseOrderLineRequest[] = [];
    for (const line of pendingLines) {
      const quantity = parseNumber(quantityText(line));
      const unitCost = parseNumber(costText(line));
      if (quantity === null || quantity < 0 || roundQuantity(quantity) !== quantity) {
        toast.error(`${line.supplyItemName}: escribe una cantidad válida (hasta 3 decimales).`);
        return;
      }
      if (quantity > line.pendingQuantity) {
        toast.error(`${line.supplyItemName}: solo faltan ${formatQuantity(line.pendingQuantity, line.unit)}.`);
        return;
      }
      if (unitCost === null || unitCost < 0) {
        toast.error(`${line.supplyItemName}: escribe un costo válido.`);
        return;
      }
      if (quantity === 0) continue;
      lines.push({
        purchaseOrderLineId: line.id,
        quantityReceived: quantity,
        unitCost: unitCost !== line.unitCost ? unitCost : undefined,
      });
    }
    if (lines.length === 0) {
      toast.error('Escribe cuánto llegó de al menos un insumo.');
      return;
    }

    const received = await run(
      async () => (await purchaseOrdersApi.receive(order.id, { lines, idempotencyKey: receptionKey.current })).data,
      'Recepción registrada: las existencias ya se actualizaron.',
      'No se pudo registrar la recepción.',
    );
    if (received) {
      receptionKey.current = newUuid();
      setReception({});
      onModeChange('view');
    }
  };

  return (
    <Offcanvas show={orderId !== null} onHide={onHide} placement="end" style={{ width: 'min(640px, 100vw)' }}>
      <Offcanvas.Header closeButton>
        <Offcanvas.Title as="h2" className="h5 mb-0">
          {order ? order.orderNumber : 'Compra'}
          {order && (
            <span className="d-block small text-muted fw-normal">
              {order.supplierName} · <span className={`badge ${getPurchaseOrderStatusBadge(order.status)}`}>{getPurchaseOrderStatusLabel(order.status)}</span>
            </span>
          )}
        </Offcanvas.Title>
      </Offcanvas.Header>
      <Offcanvas.Body>
        {orderQuery.isLoading && (
          <div className="text-center py-4"><div className="spinner-border text-primary" role="status" /></div>
        )}
        {orderQuery.error && (
          <div className="alert alert-danger">{getApiErrorMessage(orderQuery.error, 'No se pudo cargar la compra.')}</div>
        )}
        {order && (
          <>
            <dl className="row small mb-3">
              <dt className="col-5 text-muted fw-normal">Pedida</dt>
              <dd className="col-7 mb-1">{formatDateTime(order.orderedAt)}</dd>
              <dt className="col-5 text-muted fw-normal">Entrega esperada</dt>
              <dd className="col-7 mb-1">{formatDate(order.expectedDeliveryAt)}</dd>
              <dt className="col-5 text-muted fw-normal">Última recepción</dt>
              <dd className="col-7 mb-1">{formatDateTime(order.receivedAt)}</dd>
              {order.referenceNumber && (
                <>
                  <dt className="col-5 text-muted fw-normal">Referencia</dt>
                  <dd className="col-7 mb-1">{order.referenceNumber}</dd>
                </>
              )}
              {order.notes && (
                <>
                  <dt className="col-5 text-muted fw-normal">Notas</dt>
                  <dd className="col-7 mb-1">{order.notes}</dd>
                </>
              )}
              {order.status === PurchaseOrderStatus.Closed && (
                <>
                  <dt className="col-5 text-muted fw-normal">Cerrada sin el resto</dt>
                  <dd className="col-7 mb-1">
                    {formatDateTime(order.closedAt)}
                    {order.closeReason && <span className="d-block text-muted">{order.closeReason}</span>}
                  </dd>
                </>
              )}
            </dl>

            {receiving ? (
              <>
                <p className="small text-muted">
                  Escribe lo que llegó de cada insumo y el costo de la factura si cambió. Lo que no llegue queda pendiente.
                </p>
                <div className="table-responsive">
                  <table className="table table-sm align-middle">
                    <thead>
                      <tr>
                        <th>Insumo</th>
                        <th className="text-end">Falta</th>
                        <th style={{ width: 110 }}>Llegó</th>
                        <th style={{ width: 120 }}>Costo unitario</th>
                      </tr>
                    </thead>
                    <tbody>
                      {pendingLines.map((line) => (
                        <tr key={line.id}>
                          <td>
                            {line.supplyItemName}
                            <span className="d-block small text-muted">{line.inventoryLocationName}</span>
                          </td>
                          <td className="text-end">{formatQuantity(line.pendingQuantity, line.unit)}</td>
                          <td>
                            <input
                              type="number"
                              min={0}
                              max={line.pendingQuantity}
                              step="0.001"
                              inputMode="decimal"
                              className="form-control form-control-sm"
                              aria-label={`Cantidad recibida de ${line.supplyItemName}`}
                              value={quantityText(line)}
                              onChange={(event) => setInput(line.id, { quantity: event.target.value })}
                            />
                          </td>
                          <td>
                            <input
                              type="number"
                              min={0}
                              step="0.01"
                              inputMode="decimal"
                              className="form-control form-control-sm"
                              aria-label={`Costo unitario de ${line.supplyItemName}`}
                              value={costText(line)}
                              onChange={(event) => setInput(line.id, { unitCost: event.target.value })}
                            />
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <div className="d-flex justify-content-end gap-2">
                  <button type="button" className="btn btn-light" onClick={() => onModeChange('view')} disabled={busy}>
                    Volver
                  </button>
                  <button type="button" className="btn btn-success" onClick={receive} disabled={busy}>
                    {busy ? 'Registrando…' : 'Registrar recepción'}
                  </button>
                </div>
              </>
            ) : (
              <>
                <div className="table-responsive">
                  <table className="table table-sm align-middle">
                    <thead>
                      <tr>
                        <th>Insumo</th>
                        <th className="text-end">Pedido</th>
                        <th className="text-end">Recibido</th>
                        <th className="text-end">Costo</th>
                        <th className="text-end">Total</th>
                      </tr>
                    </thead>
                    <tbody>
                      {order.lines.map((line) => (
                        <tr key={line.id}>
                          <td>
                            {line.supplyItemName}
                            <span className="d-block small text-muted">
                              {line.inventoryLocationName}
                              {line.pendingQuantity > 0 && order.status !== PurchaseOrderStatus.Cancelled && (
                                order.status === PurchaseOrderStatus.Closed
                                  ? <> · no llegaron {formatQuantity(line.pendingQuantity, line.unit)}</>
                                  : <> · faltan {formatQuantity(line.pendingQuantity, line.unit)}</>
                              )}
                            </span>
                          </td>
                          <td className="text-end">{formatQuantity(line.quantity, line.unit)}</td>
                          <td className="text-end">{formatQuantity(line.receivedQuantity, line.unit)}</td>
                          <td className="text-end">{formatMoney(line.unitCost)}</td>
                          <td className="text-end">{formatMoney(line.total)}</td>
                        </tr>
                      ))}
                    </tbody>
                    <tfoot>
                      <tr><td colSpan={4} className="text-end text-muted">Subtotal</td><td className="text-end">{formatMoney(order.subTotal)}</td></tr>
                      <tr><td colSpan={4} className="text-end text-muted">Impuestos</td><td className="text-end">{formatMoney(order.tax)}</td></tr>
                      <tr><td colSpan={4} className="text-end fw-semibold">Total</td><td className="text-end fw-semibold">{formatMoney(order.total)}</td></tr>
                    </tfoot>
                  </table>
                </div>

                <div className="d-flex flex-wrap justify-content-end gap-2">
                  {canCancel && (
                    <button type="button" className="btn btn-soft-danger" onClick={cancel} disabled={busy}>
                      Cancelar compra
                    </button>
                  )}
                  {order.status === PurchaseOrderStatus.Draft && (
                    <button type="button" className="btn btn-light" onClick={send} disabled={busy}>
                      <i className="ri-check-line me-1" />Marcar como enviada
                    </button>
                  )}
                  {canMessage && (
                    <WhatsAppLink
                      text={buildPurchaseOrderMessage(order, tenant?.name)}
                      phone={order.supplierPhone}
                      label={order.status === PurchaseOrderStatus.PartiallyReceived ? 'Pedir lo pendiente por WhatsApp' : 'Enviar por WhatsApp'}
                      className={order.status === PurchaseOrderStatus.Draft ? 'btn btn-primary' : 'btn btn-light'}
                      disabled={busy}
                      // Sending the draft is what makes it sent: the status follows the message.
                      onClick={order.status === PurchaseOrderStatus.Draft ? () => void send() : undefined}
                    />
                  )}
                  {order.status === PurchaseOrderStatus.PartiallyReceived && (
                    <button type="button" className="btn btn-light" onClick={closeWithoutRest} disabled={busy}>
                      <i className="ri-checkbox-circle-line me-1" />Cerrar sin esperar el resto
                    </button>
                  )}
                  {canReceive && (
                    <button type="button" className="btn btn-success" onClick={() => onModeChange('receive')} disabled={busy}>
                      <i className="ri-download-2-line me-1" />
                      {order.status === PurchaseOrderStatus.PartiallyReceived ? 'Recibir lo pendiente' : 'Recibir mercancía'}
                    </button>
                  )}
                </div>
                {canMessage && !order.supplierPhone && (
                  <p className="small text-muted text-end mt-2 mb-0">
                    El proveedor no tiene teléfono: WhatsApp te preguntará a quién enviarlo.
                  </p>
                )}
              </>
            )}
          </>
        )}
      </Offcanvas.Body>
    </Offcanvas>
  );
}
