import { useState, useEffect, useRef, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import PageTitle from '../../components/common/PageTitle';
import UploadLoadingModal from '../../components/common/UploadLoadingModal';
import { productsApi } from '../../api/productsApi';
import { categoriesApi } from '../../api/categoriesApi';
import type { CreateProductRequest, CategoryDto, ProductDto, UnitType } from '../../types';
import { getApiErrorMessage } from '../../api/errors';

const UNIT_TYPE_OPTIONS = [
  { value: '', label: 'Sin unidad' },
  { value: '0', label: 'Unidad' },
  { value: '1', label: 'Paquete' },
  { value: '2', label: 'Caja' },
  { value: '3', label: 'Par' },
  { value: '10', label: 'Kg' },
  { value: '11', label: 'g' },
  { value: '12', label: 'Lb' },
  { value: '13', label: 'Oz' },
  { value: '20', label: 'Litro' },
  { value: '21', label: 'ml' },
  { value: '30', label: 'Porción' },
  { value: '31', label: 'Rebanada' },
  { value: '32', label: 'Ración' },
  { value: '40', label: 'Metro' },
  { value: '41', label: 'Rollo' },
];

export default function ProductForm() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const isEditing = !!id;
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [product, setProduct] = useState<ProductDto | null>(null);
  const [loading, setLoading] = useState(isEditing);
  const [error, setError] = useState('');
  const [imageFile, setImageFile] = useState<File | null>(null);
  const [imagePreview, setImagePreview] = useState<string | null>(null);
  const [imageUploading, setImageUploading] = useState(false);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<CreateProductRequest>({
    values: product
      ? {
          categoryId: product.categoryId,
          name: product.name,
          description: product.description || '',
          priceAmount: product.priceAmount,
          priceCurrency: product.priceCurrency,
          sku: product.sku || '',
          barcode: product.barcode || '',
          sortOrder: product.sortOrder,
          isFeatured: product.isFeatured,
          tags: product.tags || '',
          compareAtPrice: product.compareAtPrice ?? undefined,
          taxRate: product.taxRate ?? undefined,
          calories: product.calories ?? undefined,
          allergens: product.allergens || '',
          stockQuantity: product.stockQuantity ?? undefined,
          lowStockThreshold: product.lowStockThreshold ?? undefined,
          maxQuantityPerOrder: product.maxQuantityPerOrder ?? undefined,
          unit: product.unit ?? undefined,
          preparationTimeMinutes: product.preparationTimeMinutes ?? undefined,
          availableFromTime: product.availableFromTime ? product.availableFromTime.slice(0, 5) : undefined,
          availableToTime: product.availableToTime ? product.availableToTime.slice(0, 5) : undefined,
        }
      : {
          categoryId: '',
          name: '',
          description: '',
          priceAmount: 0,
          priceCurrency: 'MXN',
          sku: '',
          barcode: '',
          sortOrder: 0,
          isFeatured: false,
          tags: '',
          allergens: '',
          availableFromTime: undefined,
          availableToTime: undefined,
        },
  });

  const loadData = useCallback(async () => {
    try {
      const { data: cats } = await categoriesApi.getAll();
      setCategories(cats);

      if (id) {
        const { data: prod } = await productsApi.getById(id);
        setProduct(prod);
        if (prod.images?.medium) {
          setImagePreview(prod.images.medium);
        }
      }
    } catch {
      setError('Error al cargar los datos.');
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleImageChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setImageFile(file);
    setImagePreview(URL.createObjectURL(file));
  };

  const onSubmit = async (data: CreateProductRequest) => {
    setError('');

    // Clean optional numeric fields
    const payload: CreateProductRequest = {
      ...data,
      unit: data.unit != null && data.unit !== ('' as unknown) ? (Number(data.unit) as UnitType) : undefined,
      compareAtPrice: data.compareAtPrice ? Number(data.compareAtPrice) : undefined,
      taxRate: data.taxRate != null && data.taxRate !== ('' as unknown) ? Number(data.taxRate) : undefined,
      calories: data.calories ? Number(data.calories) : undefined,
      stockQuantity: data.stockQuantity != null && data.stockQuantity !== ('' as unknown) ? Number(data.stockQuantity) : undefined,
      lowStockThreshold: data.lowStockThreshold ? Number(data.lowStockThreshold) : undefined,
      maxQuantityPerOrder: data.maxQuantityPerOrder ? Number(data.maxQuantityPerOrder) : undefined,
      preparationTimeMinutes: data.preparationTimeMinutes ? Number(data.preparationTimeMinutes) : undefined,
      availableFromTime: data.availableFromTime ? `${data.availableFromTime}:00` : undefined,
      availableToTime: data.availableToTime ? `${data.availableToTime}:00` : undefined,
      priceAmount: Number(data.priceAmount),
      sortOrder: Number(data.sortOrder),
    };

    try {
      let productId = id;

      if (isEditing && id) {
        await productsApi.update(id, { ...payload, id }, 'Product');
      } else {
        const { data: created } = await productsApi.create(payload, 'Product');
        productId = created.id;
      }

      // Upload image if selected
      if (imageFile && productId) {
        setImageUploading(true);
        try {
          const { data: uploaded } = await productsApi.uploadImage(productId, imageFile);
          setImagePreview(uploaded.medium || uploaded.original);
        } catch {
          // Product saved, but image failed — show warning but still navigate
          alert('El producto se guardó pero hubo un error al subir la imagen.');
        } finally {
          setImageUploading(false);
        }
      }

      navigate('/products');
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al guardar el producto.');
      setError(msg);
    }
  };

  if (loading) {
    return (
      <>
        <PageTitle title={isEditing ? 'Editar Producto' : 'Nuevo Producto'} subtitle="Catálogo" />
        <div className="text-center py-4">
          <div className="spinner-border text-primary" role="status"></div>
        </div>
      </>
    );
  }

  return (
    <>
      <UploadLoadingModal
        show={imageUploading}
        title="Subiendo imagen"
        description="Estamos procesando la imagen del producto."
      />
      <PageTitle title={isEditing ? 'Editar Producto' : 'Nuevo Producto'} subtitle="Catálogo" />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError('')}></button>
        </div>
      )}

      <form onSubmit={handleSubmit(onSubmit)}>
        <div className="row">
          {/* Left Column */}
          <div className="col-xl-8">
            {/* General Info */}
            <div className="card">
              <div className="card-body">
                <h5 className="header-title mb-3">Información General</h5>

                <div className="row">
                  <div className="col-md-8 mb-3">
                    <label className="form-label">Nombre <span className="text-danger">*</span></label>
                    <input
                      type="text"
                      className={`form-control ${errors.name ? 'is-invalid' : ''}`}
                      {...register('name', { required: 'El nombre es requerido', maxLength: { value: 200, message: 'Máximo 200 caracteres' } })}
                    />
                    {errors.name && <div className="invalid-feedback">{errors.name.message}</div>}
                  </div>
                  <div className="col-md-4 mb-3">
                    <label className="form-label">Categoría <span className="text-danger">*</span></label>
                    <select
                      className={`form-select ${errors.categoryId ? 'is-invalid' : ''}`}
                      {...register('categoryId', { required: 'Selecciona una categoría' })}
                    >
                      <option value="">Seleccionar...</option>
                      {categories.map((c) => (
                        <option key={c.id} value={c.id}>{c.name}</option>
                      ))}
                    </select>
                    {errors.categoryId && <div className="invalid-feedback">{errors.categoryId.message}</div>}
                  </div>
                </div>

                <div className="mb-3">
                  <label className="form-label">Descripción</label>
                  <textarea
                    className="form-control"
                    rows={3}
                    {...register('description', { maxLength: { value: 1000, message: 'Máximo 1000 caracteres' } })}
                  />
                </div>

                <div className="row">
                  <div className="col-md-4 mb-3">
                    <label className="form-label">Unidad</label>
                    <select className="form-select" {...register('unit')}>
                      {UNIT_TYPE_OPTIONS.map((opt) => (
                        <option key={opt.value} value={opt.value}>{opt.label}</option>
                      ))}
                    </select>
                  </div>
                  <div className="col-md-4 mb-3">
                    <label className="form-label">Tags</label>
                    <input
                      type="text"
                      className="form-control"
                      placeholder="ej: popular, nuevo"
                      {...register('tags', { maxLength: { value: 500, message: 'Máximo 500 caracteres' } })}
                    />
                  </div>
                  <div className="col-md-2 mb-3">
                    <label className="form-label">Orden</label>
                    <input
                      type="number"
                      className="form-control"
                      {...register('sortOrder', { valueAsNumber: true })}
                    />
                  </div>
                  <div className="col-md-2 mb-3 d-flex align-items-end">
                    <div className="form-check">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="isFeatured"
                        {...register('isFeatured')}
                      />
                      <label className="form-check-label" htmlFor="isFeatured">Destacado</label>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            {/* Pricing */}
            <div className="card">
              <div className="card-body">
                <h5 className="header-title mb-3">Precios</h5>

                <div className="row">
                  <div className="col-md-3 mb-3">
                    <label className="form-label">Precio <span className="text-danger">*</span></label>
                    <input
                      type="number"
                      step="0.01"
                      className={`form-control ${errors.priceAmount ? 'is-invalid' : ''}`}
                      {...register('priceAmount', {
                        required: 'El precio es requerido',
                        valueAsNumber: true,
                        min: { value: 0.01, message: 'Debe ser mayor a 0' },
                      })}
                    />
                    {errors.priceAmount && <div className="invalid-feedback">{errors.priceAmount.message}</div>}
                  </div>
                  <div className="col-md-3 mb-3">
                    <label className="form-label">Moneda</label>
                    <select className="form-select" {...register('priceCurrency')}>
                      <option value="MXN">MXN</option>
                      <option value="USD">USD</option>
                    </select>
                  </div>
                  <div className="col-md-3 mb-3">
                    <label className="form-label">Precio anterior</label>
                    <input
                      type="number"
                      step="0.01"
                      className="form-control"
                      placeholder="Precio tachado"
                      {...register('compareAtPrice')}
                    />
                  </div>
                  <div className="col-md-3 mb-3">
                    <label className="form-label">Impuesto (%)</label>
                    <input
                      type="number"
                      step="0.01"
                      className="form-control"
                      placeholder="ej: 16"
                      {...register('taxRate')}
                    />
                  </div>
                </div>
              </div>
            </div>

            {/* Inventory */}
            <div className="card">
              <div className="card-body">
                <h5 className="header-title mb-3">Inventario</h5>

                <div className="row">
                  <div className="col-md-4 mb-3">
                    <label className="form-label">SKU</label>
                    <input
                      type="text"
                      className="form-control"
                      {...register('sku', { maxLength: { value: 100, message: 'Máximo 100 caracteres' } })}
                    />
                  </div>
                  <div className="col-md-4 mb-3">
                    <label className="form-label">Código de barras</label>
                    <input
                      type="text"
                      className="form-control"
                      {...register('barcode', { maxLength: { value: 50, message: 'Máximo 50 caracteres' } })}
                    />
                  </div>
                  <div className="col-md-4 mb-3">
                    <label className="form-label">Máx. por orden</label>
                    <input
                      type="number"
                      className="form-control"
                      {...register('maxQuantityPerOrder')}
                    />
                  </div>
                </div>

                <div className="row">
                  <div className="col-md-6 mb-3">
                    <label className="form-label">Cantidad en stock</label>
                    <input
                      type="number"
                      className="form-control"
                      {...register('stockQuantity')}
                    />
                  </div>
                  <div className="col-md-6 mb-3">
                    <label className="form-label">Alerta stock bajo</label>
                    <input
                      type="number"
                      className="form-control"
                      {...register('lowStockThreshold')}
                    />
                  </div>
                </div>
              </div>
            </div>

            {/* Food Details */}
            <div className="card">
              <div className="card-body">
                <h5 className="header-title mb-3">Detalles y Disponibilidad</h5>

                <div className="row">
                  <div className="col-md-6 mb-3">
                    <label className="form-label">Disponible desde</label>
                    <input
                      type="time"
                      className="form-control"
                      {...register('availableFromTime')}
                    />
                  </div>
                  <div className="col-md-6 mb-3">
                    <label className="form-label">Disponible hasta</label>
                    <input
                      type="time"
                      className="form-control"
                      {...register('availableToTime')}
                    />
                  </div>
                </div>

                <div className="row">
                  <div className="col-md-4 mb-3">
                    <label className="form-label">Tiempo preparación (min)</label>
                    <input
                      type="number"
                      className="form-control"
                      {...register('preparationTimeMinutes')}
                    />
                  </div>
                  <div className="col-md-4 mb-3">
                    <label className="form-label">Calorías</label>
                    <input
                      type="number"
                      className="form-control"
                      {...register('calories')}
                    />
                  </div>
                  <div className="col-md-4 mb-3">
                    <label className="form-label">Alérgenos</label>
                    <input
                      type="text"
                      className="form-control"
                      placeholder="ej: gluten, lácteos"
                      {...register('allergens', { maxLength: { value: 500, message: 'Máximo 500 caracteres' } })}
                    />
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* Right Column */}
          <div className="col-xl-4">
            {/* Image */}
            <div className="card">
              <div className="card-body">
                <h5 className="header-title mb-3">Imagen del Producto</h5>

                {imagePreview && (
                  <div className="text-center mb-3">
                    <img
                      src={imagePreview}
                      alt="preview"
                      className="img-fluid rounded"
                      style={{ maxHeight: 200, objectFit: 'cover' }}
                    />
                  </div>
                )}

                <input
                  type="file"
                  ref={fileInputRef}
                  className="d-none"
                  accept="image/jpeg,image/png,image/webp,image/gif"
                  onChange={handleImageChange}
                />
                <button
                  type="button"
                  className="btn btn-soft-primary w-100"
                  onClick={() => fileInputRef.current?.click()}
                >
                  <i className="ri-upload-2-line me-1"></i>
                  {imagePreview ? 'Cambiar Imagen' : 'Subir Imagen'}
                </button>

                {imageFile && (
                  <p className="text-muted font-12 mt-2 mb-0">
                    Archivo: {imageFile.name} ({(imageFile.size / 1024).toFixed(0)} KB)
                  </p>
                )}
              </div>
            </div>

            {/* Actions */}
            <div className="card">
              <div className="card-body">
                <h5 className="header-title mb-3">Acciones</h5>

                <div className="d-grid gap-2">
                  <button
                    type="submit"
                    className="btn btn-primary"
                    disabled={isSubmitting}
                  >
                    {isSubmitting
                      ? 'Guardando...'
                      : isEditing
                        ? 'Guardar Cambios'
                        : 'Crear Producto'}
                  </button>
                  <Link to="/products" className="btn btn-light">
                    Cancelar
                  </Link>
                </div>
              </div>
            </div>
          </div>
        </div>
      </form>
    </>
  );
}
