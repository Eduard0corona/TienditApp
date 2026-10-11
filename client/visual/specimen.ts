/**
 * A sample of the components the app draws with the global CSS (Bootstrap with Hyper's values, theme.css): every
 * button variant, fields, cards, tables, badges, alerts, tabs, pagination, menus and the classes only Hyper defined. The
 * specimen scenes put it on a real screen and compare its picture and the computed style of each piece: a change of
 * template that moves a padding or a color shows here even where no screen shows it.
 */
export const SPECIMEN = `
<div id="specimen" class="container-fluid py-3" style="position:absolute;top:0;left:0;right:0;z-index:5000;background:var(--ct-body-bg)">
  <h4 class="header-title" data-s="header-title">Muestrario</h4>
  <h1 data-s="h1">Título 1</h1><h2 data-s="h2">Título 2</h2><h3 data-s="h3">Título 3</h3>
  <h4 data-s="h4">Título 4</h4><h5 data-s="h5">Título 5</h5><h6 data-s="h6">Título 6</h6>
  <p data-s="p">Texto de párrafo con <a href="#" data-s="a">un enlace</a>, <span class="text-muted" data-s="text-muted">texto tenue</span>,
    <small data-s="small">pequeño</small>, <span class="fw-semibold" data-s="fw-semibold">seminegrita</span> y <span class="fw-bold" data-s="fw-bold">negrita</span>.</p>
  <p class="fs-1" data-s="fs-1">fs-1</p><p class="fs-4" data-s="fs-4">fs-4</p><p class="fs-6" data-s="fs-6">fs-6</p>
  <p><span class="font-12" data-s="font-12">font-12</span> <span class="font-13" data-s="font-13">font-13</span>
    <span class="font-15" data-s="font-15">font-15</span> <span class="font-16" data-s="font-16">font-16</span> <span class="font-22" data-s="font-22">font-22</span></p>

  <div class="d-flex flex-wrap gap-2 mb-3">
    <button class="btn btn-primary" data-s="btn-primary">Principal</button>
    <button class="btn btn-secondary" data-s="btn-secondary">Secundario</button>
    <button class="btn btn-success" data-s="btn-success">Éxito</button>
    <button class="btn btn-danger" data-s="btn-danger">Peligro</button>
    <button class="btn btn-warning" data-s="btn-warning">Aviso</button>
    <button class="btn btn-light" data-s="btn-light">Claro</button>
    <button class="btn btn-dark" data-s="btn-dark">Oscuro</button>
    <button class="btn btn-link" data-s="btn-link">Enlace</button>
    <button class="btn btn-primary" disabled data-s="btn-primary-disabled">Deshabilitado</button>
  </div>
  <div class="d-flex flex-wrap gap-2 mb-3">
    <button class="btn btn-outline-primary" data-s="btn-outline-primary">Contorno</button>
    <button class="btn btn-outline-secondary" data-s="btn-outline-secondary">Contorno sec.</button>
    <button class="btn btn-outline-danger" data-s="btn-outline-danger">Contorno peligro</button>
    <button class="btn btn-outline-light" data-s="btn-outline-light">Contorno claro</button>
    <button class="btn btn-soft-primary" data-s="btn-soft-primary">Suave</button>
    <button class="btn btn-soft-success" data-s="btn-soft-success">Suave éxito</button>
    <button class="btn btn-soft-danger" data-s="btn-soft-danger">Suave peligro</button>
    <button class="btn btn-soft-warning" data-s="btn-soft-warning">Suave aviso</button>
    <button class="btn btn-soft-secondary" data-s="btn-soft-secondary">Suave sec.</button>
    <button class="btn btn-soft-danger" disabled data-s="btn-soft-disabled">Suave deshabilitado</button>
  </div>
  <div class="d-flex flex-wrap align-items-center gap-2 mb-3">
    <button class="btn btn-primary btn-sm" data-s="btn-sm">Chico</button>
    <button class="btn btn-primary btn-lg" data-s="btn-lg">Grande</button>
    <button class="btn-close" aria-label="Cerrar" data-s="btn-close"></button>
    <div class="btn-group" data-s="btn-group"><button class="btn btn-light">Uno</button><button class="btn btn-light active">Dos</button></div>
  </div>

  <div class="row g-3 mb-3">
    <div class="col-md-6">
      <label class="form-label" for="s-input" data-s="form-label">Nombre</label>
      <input class="form-control" id="s-input" value="Café americano" data-s="form-control">
      <div class="form-text" data-s="form-text">Ayuda del campo.</div>
      <input class="form-control form-control-sm mt-2" value="Chico" data-s="form-control-sm">
      <input class="form-control is-invalid mt-2" value="Con error" data-s="form-control-invalid">
      <div class="invalid-feedback d-block" data-s="invalid-feedback">Escribe un nombre.</div>
      <textarea class="form-control mt-2" rows="2" data-s="textarea">Notas</textarea>
    </div>
    <div class="col-md-6">
      <select class="form-select" data-s="form-select"><option>Efectivo</option></select>
      <select class="form-select form-select-sm mt-2" data-s="form-select-sm"><option>Chico</option></select>
      <div class="input-group mt-2" data-s="input-group"><span class="input-group-text" data-s="input-group-text">$</span><input class="form-control" value="55.00"></div>
      <div class="form-check mt-2"><input class="form-check-input" type="checkbox" id="s-check" checked data-s="form-check-input"><label class="form-check-label" for="s-check" data-s="form-check-label">Marcado</label></div>
      <div class="form-check form-switch"><input class="form-check-input" type="checkbox" role="switch" id="s-switch" checked data-s="form-switch"><label class="form-check-label" for="s-switch">Interruptor</label></div>
      <div class="form-check"><input class="form-check-input" type="radio" name="s-radio" id="s-radio" checked data-s="form-radio"><label class="form-check-label" for="s-radio">Opción</label></div>
    </div>
  </div>

  <div class="row g-3 mb-3">
    <div class="col-md-4"><div class="card" data-s="card"><div class="card-header" data-s="card-header">Encabezado</div><div class="card-body" data-s="card-body"><h5 class="card-title" data-s="card-title">Tarjeta</h5><p class="card-text mb-0">Contenido.</p></div><div class="card-footer" data-s="card-footer">Pie</div></div></div>
    <div class="col-md-4">
      <div class="alert alert-success" data-s="alert-success">Guardado.</div>
      <div class="alert alert-danger" data-s="alert-danger">Error.</div>
      <div class="alert alert-warning mb-0" data-s="alert-warning">Aviso.</div>
    </div>
    <div class="col-md-4">
      <span class="badge text-bg-primary" data-s="badge-primary">Principal</span>
      <span class="badge text-bg-success" data-s="badge-success">Pagado</span>
      <span class="badge text-bg-danger" data-s="badge-danger">Cancelado</span>
      <span class="badge text-bg-warning" data-s="badge-warning">Pendiente</span>
      <span class="badge text-bg-info" data-s="badge-info">Info</span>
      <span class="badge text-bg-secondary" data-s="badge-secondary">Inactivo</span>
      <span class="badge text-bg-light" data-s="badge-light">Claro</span>
      <span class="badge rounded-pill text-bg-primary" data-s="badge-pill">9+</span>
      <div class="mt-2 d-flex gap-2"><div class="avatar-sm" data-s="avatar-sm"><span class="avatar-title rounded-circle" data-s="avatar-title">M</span></div><div class="avatar-lg" data-s="avatar-lg"><span class="avatar-title rounded">L</span></div></div>
    </div>
  </div>

  <div class="table-responsive mb-3">
    <table class="table table-hover table-centered mb-0" data-s="table">
      <thead class="table-light" data-s="table-light"><tr><th data-s="th">Producto</th><th class="text-end">Importe</th></tr></thead>
      <tbody><tr><td data-s="td">Latte</td><td class="text-end">$55.00</td></tr><tr class="table-active" data-s="table-active"><td>Concha</td><td class="text-end">$18.00</td></tr></tbody>
    </table>
  </div>
  <table class="table table-sm table-bordered mb-3" data-s="table-sm"><tbody><tr><td data-s="td-sm">Chica</td><td>Con borde</td></tr></tbody></table>

  <ul class="nav nav-tabs mb-2" data-s="nav-tabs"><li class="nav-item"><a class="nav-link active" href="#" data-s="nav-link-active">Activa</a></li><li class="nav-item"><a class="nav-link" href="#" data-s="nav-link">Otra</a></li></ul>
  <ul class="nav nav-tabs nav-bordered mb-2" data-s="nav-bordered"><li class="nav-item"><a class="nav-link active" href="#">Bordeada</a></li><li class="nav-item"><a class="nav-link" href="#">Otra</a></li></ul>
  <nav class="mb-2"><ul class="pagination pagination-rounded mb-0" data-s="pagination"><li class="page-item disabled"><span class="page-link">‹</span></li><li class="page-item active"><span class="page-link" data-s="page-link-active">1</span></li><li class="page-item"><a class="page-link" href="#" data-s="page-link">2</a></li></ul></nav>
  <ul class="list-group mb-3" data-s="list-group"><li class="list-group-item" data-s="list-group-item">Elemento</li><li class="list-group-item active">Activo</li></ul>

  <div class="dropdown-menu show position-static mb-3" data-s="dropdown-menu"><span class="dropdown-header">Encabezado</span><a class="dropdown-item" href="#" data-s="dropdown-item">Opción</a><div class="dropdown-divider"></div><a class="dropdown-item active" href="#">Activa</a></div>
  <div class="modal position-static d-block" tabindex="-1"><div class="modal-dialog m-0"><div class="modal-content" data-s="modal-content"><div class="modal-header" data-s="modal-header"><h5 class="modal-title" data-s="modal-title">Diálogo</h5><button class="btn-close"></button></div><div class="modal-body" data-s="modal-body">Cuerpo del diálogo.</div><div class="modal-footer" data-s="modal-footer"><button class="btn btn-light">Cancelar</button><button class="btn btn-primary">Aceptar</button></div></div></div></div>
  <div class="offcanvas offcanvas-end position-static show mt-3" style="visibility:visible;transform:none" data-s="offcanvas"><div class="offcanvas-header" data-s="offcanvas-header"><h5 class="offcanvas-title">Panel</h5></div><div class="offcanvas-body" data-s="offcanvas-body">Cuerpo del panel.</div></div>
  <div class="d-flex flex-wrap gap-3 mt-3">
    <div class="p-1 bg-light" data-s="p-1">p-1</div><div class="p-2 bg-light" data-s="p-2">p-2</div><div class="p-3 bg-light" data-s="p-3">p-3</div>
    <div class="p-4 bg-light" data-s="p-4">p-4</div><div class="p-5 bg-light" data-s="p-5">p-5</div><div class="mb-3 bg-light" data-s="mb-3">mb-3</div>
    <div class="d-flex gap-2 bg-light" data-s="gap-2"><span>a</span><span>b</span></div><div class="row gx-3" data-s="gx-3"><div class="col">g</div></div>
    <div class="shadow-sm p-2" data-s="shadow-sm">sombra</div><div class="border rounded p-2" data-s="border">borde</div>
    <div class="bg-primary text-white p-2" data-s="bg-primary">bg-primary</div><div class="bg-light text-dark p-2" data-s="bg-light">bg-light</div>
    <div class="text-bg-dark p-2" data-s="text-bg-dark">text-bg-dark</div>
  </div>
</div>`;

/** The computed properties compared for each piece of the specimen: what moves a layout or changes a color. */
export const STYLE_PROPERTIES = [
  'display', 'font-family', 'font-size', 'font-weight', 'line-height', 'letter-spacing', 'text-transform', 'color',
  'background-color', 'background-image', 'padding-top', 'padding-right', 'padding-bottom', 'padding-left',
  'margin-top', 'margin-right', 'margin-bottom', 'margin-left', 'border-top-width', 'border-top-style',
  'border-top-color', 'border-right-color', 'border-bottom-width', 'border-bottom-color', 'border-left-width',
  'border-top-left-radius', 'border-bottom-right-radius', 'box-shadow', 'outline-style', 'opacity', 'height', 'gap',
  'row-gap', 'column-gap', 'cursor', 'text-decoration-line', 'vertical-align',
] as const;
