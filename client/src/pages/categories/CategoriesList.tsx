import { useId, useState } from 'react';
import { useForm } from 'react-hook-form';
import { Modal } from 'react-bootstrap';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { categoriesApi } from '../../api/categoriesApi';
import type {
  CategoryDto,
  CategoryTemplateDto,
  CreateCategoryRequest,
} from '../../types';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';

const COLUMNS = [
  { header: 'Nombre' },
  { header: 'Descripción' },
  { header: 'Productos', className: 'text-center' },
  { header: 'Orden', className: 'text-center' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

const productsLabel = (count: number) => (count === 1 ? '1 producto' : `${count} productos`);

export default function CategoriesList() {
  const formTitleId = useId();
  const archiveTitleId = useId();
  const templatesTitleId = useId();
  const { confirm } = useConfirm();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [showArchived, setShowArchived] = useState(false);
  const [archiving, setArchiving] = useState<CategoryDto | null>(null);
  const [moveToId, setMoveToId] = useState('');
  const [archiveBusy, setArchiveBusy] = useState(false);
  // What a dialog could not do is said inside it, where it can be seen, not on the list behind it. A dialog stays open
  // while what it sent is on its way, so the answer always reaches the dialog that sent it.
  const [saveError, setSaveError] = useState('');
  const [installError, setInstallError] = useState('');
  const [showModal, setShowModal] = useState(false);
  const [showTemplatesModal, setShowTemplatesModal] = useState(false);
  const [editingCategory, setEditingCategory] = useState<CategoryDto | null>(null);
  const [installingTemplateId, setInstallingTemplateId] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateCategoryRequest>({
    values: editingCategory
      ? { name: editingCategory.name, description: editingCategory.description || '', sortOrder: editingCategory.sortOrder }
      : { name: '', description: '', sortOrder: 0 },
  });

  const categoriesQuery = useQuery({
    queryKey: ['categories', 'manage', showArchived],
    queryFn: async () => (await categoriesApi.getAll(showArchived)).data,
  });
  const categories = categoriesQuery.data ?? [];
  // Read when the templates dialog opens, not before; apart from ['categories'], which installing one reads again.
  const templatesQuery = useQuery({
    queryKey: ['category-templates'],
    queryFn: async () => (await categoriesApi.getTemplates()).data,
    enabled: showTemplatesModal,
  });
  const templates = templatesQuery.data;
  const moveTargets = categories.filter((category) => category.isActive && category.id !== archiving?.id);

  const loadCategories = () => queryClient.invalidateQueries({ queryKey: ['categories'] });

  const openCreateModal = () => {
    setSaveError('');
    setEditingCategory(null);
    reset({ name: '', description: '', sortOrder: 0 });
    setShowModal(true);
  };

  const openTemplatesModal = () => {
    setInstallError('');
    setShowTemplatesModal(true);
  };

  const openEditModal = (category: CategoryDto) => {
    setSaveError('');
    setEditingCategory(category);
    setShowModal(true);
  };

  const closeModal = () => {
    setSaveError('');
    setShowModal(false);
    setEditingCategory(null);
  };

  // While a template is being installed the dialog stays open, so what happens to it is said there.
  const closeTemplatesModal = () => {
    if (installingTemplateId === null) setShowTemplatesModal(false);
  };

  const onSubmit = async (data: CreateCategoryRequest) => {
    setSaveError('');
    try {
      if (editingCategory) {
        await categoriesApi.update(editingCategory.id, { ...data, id: editingCategory.id });
      } else {
        await categoriesApi.create(data);
      }
      toast.success(editingCategory ? 'Categoría actualizada.' : 'Categoría creada.');
      closeModal();
      // Not awaited: the form is done once it is saved. Awaiting the list would keep this submit going, and when it
      // ended it would let go of the next form's «Guardando...» while that one is still on its way.
      void loadCategories();
    } catch (err: unknown) {
      setSaveError(getApiErrorMessage(err, 'Error al guardar la categoría.'));
    }
  };

  const handleInstallTemplate = async (template: CategoryTemplateDto) => {
    setInstallError('');
    setInstallingTemplateId(template.id);
    try {
      const { data } = await categoriesApi.installTemplate(template.id);
      toast.success(`Plantilla "${data.templateName}" instalada: ${data.createdCount} creadas, ${data.skippedCount} omitidas.`);
      setShowTemplatesModal(false);
      // Not awaited, so the dialog does not open again still installing while the list is read.
      void loadCategories();
    } catch (err: unknown) {
      setInstallError(getApiErrorMessage(err, 'Error al instalar la plantilla.'));
    } finally {
      setInstallingTemplateId(null);
    }
  };

  const archive = async (category: CategoryDto, moveProductsToCategoryId?: string) => {
    setArchiveBusy(true);
    try {
      await categoriesApi.toggleActive(category.id, false, moveProductsToCategoryId);
      const target = categories.find((item) => item.id === moveProductsToCategoryId);
      toast.success(target
        ? `“${category.name}” archivada; sus productos ahora están en “${target.name}”.`
        : `“${category.name}” archivada. Puedes restaurarla desde “Mostrar archivadas”.`);
      setArchiving(null);
      await loadCategories();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo archivar la categoría.'));
    } finally {
      setArchiveBusy(false);
    }
  };

  // Archived categories leave the menus, so their products must move to another one first.
  const handleArchive = async (category: CategoryDto) => {
    if (category.productCount > 0) {
      setMoveToId('');
      setArchiving(category);
      return;
    }
    const accepted = await confirm({
      title: 'Archivar categoría',
      message: `“${category.name}” dejará de aparecer en Vender, el menú en línea y los formularios. Podrás restaurarla después.`,
      confirmLabel: 'Archivar',
    });
    if (accepted) await archive(category);
  };

  const handleRestore = async (category: CategoryDto) => {
    try {
      await categoriesApi.toggleActive(category.id, true);
      toast.success(`“${category.name}” está activa otra vez.`);
      await loadCategories();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo restaurar la categoría.'));
    }
  };

  return (
    <>
      <PageTitle title="Categorías" />

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              {categoriesQuery.isError && (
                <div className="alert alert-danger">Error al cargar las categorías.</div>
              )}

              <DataTable
                data={categories}
                columns={COLUMNS}
                keyExtractor={(cat) => cat.id}
                loading={categoriesQuery.isLoading}
                emptyMessage={showArchived ? 'No hay categorías registradas.' : 'No hay categorías activas.'}
                searchPlaceholder="Nombre o descripción"
                filterValues={[showArchived]}
                filters={(
                  <div className="form-check form-switch mb-1">
                    <input
                      id="categories-show-archived"
                      type="checkbox"
                      role="switch"
                      className="form-check-input"
                      checked={showArchived}
                      onChange={(event) => setShowArchived(event.target.checked)}
                    />
                    <label className="form-check-label" htmlFor="categories-show-archived">Mostrar archivadas</label>
                  </div>
                )}
                toolbarEnd={(
                  <>
                    <button type="button" className="btn btn-light btn-sm" onClick={openTemplatesModal}>
                      <i className="ri-layout-grid-line me-1" aria-hidden="true"></i>Instalar plantilla
                    </button>
                    <button type="button" className="btn btn-primary btn-sm" onClick={openCreateModal}>
                      <i className="ri-add-line me-1" aria-hidden="true"></i>Nueva categoría
                    </button>
                  </>
                )}
                searchFilter={(cat, search) =>
                  cat.name.toLowerCase().includes(search) ||
                  (cat.description || '').toLowerCase().includes(search)
                }
                renderRow={(cat) => (
                  <>
                    <td className="fw-semibold">{cat.name}</td>
                    <td className="text-muted">{cat.description || '-'}</td>
                    <td className="text-center">{cat.productCount}</td>
                    <td className="text-center">{cat.sortOrder}</td>
                    <td className="text-center">
                      {cat.isActive
                        ? <span className="badge bg-success-subtle text-success">Activa</span>
                        : <span className="badge bg-secondary-subtle text-secondary">Archivada</span>}
                    </td>
                    <td className="text-center text-nowrap">
                      <button
                        type="button"
                        className="btn btn-sm btn-light me-1"
                        aria-label={`Editar ${cat.name}`}
                        onClick={() => openEditModal(cat)}
                      >
                        Editar
                      </button>
                      {cat.isActive ? (
                        <button
                          type="button"
                          className="btn btn-sm btn-light"
                          aria-label={`Archivar ${cat.name}`}
                          onClick={() => handleArchive(cat)}
                        >
                          Archivar
                        </button>
                      ) : (
                        <button
                          className="btn btn-sm btn-soft-success"
                          title="Restaurar"
                          aria-label={`Restaurar ${cat.name}`}
                          onClick={() => handleRestore(cat)}
                        >
                          <i className="ri-inbox-unarchive-line me-1"></i>Restaurar
                        </button>
                      )}
                    </td>
                  </>
                )}
              />
            </div>
          </div>
        </div>
      </div>

      {/* Create/Edit Modal */}
      <Modal show={showModal} onHide={() => !isSubmitting && closeModal()} centered aria-labelledby={formTitleId}>
        <Modal.Header closeButton={!isSubmitting}>
          <Modal.Title id={formTitleId}>{editingCategory ? 'Editar categoría' : 'Nueva categoría'}</Modal.Title>
        </Modal.Header>
        <form onSubmit={handleSubmit(onSubmit)}>
          <Modal.Body>
            {saveError && <div className="alert alert-danger" role="alert">{saveError}</div>}
            <div className="mb-3">
              <label htmlFor="categorieslist-nombre" className="form-label">Nombre <span className="text-danger">*</span></label>
              <input id="categorieslist-nombre"
                type="text"
                className={`form-control ${errors.name ? 'is-invalid' : ''}`}
                aria-invalid={!!errors.name}
                aria-describedby={errors.name ? 'categorieslist-nombre-error' : undefined}
                {...register('name', { required: 'El nombre es requerido', maxLength: { value: 150, message: 'Máximo 150 caracteres' } })}
              />
              {errors.name && <div id="categorieslist-nombre-error" className="invalid-feedback">{errors.name.message}</div>}
            </div>
            <div className="mb-3">
              <label htmlFor="categorieslist-descripcion" className="form-label">Descripción</label>
              <textarea id="categorieslist-descripcion"
                className={`form-control ${errors.description ? 'is-invalid' : ''}`}
                rows={3}
                aria-invalid={!!errors.description}
                aria-describedby={errors.description ? 'categorieslist-descripcion-error' : undefined}
                {...register('description', { maxLength: { value: 500, message: 'Máximo 500 caracteres' } })}
              />
              {errors.description && <div id="categorieslist-descripcion-error" className="invalid-feedback">{errors.description.message}</div>}
            </div>
            <div className="mb-0">
              <label htmlFor="categorieslist-orden" className="form-label">Orden <span className="text-danger">*</span></label>
              <input id="categorieslist-orden"
                type="number"
                className={`form-control ${errors.sortOrder ? 'is-invalid' : ''}`}
                aria-invalid={!!errors.sortOrder}
                aria-describedby={errors.sortOrder ? 'categorieslist-orden-error' : undefined}
                {...register('sortOrder', {
                  valueAsNumber: true,
                  required: 'Escribe el orden: 0 o mayor',
                  min: { value: 0, message: 'Debe ser 0 o mayor' },
                })}
              />
              {errors.sortOrder && <div id="categorieslist-orden-error" className="invalid-feedback">{errors.sortOrder.message}</div>}
            </div>
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light" onClick={closeModal} disabled={isSubmitting}>
              Cancelar
            </button>
            {/* Also while it fades out after saving: what is still written is not sent again by a double click or Enter
                (with its submit button disabled, Enter does not send the form either). */}
            <button type="submit" className="btn btn-primary" disabled={isSubmitting || !showModal}>
              {isSubmitting ? 'Guardando...' : editingCategory ? 'Guardar cambios' : 'Crear categoría'}
            </button>
          </Modal.Footer>
        </form>
      </Modal>

      <Modal show={archiving !== null} onHide={() => !archiveBusy && setArchiving(null)} centered aria-labelledby={archiveTitleId}>
        <Modal.Header closeButton={!archiveBusy}>
          <Modal.Title id={archiveTitleId}>Archivar {archiving ? `“${archiving.name}”` : 'categoría'}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p>
            Tiene {archiving ? productsLabel(archiving.productCount) : 'productos'}. Elige a qué categoría pasarlos
            para que sigan a la venta; la categoría archivada podrá restaurarse después.
          </p>
          {moveTargets.length === 0 ? (
            <div className="alert alert-warning mb-0">
              No hay otra categoría activa. Crea una primero para pasar ahí los productos.
            </div>
          ) : (
            <>
              <label className="form-label" htmlFor="categories-move-to">Pasar los productos a</label>
              <select
                id="categories-move-to"
                className="form-select"
                value={moveToId}
                onChange={(event) => setMoveToId(event.target.value)}
              >
                <option value="">Elige una categoría…</option>
                {moveTargets.map((category) => (
                  <option key={category.id} value={category.id}>{category.name}</option>
                ))}
              </select>
            </>
          )}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={() => setArchiving(null)} disabled={archiveBusy}>
            Cancelar
          </button>
          <button
            type="button"
            className="btn btn-warning"
            disabled={!moveToId || archiveBusy}
            onClick={() => archiving && archive(archiving, moveToId)}
          >
            {archiveBusy ? 'Archivando…' : 'Pasar productos y archivar'}
          </button>
        </Modal.Footer>
      </Modal>

      <Modal show={showTemplatesModal} onHide={closeTemplatesModal} centered size="lg" aria-labelledby={templatesTitleId}>
        <Modal.Header closeButton={installingTemplateId === null}>
          <Modal.Title id={templatesTitleId}>Instalar plantilla de categorías</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {installError && <div className="alert alert-danger" role="alert">{installError}</div>}
          {/* Reading them again without any goes back to pending, so «Reintentar» shows the spinner. */}
          {templates === undefined && templatesQuery.isError ? (
            <div className="alert alert-danger mb-0 d-flex align-items-center justify-content-between gap-2" role="alert">
              Error al cargar las plantillas de categorías.
              <button type="button" className="btn btn-sm btn-light" onClick={() => void templatesQuery.refetch()}>
                Reintentar
              </button>
            </div>
          ) : templates === undefined ? (
            <div className="text-center py-4">
              <div className="spinner-border text-primary" role="status">
                <span className="visually-hidden">Cargando plantillas…</span>
              </div>
            </div>
          ) : (
            <div className="row g-3">
              {templates.length === 0 && (
                <div className="col-12">
                  <div className="alert alert-light border mb-0">
                    No hay plantillas disponibles para este negocio.
                  </div>
                </div>
              )}

              {templates.map((template) => (
                <div className="col-md-6" key={template.id}>
                  <div className={`card h-100 border ${template.isRecommended ? 'border-primary' : ''}`}>
                    <div className="card-body d-flex flex-column">
                      <div className="d-flex justify-content-between align-items-start mb-2">
                        <h5 className="card-title mb-0">{template.name}</h5>
                        {template.isRecommended && (
                          <span className="badge bg-primary">Recomendada</span>
                        )}
                      </div>
                      <p className="text-muted mb-2">{template.description}</p>
                      <p className="mb-2">
                        <small className="text-muted">{template.categoryCount} categorías</small>
                      </p>
                      <div className="d-flex flex-wrap gap-1 mb-3">
                        {template.categories.slice(0, 6).map((item) => (
                          <span key={`${template.id}-${item.name}`} className="badge bg-light text-dark border">
                            {item.name}
                          </span>
                        ))}
                        {template.categories.length > 6 && (
                          <span className="badge bg-light text-dark border">
                            +{template.categories.length - 6}
                          </span>
                        )}
                      </div>
                      <div className="mt-auto d-grid">
                        <button
                          className="btn btn-primary"
                          onClick={() => handleInstallTemplate(template)}
                          disabled={installingTemplateId !== null || !showTemplatesModal}
                          aria-label={installingTemplateId === template.id ? `Instalando ${template.name}...` : `Instalar ${template.name}`}
                        >
                          {installingTemplateId === template.id ? 'Instalando...' : 'Instalar'}
                        </button>
                      </div>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={closeTemplatesModal} disabled={installingTemplateId !== null}>
            Cerrar
          </button>
        </Modal.Footer>
      </Modal>
    </>
  );
}
