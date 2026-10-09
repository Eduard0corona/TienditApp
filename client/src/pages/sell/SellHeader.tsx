import { Link } from 'react-router-dom';
import type { Branch } from '../../api/branchesApi';
import BranchPicker from '../../components/branches/BranchPicker';

interface SellHeaderProps {
  /** In focus mode the bar above says where you are. */
  focused: boolean;
  onEnterFocus: () => void;
  online: boolean;
  pendingSales: number;
  failedPendingSales: number;
  onShowPendingSales: () => void;
  /** Branches of the business, when it has several. */
  branches: Branch[];
  hasSeveralBranches: boolean;
  workingBranchId?: string;
  onWorkingBranchChange: (branchId: string) => void;
  /** The branch of the cashier's open drawer: sales are recorded there. */
  drawerBranch: { id: string; name: string } | null;
  showServiceIntake: boolean;
  showOrders: boolean;
}

/** Title and status of Vender: connection, sales waiting on the device, branch, shortcuts and focus mode. */
export default function SellHeader(props: SellHeaderProps) {
  const { drawerBranch, pendingSales, failedPendingSales } = props;
  return (
    <div className="sell-header d-flex flex-wrap align-items-center justify-content-between gap-2">
      {!props.focused && <h1 className="h3 mb-0">Vender</h1>}
      <div className="d-flex flex-wrap align-items-center gap-2 ms-auto">
        {!props.online && (
          <span className="badge text-bg-warning" role="status">
            <i className="ri-wifi-off-line me-1" aria-hidden="true"></i>Sin conexión
          </span>
        )}
        {pendingSales > 0 && (
          <button type="button" className={`btn btn-sm ${failedPendingSales > 0 ? 'btn-danger' : 'btn-warning'}`}
            onClick={props.onShowPendingSales}>
            <i className="ri-upload-cloud-2-line me-1" aria-hidden="true"></i>
            {pendingSales} {pendingSales === 1 ? 'venta sin enviar' : 'ventas sin enviar'}
          </button>
        )}
        {props.hasSeveralBranches && (drawerBranch ? (
          <span className="badge text-bg-light border" title="Las ventas se registran en la sucursal de tu caja abierta">
            <i className="ri-store-3-line me-1" aria-hidden="true"></i>{drawerBranch.name} · tu caja
          </span>
        ) : (
          <BranchPicker
            branches={props.branches}
            value={props.workingBranchId}
            onChange={props.onWorkingBranchChange}
            label="Sucursal donde vendes"
          />
        ))}
        {props.showServiceIntake && (
          <Link to="/orders/cart" className="btn btn-sm btn-light">
            <i className="ri-t-shirt-line me-1" aria-hidden="true"></i>Recibir servicio con piezas
          </Link>
        )}
        {props.showOrders && (
          <Link to="/orders" className="btn btn-sm btn-light">
            <i className="ri-file-list-3-line me-1" aria-hidden="true"></i>Pedidos
          </Link>
        )}
        {!props.focused && (
          <button type="button" className="btn btn-sm btn-light" onClick={props.onEnterFocus}
            title="Vender a pantalla completa, sin el menú: para la tableta del mostrador">
            <i className="ri-fullscreen-line me-1" aria-hidden="true"></i>Modo enfocado
          </button>
        )}
      </div>
    </div>
  );
}
