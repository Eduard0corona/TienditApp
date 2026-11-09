import PrivatePhoto from '../../components/common/PrivatePhoto';
import type { ProductDto } from '../../types';
import { SERVICE_PRIORITY_OPTIONS } from '../../utils/serviceDetail';
import type { CartLine, ServicePhotoItem } from './ordersCartModel';
import ServicePiecesEditor from './ServicePiecesEditor';
import type { ServiceIntake } from './useServiceIntake';

interface ServicePhotosFieldProps {
  id: string;
  label: string;
  photos: ServicePhotoItem[];
  onUpload: (files: FileList | null) => void;
  onRemove: (id: string) => void;
}

/** Photos of the piece as it came in or as it leaves: several at once, each one removable before the order is registered. */
function ServicePhotosField({ id, label, photos, onUpload, onRemove }: ServicePhotosFieldProps) {
  return (
    <div className="col-md-6">
      <label htmlFor={id} className="form-label">{label}</label>
      <input id={id}
        type="file"
        accept="image/*"
        className="form-control"
        multiple
        onChange={(e) => {
          onUpload(e.target.files);
          e.currentTarget.value = '';
        }}
      />
      {photos.length > 0 && (
        <div className="orders-photo-grid mt-2">
          {photos.map((photo) => (
            <div className="orders-photo-item" key={photo.id}>
              <PrivatePhoto path={photo.url} alt={photo.fileName} className="rounded w-100" style={{ height: 84 }} />
              <button
                type="button"
                className="btn btn-xs btn-danger orders-photo-remove"
                aria-label={`Quitar ${photo.fileName}`}
                onClick={() => onRemove(photo.id)}
              >
                <i className="ri-close-line"></i>
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

interface ServiceIntakeFormProps {
  intake: ServiceIntake;
  /** The services of the catalog a piece can point to. */
  serviceCatalog: ProductDto[];
  /** The first service of the cart: a new piece starts with it. */
  firstServiceLine: CartLine | undefined;
}

/** How a service order is received in Nuevo pedido: dates, priority, notes, checklist, photos and its pieces. */
export default function ServiceIntakeForm({ intake, serviceCatalog, firstServiceLine }: ServiceIntakeFormProps) {
  return (
    <div className="border rounded p-2 mb-2">
      <h5 className="font-15 mb-2">Recepción de servicio</h5>
      <div className="row g-2 mb-2">
        <div className="col-md-6">
          <label htmlFor="orderscart-fecha-de-recepcion" className="form-label">Fecha de recepción</label>
          <input id="orderscart-fecha-de-recepcion"
            type="datetime-local"
            className="form-control"
            value={intake.receivedAt}
            onChange={(e) => intake.setReceivedAt(e.target.value)}
          />
        </div>
        <div className="col-md-6">
          <label htmlFor="orderscart-fecha-promesa" className="form-label">Fecha promesa</label>
          <input id="orderscart-fecha-promesa"
            type="datetime-local"
            className="form-control"
            value={intake.promisedAt}
            onChange={(e) => intake.setPromisedAt(e.target.value)}
          />
        </div>
        <div className="col-md-6">
          <label htmlFor="orderscart-prioridad" className="form-label">Prioridad</label>
          <select id="orderscart-prioridad"
            className="form-select"
            value={intake.priority}
            onChange={(e) => intake.setPriority(Number(e.target.value))}
          >
            {SERVICE_PRIORITY_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </div>
      </div>

      <div className="mb-2">
        <label htmlFor="orderscart-notas-de-ingreso" className="form-label">Notas de ingreso</label>
        <textarea id="orderscart-notas-de-ingreso"
          className="form-control"
          rows={2}
          value={intake.intakeNotes}
          onChange={(e) => intake.setIntakeNotes(e.target.value)}
        />
      </div>

      <div className="mb-2">
        <label htmlFor="orderscart-notas-de-entrega" className="form-label">Notas de entrega</label>
        <textarea id="orderscart-notas-de-entrega"
          className="form-control"
          rows={2}
          value={intake.deliveryNotes}
          onChange={(e) => intake.setDeliveryNotes(e.target.value)}
        />
      </div>

      <div className="mb-2">
        <div className="d-flex justify-content-between align-items-center mb-1">
          <label htmlFor="orderscart-checklist-de-recepcion" className="form-label mb-0">Checklist de recepción</label>
          <button type="button" className="btn btn-sm btn-soft-primary" onClick={intake.addChecklistItem}>
            <i className="ri-add-line me-1"></i>Agregar item
          </button>
        </div>

        {intake.checklist.map((item) => (
          <div className="d-flex align-items-center gap-2 mb-1" key={item.id}>
            <input id="orderscart-checklist-de-recepcion"
              className="form-check-input mt-0"
              type="checkbox"
              checked={item.checked}
              onChange={(e) => intake.updateChecklistItem(item.id, { checked: e.target.checked })}
            />
            <input
              type="text"
              className="form-control form-control-sm"
              placeholder="Ejemplo: Sin manchas visibles"
              value={item.label}
              onChange={(e) => intake.updateChecklistItem(item.id, { label: e.target.value })}
            />
            <button
              type="button"
              className="btn btn-sm btn-soft-danger"
              aria-label={item.label ? `Quitar ${item.label}` : 'Quitar este punto'}
              onClick={() => intake.removeChecklistItem(item.id)}
              disabled={intake.checklist.length <= 1}
            >
              <i className="ri-delete-bin-line" aria-hidden="true"></i>
            </button>
          </div>
        ))}
      </div>

      <div className="row g-2 mb-2">
        <ServicePhotosField
          id="orderscart-fotos-antes"
          label="Fotos antes"
          photos={intake.beforePhotos}
          onUpload={(files) => void intake.uploadPhotos(files, 'before')}
          onRemove={(id) => intake.removePhoto('before', id)}
        />
        <ServicePhotosField
          id="orderscart-fotos-despues"
          label="Fotos después"
          photos={intake.afterPhotos}
          onUpload={(files) => void intake.uploadPhotos(files, 'after')}
          onRemove={(id) => intake.removePhoto('after', id)}
        />
      </div>

      <ServicePiecesEditor
        pieces={intake.pieces}
        serviceCatalog={serviceCatalog}
        onAdd={() => intake.addPiece(firstServiceLine)}
        onChange={intake.updatePiece}
        onRemove={intake.removePiece}
      />
    </div>
  );
}
