import { useState, useEffect, useRef, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import PageTitle from '../../components/common/PageTitle';
import UploadLoadingModal from '../../components/common/UploadLoadingModal';
import { productsApi } from '../../api/productsApi';
import { categoriesApi } from '../../api/categoriesApi';
import type { CreateProductRequest, CategoryDto, ProductDto, UnitType } from '../../types';
import { CfdiTaxObject } from '../../types';
import SatFiscalFields from '../../components/catalog/SatFiscalFields';
import { satProductCodeRules, satUnitCodeRules, toSatCodePayload } from '../../utils/satCodes';
import { getApiErrorMessage } from '../../api/errors';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { fromVatOption, getVatOptions, toVatOption, type VatOption } from '../../utils/vatOptions';
import { getDefaultSatUnitCode } from '../../utils/units';
import ProductOptionsEditor from '../../components/catalog/ProductOptionsEditor';
import ProductStockCard from '../../components/catalog/ProductStockCard';
import type { ProductOptionGroupInput } from '../../api/productsApi';
import { getOptionGroupsError, toOptionGroupInputs } from '../../utils/productOptions';
import { getAccess } from '../../utils/access';

type ProductFormValues = CreateProductRequest & { vatOption: VatOption };

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
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const access = getAccess(user, capabilities);
  const toast = useToast();

  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [product, setProduct] = useState<ProductDto | null>(null);
  const [optionGroups, setOptionGroups] = useState<ProductOptionGroupInput[]>([]);
  // Saved only when edited, so a plain product update never rewrites them.
  const [optionsTouched, setOptionsTouched] = useState(false);
  const [loading, setLoading] = useState(isEditing);
  const [error, setError] = useState('');
  const [imageFile, setImageFile] = useState<File | null>(null);
  const [imagePreview, setImagePreview] = useState<string | null>(null);
  const [imageUploading, setImageUploading] = useState(false);

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<ProductFormValues>({
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
          vatOption: toVatOption(product),
          satProductCode: product.satProductCode ?? '',
          satUnitCode: product.satUnitCode ?? '',
          taxObject: product.taxObject ?? CfdiTaxObject.Subject,
          calories: product.calories ?? undefined,
          allergens: product.allergens || '',
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
          vatOption: '',
          satProductCode: '',
          satUnitCode: '',
          taxObject: CfdiTaxObject.Subject,
          allergens: '',
          availableFromTime: undefined,
          availableToTime: undefined,
        },
  });

  const watchedUnit = watch('unit');
  const selectedUnit = watchedUnit != null && String(watchedUnit) !== '' ? (Number(watchedUnit) as UnitType) : undefined;

  const loadData = useCallback(async () => {
    try {
      const { data: cats } = await categoriesApi.getAll();
      setCategories(cats);

      if (id) {
        const { data: prod } = await productsApi.getById(id);
        setProduct(prod);
        setOptionGroups(toOptionGroupInputs(prod.optionGroups));
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

  const onSubmit = async ({ vatOption, ...data }: ProductFormValues) => {
    setError('');
    const optionsError = optionsTouched ? getOptionGroupsError(optionGroups) : null;
    if (optionsError) {
      setError(optionsError);
      return;
    }

    // Clean optional numeric fields
    const payload: CreateProductRequest = {
      ...data,
      ...fromVatOption(vatOption),
      // Sent every time: an update without them would go back to the generic codes.
      satProductCode: toSatCodePayload(data.satProductCode),
      satUnitCode: toSatCodePayload(data.satUnitCode),
      taxObject: Number(data.taxObject ?? CfdiTaxObject.Subject) as CfdiTaxObject,
      unit: data.unit != null && data.unit !== ('' as unknown) ? (Number(data.unit) as UnitType) : undefined,
      compareAtPrice: data.compareAtPrice ? Number(data.compareAtPrice) : undefined,
      calories: data.calories ? Number(data.calories) : undefined,
      maxQuantityPerOrder: data.maxQuantityPerOrder ? Number(data.maxQuantityPerOrder) : undefined,
      preparationTimeMinutes: data.preparationTimeMinutes ? Number(data.preparationTimeMinutes) : undefined,
      availableFromTime: data.availableFromTime ? `${data.availableFromTime}:00` : undefined,
      availableToTime: data.availableToTime ? `${data.availableToTime}:00` : undefined,
      priceAmount: Number(data.priceAmount),
      // Every amount is charged and shown in pesos.
      priceCurrency: 'MXN',
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

      if (optionsTouched && productId) {
        await productsApi.setOptionGroups(productId, optionGroups.map((group) => ({
          ...group,
          name: group.name.trim(),
          options: group.options.map((option) => ({ ...option, name: option.name.trim() })),
        })));
      }

      // Upload image if selected
      if (imageFile && productId) {
        setImageUploading(true);
        try {
          const { data: uploaded } = await productsApi.uploadImage(productId, imageFile);
          setImagePreview(uploaded.medium || uploaded.original);
        } catch {
          // Product saved, but image failed — show warning but still navigate
          toast.warning('El producto se guardó, pero la imagen no se pudo subir. Inténtalo de nuevo desde Editar.');
        } finally {
          setImageUploading(false);
        }
      }

      toast.success(isEditing ? 'Producto actualizado.' : 'Producto creado.');
      navigate('/products');
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al guardar el producto.');
      setError(msg);
    }
  };

  if (loading) {
    return (
      <>
        <PageTitle title={isEditing ? 'Editar producto' : 'Nuevo producto'} parent={{ label: 'Productos', to: '/products' }} />
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
      <PageTitle title={isEditing ? 'Editar producto' : 'Nuevo producto'} parent={{ label: 'Productos', to: '/products' }} />

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
                <h5 className="header-title mb-3">Información general</h5>

                <div className="row">
                  <div className="col-md-8 mb-3">
                    <label htmlFor="productform-nombre" className="form-label">Nombre <span className="text-danger">*</span></label>
                    <input id="productform-nombre"
                      type="text"
                      className={`form-control ${errors.name ? 'is-invalid' : ''}`}
                      {...register('name', { required: 'El nombre es requerido', maxLength: { value: 200, message: 'Máximo 200 caracteres' } })}
                    />
                    {errors.name && <div className="invalid-feedback">{errors.name.message}</div>}
                  </div>
                  <div className="col-md-4 mb-3">
                    <label htmlFor="productform-categoria" className="form-label">Categoría <span className="text-danger">*</span></label>
                    <select id="productform-categoria"
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
                  <label htmlFor="productform-descripcion" className="form-label">Descripción</label>
                  <textarea id="productform-descripcion"
                    className="form-control"
                    rows={3}
                    {...register('description', { maxLength: { value: 1000, message: 'Máximo 1000 caracteres' } })}
                  />
                </div>

                <div className="row">
                  <div className="col-md-4 mb-3">
                    <label htmlFor="productform-unidad" className="form-label">Unidad</label>
                    <select id="productform-unidad" className="form-select" {...register('unit')}>
                      {UNIT_TYPE_OPTIONS.map((opt) => (
                        <option key={opt.value} value={opt.value}>{opt.label}</option>
                      ))}
                    </select>
                  </div>
                  <div className="col-md-4 mb-3">
                    <label htmlFor="productform-tags" className="form-label">Etiquetas</label>
                    <input id="productform-tags"
                      type="text"
                      className="form-control"
                      placeholder="ej: popular, nuevo"
                      {...register('tags', { maxLength: { value: 500, message: 'Máximo 500 caracteres' } })}
                    />
                  </div>
                  <div className="col-md-2 mb-3">
                    <label htmlFor="productform-orden" className="form-label">Orden</label>
                    <input id="productform-orden"
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
                    <label htmlFor="productform-precio" className="form-label">Precio <span className="text-danger">*</span></label>
                    <input id="productform-precio"
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
                    <label htmlFor="productform-precio-anterior" className="form-label">Precio anterior</label>
                    <input id="productform-precio-anterior"
                      type="number"
                      step="0.01"
                      className="form-control"
                      placeholder="Precio tachado"
                      {...register('compareAtPrice')}
                    />
                  </div>
                  <div className="col-md-3 mb-3">
                    <label className="form-label" htmlFor="product-vat">IVA</label>
                    <select id="product-vat" className="form-select" {...register('vatOption')}>
                      {getVatOptions(tenant?.defaultTaxRate ?? 16).map((option) => (
                        <option key={option.value} value={option.value}>{option.label}</option>
                      ))}
                    </select>
                    <div className="form-text">
                      {tenant?.pricesIncludeTax ? 'El precio ya incluye el IVA.' : 'El IVA se suma al precio.'}
                    </div>
                  </div>
                </div>
                <SatFiscalFields
                  idPrefix="product"
                  productCode={register('satProductCode', satProductCodeRules)}
                  unitCode={register('satUnitCode', satUnitCodeRules)}
                  taxObject={register('taxObject')}
                  defaultUnitCode={getDefaultSatUnitCode(selectedUnit)}
                  errors={{ productCode: errors.satProductCode?.message, unitCode: errors.satUnitCode?.message }}
                />
              </div>
            </div>

            {/* Variants and extras */}
            <div className="card">
              <div className="card-body">
                <h5 className="header-title mb-1">Opciones y extras</h5>
                <p className="text-muted small mb-3">
                  Tamaños, tipos de leche, términos o ingredientes extra. En Vender se eligen al tocar el producto y
                  su precio se suma al del producto.
                </p>
                <ProductOptionsEditor
                  groups={optionGroups}
                  onChange={(groups) => {
                    setOptionGroups(groups);
                    setOptionsTouched(true);
                  }}
                />
              </div>
            </div>

            {/* Codes */}
            <div className="card">
              <div className="card-body">
                <h5 className="header-title mb-3">Códigos y límites</h5>

                <div className="row">
                  <div className="col-md-4 mb-3">
                    <label htmlFor="productform-sku" className="form-label">SKU</label>
                    <input id="productform-sku"
                      type="text"
                      className="form-control"
                      {...register('sku', { maxLength: { value: 100, message: 'Máximo 100 caracteres' } })}
                    />
                  </div>
                  <div className="col-md-4 mb-3">
                    <label htmlFor="productform-codigo-de-barras" className="form-label">Código de barras</label>
                    <input id="productform-codigo-de-barras"
                      type="text"
                      className="form-control"
                      {...register('barcode', { maxLength: { value: 50, message: 'Máximo 50 caracteres' } })}
                    />
                  </div>
                  <div className="col-md-4 mb-3">
                    <label htmlFor="productform-max-por-orden" className="form-label">Máx. por orden</label>
                    <input id="productform-max-por-orden"
                      type="number"
                      className="form-control"
                      {...register('maxQuantityPerOrder')}
                    />
                  </div>
                </div>
              </div>
            </div>

            {/* Stock: one model, the inventory ledger */}
            {isEditing && product ? (
              <ProductStockCard product={product} canManage={access.inventory} />
            ) : (
              <div className="card">
                <div className="card-body">
                  <h5 className="header-title mb-1">Existencias</h5>
                  <p className="text-muted small mb-0">Guarda el producto para controlar sus existencias.</p>
                </div>
              </div>
            )}

            {/* Food Details */}
            <div className="card">
              <div className="card-body">
                <h5 className="header-title mb-3">Detalles y disponibilidad</h5>

                <div className="row">
                  <div className="col-md-6 mb-3">
                    <label htmlFor="productform-disponible-desde" className="form-label">Disponible desde</label>
                    <input id="productform-disponible-desde"
                      type="time"
                      className="form-control"
                      {...register('availableFromTime')}
                    />
                  </div>
                  <div className="col-md-6 mb-3">
                    <label htmlFor="productform-disponible-hasta" className="form-label">Disponible hasta</label>
                    <input id="productform-disponible-hasta"
                      type="time"
                      className="form-control"
                      {...register('availableToTime')}
                    />
                  </div>
                </div>

                <div className="row">
                  <div className="col-md-4 mb-3">
                    <label htmlFor="productform-tiempo-preparacion-min" className="form-label">Tiempo preparación (min)</label>
                    <input id="productform-tiempo-preparacion-min"
                      type="number"
                      className="form-control"
                      {...register('preparationTimeMinutes')}
                    />
                  </div>
                  <div className="col-md-4 mb-3">
                    <label htmlFor="productform-calorias" className="form-label">Calorías</label>
                    <input id="productform-calorias"
                      type="number"
                      className="form-control"
                      {...register('calories')}
                    />
                  </div>
                  <div className="col-md-4 mb-3">
                    <label htmlFor="productform-alergenos" className="form-label">Alérgenos</label>
                    <input id="productform-alergenos"
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
                <h5 className="header-title mb-3">Imagen del producto</h5>

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
                  {imagePreview ? 'Cambiar imagen' : 'Subir imagen'}
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
                        ? 'Guardar cambios'
                        : 'Crear producto'}
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
