import { useState } from 'react';
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
  const { confirm } = useConfirm();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [showArchived, setShowArchived] = useState(false);
  const [archiving, setArchiving] = useState<CategoryDto | null>(null);
  const [moveToId, setMoveToId] = useState('');
  const [archiveBusy, setArchiveBusy] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [showModal, setShowModal] = useState(false);
  const [showTemplatesModal, setShowTemplatesModal] = useState(false);
  const [editingCategory, setEditingCategory] = useState<CategoryDto | null>(null);
  const [templates, setTemplates] = useState<CategoryTemplateDto[]>([]);
  const [templatesLoading, setTemplatesLoading] = useState(false);
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
  const moveTargets = categories.filter((category) => category.isActive && category.id !== archiving?.id);

  const loadCategories = () => queryClient.invalidateQueries({ queryKey: ['categories'] });

  const openCreateModal = () => {
    setEditingCategory(null);
    reset({ name: '', description: '', sortOrder: 0 });
    setShowModal(true);
  };

  const openTemplatesModal = async () => {
    setError('');
    setSuccess('');
    setShowTemplatesModal(true);
    setTemplatesLoading(true);
    try {
      const { data } = await categoriesApi.getTemplates();
      setTemplates(data);
    } catch {
      setError('Error al cargar las plantillas de categorías.');
    } finally {
      setTemplatesLoading(false);
    }
  };

  const openEditModal = (category: CategoryDto) => {
    setEditingCategory(category);
    setShowModal(true);
  };

  const closeModal = () => {
    setShowModal(false);
    setEditingCategory(null);
  };

  const closeTemplatesModal = () => {
    setShowTemplatesModal(false);
    setInstallingTemplateId(null);
  };

  const onSubmit = async (data: CreateCategoryRequest) => {
    try {
      if (editingCategory) {
        await categoriesApi.update(editingCategory.id, { ...data, id: editingCategory.id });
      } else {
        await categoriesApi.create(data);
      }
      setSuccess(editingCategory ? 'Categoría actualizada.' : 'Categoría creada.');
      closeModal();
      await loadCategories();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al guardar la categoría.');
      setError(msg);
    }
  };

  const handleInstallTemplate = async (template: CategoryTemplateDto) => {
    setError('');
    setSuccess('');
    setInstallingTemplateId(template.id);
    try {
      const { data } = await categoriesApi.installTemplate(template.id);
      setSuccess(
        `Plantilla "${data.templateName}" instalada: ${data.createdCount} creadas, ${data.skippedCount} omitidas.`,
      );
      await loadCategories();
      closeTemplatesModal();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al instalar la plantilla.');
      setError(msg);
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
              <div className="row mb-2">
                <div className="col-sm-5">
                  <h4 className="header-title mt-0">Categorías</h4>
                </div>
                <div className="col-sm-7">
                  <div className="text-sm-end d-flex flex-wrap align-items-center justify-content-sm-end gap-2">
                    <div className="form-check form-switch mb-0 me-2">
                      <input
                        id="categories-show-archived"
                        type="checkbox"
                        className="form-check-input"
                        checked={showArchived}
                        onChange={(event) => setShowArchived(event.target.checked)}
                      />
                      <label className="form-check-label" htmlFor="categories-show-archived">Mostrar archivadas</label>
                    </div>
                    <button className="btn btn-soft-primary" onClick={openTemplatesModal}>
                      <i className="ri-layout-grid-line me-1"></i> Instalar plantilla
                    </button>
                    <button className="btn btn-primary" onClick={openCreateModal}>
                      <i className="ri-add-line me-1"></i> Nueva categoría
                    </button>
                  </div>
                </div>
              </div>

              {categoriesQuery.isError && (
                <div className="alert alert-danger">Error al cargar las categorías.</div>
              )}

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

              <DataTable
                data={categories}
                columns={COLUMNS}
                keyExtractor={(cat) => cat.id}
                loading={categoriesQuery.isLoading}
                emptyMessage={showArchived ? 'No hay categorías registradas.' : 'No hay categorías activas.'}
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
                        className="btn btn-sm btn-soft-primary me-1"
                        title="Editar"
                        aria-label={`Editar ${cat.name}`}
                        onClick={() => openEditModal(cat)}
                      >
                        <i className="ri-pencil-line"></i>
                      </button>
                      {cat.isActive ? (
                        <button
                          className="btn btn-sm btn-soft-warning"
                          title="Archivar"
                          aria-label={`Archivar ${cat.name}`}
                          onClick={() => handleArchive(cat)}
                        >
                          <i className="ri-archive-line"></i>
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
      <Modal show={showModal} onHide={closeModal} centered>
        <Modal.Header closeButton>
          <Modal.Title>{editingCategory ? 'Editar categoría' : 'Nueva categoría'}</Modal.Title>
        </Modal.Header>
        <form onSubmit={handleSubmit(onSubmit)}>
          <Modal.Body>
            <div className="mb-3">
              <label htmlFor="categorieslist-nombre" className="form-label">Nombre <span className="text-danger">*</span></label>
              <input id="categorieslist-nombre"
                type="text"
                className={`form-control ${errors.name ? 'is-invalid' : ''}`}
                {...register('name', { required: 'El nombre es requerido', maxLength: { value: 150, message: 'Máximo 150 caracteres' } })}
              />
              {errors.name && <div className="invalid-feedback">{errors.name.message}</div>}
            </div>
            <div className="mb-3">
              <label htmlFor="categorieslist-descripcion" className="form-label">Descripción</label>
              <textarea id="categorieslist-descripcion"
                className="form-control"
                rows={3}
                {...register('description', { maxLength: { value: 500, message: 'Máximo 500 caracteres' } })}
              />
            </div>
            <div className="mb-0">
              <label htmlFor="categorieslist-orden" className="form-label">Orden</label>
              <input id="categorieslist-orden"
                type="number"
                className="form-control"
                {...register('sortOrder', { valueAsNumber: true, min: { value: 0, message: 'Debe ser 0 o mayor' } })}
              />
            </div>
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light" onClick={closeModal}>
              Cancelar
            </button>
            <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
              {isSubmitting ? 'Guardando...' : editingCategory ? 'Guardar cambios' : 'Crear categoría'}
            </button>
          </Modal.Footer>
        </form>
      </Modal>

      <Modal show={archiving !== null} onHide={() => !archiveBusy && setArchiving(null)} centered>
        <Modal.Header closeButton={!archiveBusy}>
          <Modal.Title>Archivar {archiving ? `“${archiving.name}”` : 'categoría'}</Modal.Title>
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

      <Modal show={showTemplatesModal} onHide={closeTemplatesModal} centered size="lg">
        <Modal.Header closeButton>
          <Modal.Title>Instalar plantilla de categorías</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {templatesLoading ? (
            <div className="text-center py-4">
              <div className="spinner-border text-primary" role="status"></div>
            </div>
          ) : (
            <div className="row g-3">
              {templates.length === 0 && (
                <div className="col-12">
                  <div className="alert alert-light border mb-0">
                    No hay plantillas disponibles para este tenant.
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
                          disabled={installingTemplateId !== null}
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
          <button type="button" className="btn btn-light" onClick={closeTemplatesModal}>
            Cerrar
          </button>
        </Modal.Footer>
      </Modal>
    </>
  );
}
