import PageTitle from '../../components/common/PageTitle';
import { useAuth } from '../../hooks/useAuth';

export default function Dashboard() {
  const { user } = useAuth();

  return (
    <>
      <PageTitle title="Dashboard" />

      {/* Welcome */}
      <div className="row">
        <div className="col-12">
          <div className="card widget-inline">
            <div className="card-body">
              <h4 className="mt-0">
                Bienvenido, {user?.firstName}!
              </h4>
              <p className="text-muted mb-0">
                Aquí puedes ver un resumen de tu negocio en FoodPark Digital.
              </p>
            </div>
          </div>
        </div>
      </div>

      {/* Stats */}
      <div className="row">
        <div className="col-sm-6 col-xl-3">
          <div className="card tilebox-one">
            <div className="card-body">
              <i className="ri-shopping-basket-2-line float-end text-muted"></i>
              <h6 className="text-muted text-uppercase mt-0">Pedidos</h6>
              <h2 className="m-b-20">0</h2>
              <span className="badge bg-primary">Hoy</span>
            </div>
          </div>
        </div>

        <div className="col-sm-6 col-xl-3">
          <div className="card tilebox-one">
            <div className="card-body">
              <i className="ri-archive-line float-end text-muted"></i>
              <h6 className="text-muted text-uppercase mt-0">Ingresos</h6>
              <h2 className="m-b-20">$0.00</h2>
              <span className="badge bg-success">Este mes</span>
            </div>
          </div>
        </div>

        <div className="col-sm-6 col-xl-3">
          <div className="card tilebox-one">
            <div className="card-body">
              <i className="ri-vip-diamond-line float-end text-muted"></i>
              <h6 className="text-muted text-uppercase mt-0">Productos</h6>
              <h2 className="m-b-20">0</h2>
              <span className="badge bg-info">Activos</span>
            </div>
          </div>
        </div>

        <div className="col-sm-6 col-xl-3">
          <div className="card tilebox-one">
            <div className="card-body">
              <i className="ri-group-line float-end text-muted"></i>
              <h6 className="text-muted text-uppercase mt-0">Clientes</h6>
              <h2 className="m-b-20">0</h2>
              <span className="badge bg-warning">Total</span>
            </div>
          </div>
        </div>
      </div>

      {/* Info Card */}
      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body text-center py-5">
              <i className="ri-bar-chart-box-line text-muted" style={{ fontSize: '3rem' }}></i>
              <h5 className="mt-3">Dashboard en construcción</h5>
              <p className="text-muted">
                Las métricas y gráficos se mostrarán aquí cuando tengas pedidos y productos registrados.
              </p>
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
