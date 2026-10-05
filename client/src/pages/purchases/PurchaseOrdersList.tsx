import { useCallback, useEffect, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { purchaseOrdersApi } from '../../api/purchaseOrdersApi';
import { suppliersApi } from '../../api/suppliersApi';
import { inventoryApi } from '../../api/inventoryApi';
import type {
  CreatePurchaseOrderRequest,
  CreatePurchaseOrderLineRequest,
  InventoryLocationDto,
  PaginatedList,
  PurchaseOrderDto,
  PurchaseOrderStatus,
  SupplierDto,
  SupplyItemDto,
} from '../../types';
import { PurchaseOrderStatus as PurchaseOrderStatusConst } from '../../types';
import { getApiErrorMessage } from '../../api/errors';
import PurchaseOrderDetail, { type PurchaseOrderDetailMode } from '../../components/purchases/PurchaseOrderDetail';
import { getPurchaseOrderStatusBadge, getPurchaseOrderStatusLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';

const COLUMNS = [
  { header: 'Folio' },
  { header: 'Proveedor' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Fecha', className: 'text-center' },
  { header: 'Total', className: 'text-end' },
  { header: 'Acciones', className: 'text-center' },
];

const EMPTY_PAGINATED = {
  items: [],
  pageNumber: 1,
  totalPages: 0,
  totalCount: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

interface LineFormState {
  supplyItemId: string;
  inventoryLocationId: string;
  quantity: string;
  unitCost: string;
  taxPercent: string;
  notes: string;
}

interface FormState {
  supplierId: string;
  expectedDeliveryAt: string;
  referenceNumber: string;
  notes: string;
  markAsSent: boolean;
  lines: LineFormState[];
}

const buildLine = (): LineFormState => ({
  supplyItemId: '',
  inventoryLocationId: '',
  quantity: '1',
  unitCost: '0',
  taxPercent: '0',
  notes: '',
});

const buildForm = (): FormState => ({
  supplierId: '',
  expectedDeliveryAt: '',
  referenceNumber: '',
  notes: '',
  markAsSent: true,
  lines: [buildLine()],
});

export default function PurchaseOrdersList() {
  const [orders, setOrders] = useState<PaginatedList<PurchaseOrderDto>>(EMPTY_PAGINATED);
  const [loading, setLoading] = useState(true);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [statusFilter, setStatusFilter] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [suppliers, setSuppliers] = useState<SupplierDto[]>([]);
  const [supplyOptions, setSupplyOptions] = useState<SupplyItemDto[]>([]);
  const [locationOptions, setLocationOptions] = useState<InventoryLocationDto[]>([]);

  const [showModal, setShowModal] = useState(false);
  const [saving, setSaving] = useState(false);
  const [detail, setDetail] = useState<{ id: string; mode: PurchaseOrderDetailMode } | null>(null);
  const [form, setForm] = useState<FormState>(buildForm());

  const activeSuppliers = useMemo(() => suppliers.filter((x) => x.isActive), [suppliers]);

  const loadLookups = useCallback(async () => {
    try {
      const [suppliersRes, suppliesRes, locationsRes] = await Promise.all([
        suppliersApi.getAll({ isActive: true, pageNumber: 1, pageSize: 500 }),
        inventoryApi.getSupplies({ isActive: true, pageNumber: 1, pageSize: 500 }),
        inventoryApi.getLocations({ isActive: true }),
      ]);
      setSuppliers(suppliersRes.data.items);
      setSupplyOptions(suppliesRes.data.items);
      setLocationOptions(locationsRes.data);
    } catch {
      setSuppliers([]);
      setSupplyOptions([]);
      setLocationOptions([]);
    }
  }, []);

  const loadOrders = useCallback(async () => {
    setLoading(true);
    try {
      const params: { status?: PurchaseOrderStatus; pageNumber: number; pageSize: number } = {
        pageNumber,
        pageSize,
      };
      if (statusFilter !== '') params.status = Number(statusFilter) as PurchaseOrderStatus;
      const { data } = await purchaseOrdersApi.getAll(params);
      setOrders(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron cargar las compras.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, statusFilter]);

  useEffect(() => {
    loadLookups();
  }, [loadLookups]);

  useEffect(() => {
    loadOrders();
  }, [loadOrders]);

  const addLine = () => {
    setForm((x) => ({ ...x, lines: [...x.lines, buildLine()] }));
  };

  const updateLine = (index: number, patch: Partial<LineFormState>) => {
    setForm((x) => ({
      ...x,
      lines: x.lines.map((line, i) => (i === index ? { ...line, ...patch } : line)),
    }));
  };

  const removeLine = (index: number) => {
    setForm((x) => ({
      ...x,
      lines: x.lines.length <= 1 ? x.lines : x.lines.filter((_, i) => i !== index),
    }));
  };

  const submit = async () => {
    setError('');
    setSuccess('');

    if (!form.supplierId) {
      setError('Selecciona un proveedor.');
      return;
    }

    const lines: CreatePurchaseOrderLineRequest[] = [];
    for (let index = 0; index < form.lines.length; index += 1) {
      const line = form.lines[index];
      const quantity = Number(line.quantity);
      const unitCost = Number(line.unitCost);
      const taxPercent = Number(line.taxPercent);
      if (!line.supplyItemId || !line.inventoryLocationId) {
        setError(`Línea ${index + 1}: insumo y ubicación son obligatorios.`);
        return;
      }
      if (Number.isNaN(quantity) || quantity <= 0) {
        setError(`Línea ${index + 1}: cantidad inválida.`);
        return;
      }
      if (Number.isNaN(unitCost) || unitCost < 0) {
        setError(`Línea ${index + 1}: costo inválido.`);
        return;
      }
      if (Number.isNaN(taxPercent) || taxPercent < 0 || taxPercent > 100) {
        setError(`Línea ${index + 1}: impuesto inválido.`);
        return;
      }

      lines.push({
        supplyItemId: line.supplyItemId,
        inventoryLocationId: line.inventoryLocationId,
        quantity,
        unitCost,
        taxPercent,
        notes: line.notes.trim() || undefined,
      });
    }

    const payload: CreatePurchaseOrderRequest = {
      supplierId: form.supplierId,
      expectedDeliveryAt: form.expectedDeliveryAt ? new Date(form.expectedDeliveryAt).toISOString() : undefined,
      referenceNumber: form.referenceNumber.trim() || undefined,
      notes: form.notes.trim() || undefined,
      markAsSent: form.markAsSent,
      lines,
    };

    try {
      setSaving(true);
      await purchaseOrdersApi.create(payload);
      setSuccess('Compra registrada.');
      setShowModal(false);
      setForm(buildForm());
      await loadOrders();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo registrar la compra.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <PageTitle title="Compras" subtitle="Abastecimiento" />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError('')}></button>
        </div>
      )}
      {success && (
        <div className="alert alert-success alert-dismissible fade show">
          {success}
          <button type="button" className="btn-close" onClick={() => setSuccess('')}></button>
        </div>
      )}

      <div className="card">
        <div className="card-body">
          <div className="d-flex justify-content-between align-items-center mb-2">
            <div className="d-flex align-items-center gap-2">
              <label className="form-label mb-0">Estado</label>
              <select
                className="form-select form-select-sm"
                value={statusFilter}
                onChange={(e) => {
                  setStatusFilter(e.target.value);
                  setPageNumber(1);
                }}
              >
                <option value="">Todos</option>
                <option value={PurchaseOrderStatusConst.Draft}>Borrador</option>
                <option value={PurchaseOrderStatusConst.Sent}>Enviada</option>
                <option value={PurchaseOrderStatusConst.PartiallyReceived}>Recepción parcial</option>
                <option value={PurchaseOrderStatusConst.Received}>Recibida</option>
                <option value={PurchaseOrderStatusConst.Cancelled}>Cancelada</option>
              </select>
            </div>
            <button className="btn btn-primary btn-sm" onClick={() => setShowModal(true)}>
              <i className="ri-add-line me-1"></i>Nueva compra
            </button>
          </div>

          <DataTable
            data={orders.items}
            columns={COLUMNS}
            keyExtractor={(item) => item.id}
            loading={loading}
            emptyMessage="No hay compras registradas."
            searchFilter={(item, search) =>
              item.orderNumber.toLowerCase().includes(search) ||
              item.supplierName.toLowerCase().includes(search) ||
              (item.referenceNumber || '').toLowerCase().includes(search)
            }
            serverSide={{
              totalCount: orders.totalCount,
              page: pageNumber,
              pageSize,
              onPageChange: setPageNumber,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPageNumber(1);
              },
            }}
            renderRow={(item) => (
              <>
                <td className="fw-semibold">
                  <button type="button" className="btn btn-link p-0 fw-semibold" onClick={() => setDetail({ id: item.id, mode: 'view' })}>
                    {item.orderNumber}
                  </button>
                </td>
                <td>{item.supplierName}</td>
                <td className="text-center">
                  <span className={`badge ${getPurchaseOrderStatusBadge(item.status)}`}>{getPurchaseOrderStatusLabel(item.status)}</span>
                </td>
                <td className="text-center">{new Date(item.orderedAt).toLocaleDateString('es-MX')}</td>
                <td className="text-end">{formatMoney(item.total)}</td>
                <td className="text-center text-nowrap">
                  <button
                    className="btn btn-sm btn-soft-primary me-1"
                    aria-label={`Ver ${item.orderNumber}`}
                    onClick={() => setDetail({ id: item.id, mode: 'view' })}
                  >
                    <i className="ri-eye-line"></i>
                  </button>
                  {(item.status === PurchaseOrderStatusConst.Sent ||
                    item.status === PurchaseOrderStatusConst.PartiallyReceived) && (
                    <button
                      className="btn btn-sm btn-soft-success"
                      onClick={() => setDetail({ id: item.id, mode: 'receive' })}
                    >
                      <i className="ri-download-2-line me-1"></i>Recibir
                    </button>
                  )}
                </td>
              </>
            )}
          />
        </div>
      </div>

      <PurchaseOrderDetail
        orderId={detail?.id ?? null}
        mode={detail?.mode ?? 'view'}
        onModeChange={(mode) => setDetail((current) => (current ? { ...current, mode } : current))}
        onHide={() => setDetail(null)}
        onChanged={loadOrders}
      />

      <Modal show={showModal} onHide={() => setShowModal(false)} size="xl" centered>
        <Modal.Header closeButton>
          <Modal.Title>Nueva compra</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <div className="row">
            <div className="col-md-6 mb-2">
              <label className="form-label">Proveedor *</label>
              <select
                className="form-select"
                value={form.supplierId}
                onChange={(e) => setForm((x) => ({ ...x, supplierId: e.target.value }))}
              >
                <option value="">Seleccionar...</option>
                {activeSuppliers.map((supplier) => (
                  <option key={supplier.id} value={supplier.id}>{supplier.name}</option>
                ))}
              </select>
            </div>
            <div className="col-md-3 mb-2">
              <label className="form-label">Entrega esperada</label>
              <input
                type="datetime-local"
                className="form-control"
                value={form.expectedDeliveryAt}
                onChange={(e) => setForm((x) => ({ ...x, expectedDeliveryAt: e.target.value }))}
              />
            </div>
            <div className="col-md-3 mb-2">
              <label className="form-label">Referencia</label>
              <input
                className="form-control"
                value={form.referenceNumber}
                onChange={(e) => setForm((x) => ({ ...x, referenceNumber: e.target.value }))}
              />
            </div>
            <div className="col-12 mb-2">
              <label className="form-label">Notas</label>
              <textarea
                className="form-control"
                rows={2}
                value={form.notes}
                onChange={(e) => setForm((x) => ({ ...x, notes: e.target.value }))}
              />
            </div>
            <div className="col-12 mb-3">
              <div className="form-check">
                <input
                  className="form-check-input"
                  type="checkbox"
                  checked={form.markAsSent}
                  onChange={(e) => setForm((x) => ({ ...x, markAsSent: e.target.checked }))}
                />
                <label className="form-check-label">Marcar como enviada</label>
              </div>
            </div>
          </div>

          <div className="d-flex justify-content-between align-items-center mb-2">
            <h5 className="mb-0">Líneas</h5>
            <button className="btn btn-sm btn-light" onClick={addLine}>
              <i className="ri-add-line me-1"></i>Agregar línea
            </button>
          </div>

          {form.lines.map((line, index) => (
            <div className="border rounded p-2 mb-2" key={`po-line-${index}`}>
              <div className="row">
                <div className="col-md-3 mb-2">
                  <label className="form-label">Insumo</label>
                  <select
                    className="form-select"
                    value={line.supplyItemId}
                    onChange={(e) => updateLine(index, { supplyItemId: e.target.value })}
                  >
                    <option value="">Seleccionar...</option>
                    {supplyOptions.map((item) => (
                      <option key={item.id} value={item.id}>{item.name}</option>
                    ))}
                  </select>
                </div>
                <div className="col-md-3 mb-2">
                  <label className="form-label">Ubicación</label>
                  <select
                    className="form-select"
                    value={line.inventoryLocationId}
                    onChange={(e) => updateLine(index, { inventoryLocationId: e.target.value })}
                  >
                    <option value="">Seleccionar...</option>
                    {locationOptions.map((location) => (
                      <option key={location.id} value={location.id}>{location.name}</option>
                    ))}
                  </select>
                </div>
                <div className="col-md-2 mb-2">
                  <label className="form-label">Cantidad</label>
                  <input
                    type="number"
                    step="0.01"
                    className="form-control"
                    value={line.quantity}
                    onChange={(e) => updateLine(index, { quantity: e.target.value })}
                  />
                </div>
                <div className="col-md-2 mb-2">
                  <label className="form-label">Costo</label>
                  <input
                    type="number"
                    step="0.01"
                    className="form-control"
                    value={line.unitCost}
                    onChange={(e) => updateLine(index, { unitCost: e.target.value })}
                  />
                </div>
                <div className="col-md-2 mb-2">
                  <label className="form-label">Impuesto %</label>
                  <input
                    type="number"
                    step="0.01"
                    className="form-control"
                    value={line.taxPercent}
                    onChange={(e) => updateLine(index, { taxPercent: e.target.value })}
                  />
                </div>
                <div className="col-md-10 mb-0">
                  <label className="form-label">Notas</label>
                  <input
                    className="form-control"
                    value={line.notes}
                    onChange={(e) => updateLine(index, { notes: e.target.value })}
                  />
                </div>
                <div className="col-md-2 mb-0 d-flex align-items-end">
                  <button
                    className="btn btn-soft-danger w-100"
                    onClick={() => removeLine(index)}
                    disabled={form.lines.length <= 1}
                  >
                    Eliminar
                  </button>
                </div>
              </div>
            </div>
          ))}
        </Modal.Body>
        <Modal.Footer>
          <button className="btn btn-light" onClick={() => setShowModal(false)}>Cancelar</button>
          <button className="btn btn-primary" onClick={submit} disabled={saving}>
            {saving ? 'Guardando...' : 'Registrar compra'}
          </button>
        </Modal.Footer>
      </Modal>
    </>
  );
}
