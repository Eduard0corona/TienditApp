import { useState, useEffect, useCallback } from 'react';
import { useForm } from 'react-hook-form';
import { Modal } from 'react-bootstrap';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { categoriesApi } from '../../api/categoriesApi';
import type { CategoryDto, CreateCategoryRequest } from '../../types';

const COLUMNS = [
  { header: 'Nombre' },
  { header: 'Descripción' },
  { header: 'Orden', className: 'text-center' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

export default function CategoriesList() {
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showModal, setShowModal] = useState(false);
  const [editingCategory, setEditingCategory] = useState<CategoryDto | null>(null);

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

  const openEditModal = (category: CategoryDto) => {
    setEditingCategory(category);
    setShowModal(true);
  };

  const closeModal = () => {
    setShowModal(false);
    setEditingCategory(null);
  };

  const onSubmit = async (data: CreateCategoryRequest) => {
    try {
      if (editingCategory) {
        await categoriesApi.update(editingCategory.id, { ...data, id: editingCategory.id });
      } else {
        await categoriesApi.create(data);
      }
      closeModal();
      await loadCategories();
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        'Error al guardar la categoría.';
      setError(msg);
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
    if (!window.confirm('¿Deseas eliminar esta categoría?')) return;
    try {
      await categoriesApi.delete(id);
      await loadCategories();
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        'Error al eliminar la categoría.';
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
                  <div className="text-sm-end">
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
    </>
  );
}
