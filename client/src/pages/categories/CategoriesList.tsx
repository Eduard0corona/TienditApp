import { useState, useEffect, useCallback } from 'react';
import { useForm } from 'react-hook-form';
import { Modal } from 'react-bootstrap';
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

const COLUMNS = [
  { header: 'Nombre' },
  { header: 'Descripción' },
  { header: 'Orden', className: 'text-center' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

export default function CategoriesList() {
  const { confirm } = useConfirm();
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [loading, setLoading] = useState(true);
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

  const loadCategories = useCallback(async () => {
    try {
      const { data } = await categoriesApi.getAll();
      setCategories(data);
    } catch {
      setError('Error al cargar las categorías.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadCategories();
  }, [loadCategories]);

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

  const handleToggleActive = async (category: CategoryDto) => {
    try {
      await categoriesApi.toggleActive(category.id, !category.isActive);
      await loadCategories();
    } catch {
      setError('Error al cambiar el estado de la categoría.');
    }
  };

  const handleDelete = async (id: string) => {
    const category = categories.find((item) => item.id === id);
    const accepted = await confirm({
      title: 'Eliminar categoría',
      message: `Se eliminará ${category ? `“${category.name}”` : 'la categoría'}.`,
      confirmLabel: 'Eliminar',
      danger: true,
    });
    if (!accepted) return;
    try {
      await categoriesApi.delete(id);
      await loadCategories();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al eliminar la categoría.');
      setError(msg);
    }
  };

  return (
    <>
      <PageTitle title="Categorías" subtitle="Catálogo" />

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              <div className="row mb-2">
                <div className="col-sm-5">
                  <h4 className="header-title mt-0">Listado de Categorías</h4>
                </div>
                <div className="col-sm-7">
                  <div className="text-sm-end d-flex justify-content-sm-end gap-2">
                    <button className="btn btn-soft-primary" onClick={openTemplatesModal}>
                      <i className="ri-layout-grid-line me-1"></i> Instalar Plantilla
                    </button>
                    <button className="btn btn-primary" onClick={openCreateModal}>
                      <i className="ri-add-line me-1"></i> Nueva Categoría
                    </button>
                  </div>
                </div>
              </div>

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
                loading={loading}
                emptyMessage="No hay categorías registradas."
                searchFilter={(cat, search) =>
                  cat.name.toLowerCase().includes(search) ||
                  (cat.description || '').toLowerCase().includes(search)
                }
                renderRow={(cat) => (
                  <>
                    <td className="fw-semibold">{cat.name}</td>
                    <td className="text-muted">{cat.description || '-'}</td>
                    <td className="text-center">{cat.sortOrder}</td>
                    <td className="text-center">
                      <div className="form-check form-switch d-inline-block">
                        <input
                          type="checkbox"
                          className="form-check-input"
                          checked={cat.isActive}
                          onChange={() => handleToggleActive(cat)}
                        />
                      </div>
                    </td>
                    <td className="text-center">
                      <button
                        className="btn btn-sm btn-soft-primary me-1"
                        title="Editar"
                        onClick={() => openEditModal(cat)}
                      >
                        <i className="ri-pencil-line"></i>
                      </button>
                      <button
                        className="btn btn-sm btn-soft-danger"
                        title="Eliminar"
                        onClick={() => handleDelete(cat.id)}
                      >
                        <i className="ri-delete-bin-line"></i>
                      </button>
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
          <Modal.Title>{editingCategory ? 'Editar Categoría' : 'Nueva Categoría'}</Modal.Title>
        </Modal.Header>
        <form onSubmit={handleSubmit(onSubmit)}>
          <Modal.Body>
            <div className="mb-3">
              <label className="form-label">Nombre <span className="text-danger">*</span></label>
              <input
                type="text"
                className={`form-control ${errors.name ? 'is-invalid' : ''}`}
                {...register('name', { required: 'El nombre es requerido', maxLength: { value: 150, message: 'Máximo 150 caracteres' } })}
              />
              {errors.name && <div className="invalid-feedback">{errors.name.message}</div>}
            </div>
            <div className="mb-3">
              <label className="form-label">Descripción</label>
              <textarea
                className="form-control"
                rows={3}
                {...register('description', { maxLength: { value: 500, message: 'Máximo 500 caracteres' } })}
              />
            </div>
            <div className="mb-0">
              <label className="form-label">Orden</label>
              <input
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
              {isSubmitting ? 'Guardando...' : editingCategory ? 'Guardar Cambios' : 'Crear Categoría'}
            </button>
          </Modal.Footer>
        </form>
      </Modal>

      <Modal show={showTemplatesModal} onHide={closeTemplatesModal} centered size="lg">
        <Modal.Header closeButton>
          <Modal.Title>Instalar Plantilla de Categorías</Modal.Title>
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
