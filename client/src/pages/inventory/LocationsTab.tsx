import DataTable from '../../components/common/DataTable';
import type { InventoryLocationDto } from '../../types';

const COLUMNS = [
  { header: 'Nombre' },
  { header: 'Código' },
  { header: 'Contacto' },
  { header: 'Ciudad / Estado' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

interface LocationsTabProps {
  locations: InventoryLocationDto[];
  loading: boolean;
  onEdit: (location: InventoryLocationDto) => void;
  onToggleActive: (location: InventoryLocationDto) => void;
}

/** Warehouses: the general one serves every branch, a branch's own serves its sales. */
export default function LocationsTab({ locations, loading, onEdit, onToggleActive }: LocationsTabProps) {
  return (
    <DataTable
      data={locations}
      columns={COLUMNS}
      keyExtractor={(item) => item.id}
      loading={loading}
      emptyMessage="No hay ubicaciones registradas."
      searchFilter={(item, search) =>
        item.name.toLowerCase().includes(search) ||
        item.code.toLowerCase().includes(search) ||
        (item.city || '').toLowerCase().includes(search)
      }
      renderRow={(item) => (
        <>
          <td>
            <div className="fw-semibold">{item.name}</div>
            <div className="text-muted small">
              {item.branchName ? `Sucursal ${item.branchName}` : 'Almacén general'}
              {item.description ? ` · ${item.description}` : ''}
            </div>
          </td>
          <td>{item.code}</td>
          <td>
            <div>{item.contactName || '-'}</div>
            <div className="text-muted small">{item.contactPhone || '-'}</div>
          </td>
          <td>{item.city || '-'}{item.state ? `, ${item.state}` : ''}</td>
          <td className="text-center">
            {item.isDefault && <span className="badge bg-info me-1">Por defecto</span>}
            <span className={`badge ${item.isActive ? 'bg-success' : 'bg-danger'}`}>
              {item.isActive ? 'Activa' : 'Inactiva'}
            </span>
          </td>
          <td className="text-center">
            <button
              className="btn btn-sm btn-soft-primary me-1"
              title="Editar"
              aria-label={`Editar ${item.name}`}
              onClick={() => onEdit(item)}
            >
              <i className="ri-pencil-line"></i>
            </button>
            <button
              className={`btn btn-sm ${item.isActive ? 'btn-soft-warning' : 'btn-soft-success'}`}
              title={item.isActive ? 'Desactivar' : 'Activar'}
              aria-label={item.isActive ? `Desactivar ${item.name}` : `Activar ${item.name}`}
              onClick={() => onToggleActive(item)}
            >
              <i className={item.isActive ? 'ri-pause-line' : 'ri-play-line'}></i>
            </button>
          </td>
        </>
      )}
    />
  );
}
