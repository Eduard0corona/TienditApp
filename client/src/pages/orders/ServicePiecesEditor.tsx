import type { CreateServiceOrderPieceRequest, ProductDto } from '../../types';

interface ServicePiecesEditorProps {
  pieces: CreateServiceOrderPieceRequest[];
  /** The services of the catalog a piece can point to. */
  serviceCatalog: ProductDto[];
  onAdd: () => void;
  onChange: (index: number, changes: Partial<CreateServiceOrderPieceRequest>) => void;
  onRemove: (index: number) => void;
}

/** The pieces a service order receives: what each one is, the service asked for and how it came in. At least one stays. */
export default function ServicePiecesEditor({ pieces, serviceCatalog, onAdd, onChange, onRemove }: ServicePiecesEditorProps) {
  return (
    <div>
      <div className="d-flex justify-content-between align-items-center mb-2">
        <div className="form-label mb-0">Piezas</div>
        <button type="button" className="btn btn-sm btn-soft-primary" onClick={onAdd}>
          <i className="ri-add-line me-1"></i>Agregar pieza
        </button>
      </div>

      {pieces.map((piece, index) => (
        <div key={`piece-${index}`} className="border rounded p-2 mb-2">
          <div className="row g-2">
            <div className="col-md-6">
              <label htmlFor={`orderscart-nombre-de-pieza-${index}`} className="form-label">Nombre de pieza *</label>
              <input id={`orderscart-nombre-de-pieza-${index}`}
                type="text"
                className="form-control"
                value={piece.itemName}
                onChange={(e) => onChange(index, { itemName: e.target.value })}
              />
            </div>
            <div className="col-md-6">
              <label htmlFor={`orderscart-servicio-solicitado-${index}`} className="form-label">Servicio solicitado</label>
              <input id={`orderscart-servicio-solicitado-${index}`}
                type="text"
                className="form-control"
                value={piece.requestedService || ''}
                onChange={(e) => onChange(index, { requestedService: e.target.value })}
              />
            </div>
            <div className="col-md-6">
              <label htmlFor={`orderscart-servicio-del-catalogo-${index}`} className="form-label">Servicio del catálogo</label>
              <select id={`orderscart-servicio-del-catalogo-${index}`}
                className="form-select"
                value={piece.serviceProductId || ''}
                onChange={(e) =>
                  onChange(index, {
                    serviceProductId: e.target.value || undefined,
                  })
                }
              >
                <option value="">Seleccionar...</option>
                {serviceCatalog.map((item) => (
                  <option key={item.id} value={item.id}>{item.name}</option>
                ))}
              </select>
            </div>
            <div className="col-md-2">
              <label htmlFor={`orderscart-rastreable-${index}`} className="form-label d-block">Rastreable</label>
              <input id={`orderscart-rastreable-${index}`}
                type="checkbox"
                className="form-check-input"
                checked={piece.isTrackable ?? true}
                onChange={(e) => onChange(index, { isTrackable: e.target.checked })}
              />
            </div>
            <div className="col-md-2">
              <label htmlFor={`orderscart-notificar-${index}`} className="form-label d-block">Notificar</label>
              <input id={`orderscart-notificar-${index}`}
                type="checkbox"
                className="form-check-input"
                checked={piece.notifyCustomer ?? false}
                onChange={(e) => onChange(index, { notifyCustomer: e.target.checked })}
              />
            </div>
            <div className="col-md-6">
              <label htmlFor={`orderscart-marca-${index}`} className="form-label">Marca</label>
              <input id={`orderscart-marca-${index}`}
                type="text"
                className="form-control"
                value={piece.brand || ''}
                onChange={(e) => onChange(index, { brand: e.target.value })}
              />
            </div>
            <div className="col-md-6">
              <label htmlFor={`orderscart-modelo-${index}`} className="form-label">Modelo</label>
              <input id={`orderscart-modelo-${index}`}
                type="text"
                className="form-control"
                value={piece.model || ''}
                onChange={(e) => onChange(index, { model: e.target.value })}
              />
            </div>
            <div className="col-md-4">
              <label htmlFor={`orderscart-color-${index}`} className="form-label">Color</label>
              <input id={`orderscart-color-${index}`}
                type="text"
                className="form-control"
                value={piece.color || ''}
                onChange={(e) => onChange(index, { color: e.target.value })}
              />
            </div>
            <div className="col-md-4">
              <label htmlFor={`orderscart-talla-${index}`} className="form-label">Talla</label>
              <input id={`orderscart-talla-${index}`}
                type="text"
                className="form-control"
                value={piece.size || ''}
                onChange={(e) => onChange(index, { size: e.target.value })}
              />
            </div>
            <div className="col-md-4">
              <label htmlFor={`orderscart-material-${index}`} className="form-label">Material</label>
              <input id={`orderscart-material-${index}`}
                type="text"
                className="form-control"
                value={piece.material || ''}
                onChange={(e) => onChange(index, { material: e.target.value })}
              />
            </div>
            <div className="col-md-6">
              <label htmlFor={`orderscart-condicion-${index}`} className="form-label">Condición</label>
              <textarea id={`orderscart-condicion-${index}`}
                className="form-control"
                rows={2}
                value={piece.conditionNotes || ''}
                onChange={(e) => onChange(index, { conditionNotes: e.target.value })}
              />
            </div>
            <div className="col-md-6">
              <label htmlFor={`orderscart-nota-interna-${index}`} className="form-label">Nota interna</label>
              <textarea id={`orderscart-nota-interna-${index}`}
                className="form-control"
                rows={2}
                value={piece.internalNotes || ''}
                onChange={(e) => onChange(index, { internalNotes: e.target.value })}
              />
            </div>
          </div>
          <div className="mt-2 text-end">
            <button
              type="button"
              className="btn btn-sm btn-soft-danger"
              aria-label={piece.itemName ? `Quitar ${piece.itemName}` : `Quitar la pieza ${index + 1}`}
              onClick={() => onRemove(index)}
              disabled={pieces.length <= 1}
            >
              <i className="ri-delete-bin-line" aria-hidden="true"></i>
            </button>
          </div>
        </div>
      ))}
    </div>
  );
}
