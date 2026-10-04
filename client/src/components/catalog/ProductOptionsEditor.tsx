import type { ProductOptionGroupInput } from '../../api/productsApi';
import { formatMoney } from '../../utils/money';

interface ProductOptionsEditorProps {
  groups: ProductOptionGroupInput[];
  onChange: (groups: ProductOptionGroupInput[]) => void;
}

// Same limits as the API (Product.MaxOptionGroups, ProductOptionGroup.MaxOptions).
const MAX_GROUPS = 10;
const MAX_OPTIONS = 30;

type GroupKind = 'required-one' | 'optional-one' | 'multiple';

function kindOf(group: ProductOptionGroupInput): GroupKind {
  if (group.maxSelections > 1) return 'multiple';
  return group.minSelections >= 1 ? 'required-one' : 'optional-one';
}

const TEMPLATES: Record<string, ProductOptionGroupInput> = {
  size: {
    name: 'Tamaño',
    minSelections: 1,
    maxSelections: 1,
    options: [
      { name: 'Chico', priceDelta: 0, isAvailable: true },
      { name: 'Mediano', priceDelta: 10, isAvailable: true },
      { name: 'Grande', priceDelta: 20, isAvailable: true },
    ],
  },
  extras: {
    name: 'Extras',
    minSelections: 0,
    maxSelections: 3,
    options: [
      { name: 'Queso extra', priceDelta: 10, isAvailable: true },
      { name: 'Tocino', priceDelta: 15, isAvailable: true },
      { name: 'Aguacate', priceDelta: 12, isAvailable: true },
    ],
  },
};

/**
 * Variants and extras of a product: a required single choice works as a variant (size, type of milk) and
 * an optional multiple choice as extras. Each option adds its price to the product's.
 */
export default function ProductOptionsEditor({ groups, onChange }: ProductOptionsEditorProps) {
  const updateGroup = (index: number, changes: Partial<ProductOptionGroupInput>) =>
    onChange(groups.map((group, i) => (i === index ? { ...group, ...changes } : group)));

  const setKind = (index: number, kind: GroupKind) => {
    const group = groups[index];
    if (kind === 'required-one') updateGroup(index, { minSelections: 1, maxSelections: 1 });
    else if (kind === 'optional-one') updateGroup(index, { minSelections: 0, maxSelections: 1 });
    else updateGroup(index, { minSelections: 0, maxSelections: Math.max(2, group.options.length) });
  };

  const updateOption = (groupIndex: number, optionIndex: number, changes: Partial<ProductOptionGroupInput['options'][number]>) =>
    updateGroup(groupIndex, {
      options: groups[groupIndex].options.map((option, i) => (i === optionIndex ? { ...option, ...changes } : option)),
    });

  const removeOption = (groupIndex: number, optionIndex: number) => {
    const group = groups[groupIndex];
    const options = group.options.filter((_, i) => i !== optionIndex);
    updateGroup(groupIndex, {
      options,
      maxSelections: Math.min(group.maxSelections, Math.max(1, options.length)),
      minSelections: Math.min(group.minSelections, Math.max(0, options.length)),
    });
  };

  return (
    <div className="ta-options-editor">
      {groups.length === 0 && (
        <p className="text-muted small mb-2">
          Sin opciones. Agrega un grupo para ofrecer tamaños, tipos de leche, términos o extras con precio.
        </p>
      )}

      {groups.map((group, groupIndex) => {
        const kind = kindOf(group);
        return (
          <fieldset key={group.id ?? `new-${groupIndex}`} className="border rounded p-2 mb-2">
            <legend className="visually-hidden">Grupo {group.name || groupIndex + 1}</legend>
            <div className="row g-2 align-items-end">
              <div className="col-sm-5">
                <label className="form-label small mb-1" htmlFor={`option-group-${groupIndex}`}>Nombre del grupo</label>
                <input
                  id={`option-group-${groupIndex}`}
                  className="form-control form-control-sm"
                  maxLength={80}
                  placeholder="Ej. Tamaño"
                  value={group.name}
                  onChange={(event) => updateGroup(groupIndex, { name: event.target.value })}
                />
              </div>
              <div className="col-sm-4">
                <label className="form-label small mb-1" htmlFor={`option-kind-${groupIndex}`}>Se elige</label>
                <select
                  id={`option-kind-${groupIndex}`}
                  className="form-select form-select-sm"
                  value={kind}
                  onChange={(event) => setKind(groupIndex, event.target.value as GroupKind)}
                >
                  <option value="required-one">Una, obligatoria</option>
                  <option value="optional-one">Una, opcional</option>
                  <option value="multiple">Varias, opcionales</option>
                </select>
              </div>
              {kind === 'multiple' && (
                <div className="col-sm-2">
                  <label className="form-label small mb-1" htmlFor={`option-max-${groupIndex}`}>Máximo</label>
                  <input
                    id={`option-max-${groupIndex}`}
                    type="number"
                    min={1}
                    max={group.options.length}
                    className="form-control form-control-sm"
                    value={group.maxSelections}
                    onChange={(event) => updateGroup(groupIndex, {
                      maxSelections: Math.min(Math.max(1, Number(event.target.value) || 1), group.options.length),
                    })}
                  />
                </div>
              )}
              <div className="col-auto ms-auto">
                <button
                  type="button"
                  className="btn btn-sm btn-light text-danger"
                  onClick={() => onChange(groups.filter((_, i) => i !== groupIndex))}
                  aria-label={`Quitar el grupo ${group.name}`}
                >
                  <i className="ri-delete-bin-line" aria-hidden="true"></i>
                </button>
              </div>
            </div>

            <ul className="list-unstyled mt-2 mb-1">
              {group.options.map((option, optionIndex) => (
                <li key={option.id ?? `new-${optionIndex}`} className="d-flex flex-wrap align-items-center gap-2 py-1">
                  <input
                    className="form-control form-control-sm flex-grow-1"
                    style={{ minWidth: '9rem', maxWidth: '18rem' }}
                    maxLength={80}
                    placeholder="Opción"
                    aria-label={`Nombre de la opción ${optionIndex + 1} de ${group.name}`}
                    value={option.name}
                    onChange={(event) => updateOption(groupIndex, optionIndex, { name: event.target.value })}
                  />
                  <div className="input-group input-group-sm" style={{ maxWidth: '9rem' }}>
                    <span className="input-group-text">$</span>
                    <input
                      type="number"
                      step="0.01"
                      className="form-control ta-money"
                      aria-label={`Precio extra de ${option.name || 'la opción'}`}
                      value={Number.isFinite(option.priceDelta) ? option.priceDelta : ''}
                      onChange={(event) => updateOption(groupIndex, optionIndex, { priceDelta: event.target.value === '' ? 0 : Number(event.target.value) })}
                    />
                  </div>
                  <div className="form-check form-switch mb-0">
                    <input
                      id={`option-available-${groupIndex}-${optionIndex}`}
                      type="checkbox"
                      role="switch"
                      className="form-check-input"
                      checked={option.isAvailable}
                      onChange={(event) => updateOption(groupIndex, optionIndex, { isAvailable: event.target.checked })}
                    />
                    <label className="form-check-label small" htmlFor={`option-available-${groupIndex}-${optionIndex}`}>Disponible</label>
                  </div>
                  <button
                    type="button"
                    className="btn btn-sm btn-link text-danger px-1"
                    onClick={() => removeOption(groupIndex, optionIndex)}
                    aria-label={`Quitar ${option.name || 'la opción'}`}
                  >
                    <i className="ri-close-line" aria-hidden="true"></i>
                  </button>
                </li>
              ))}
            </ul>
            <button
              type="button"
              className="btn btn-sm btn-light"
              disabled={group.options.length >= MAX_OPTIONS}
              onClick={() => updateGroup(groupIndex, { options: [...group.options, { name: '', priceDelta: 0, isAvailable: true }] })}
            >
              <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar opción
            </button>
            {group.options.some((option) => option.priceDelta !== 0) && (
              <span className="small text-muted ms-2">
                Ej. {group.options[0]?.name || 'la primera'}: {formatMoney(group.options[0]?.priceDelta ?? 0)} sobre el precio
              </span>
            )}
          </fieldset>
        );
      })}

      <div className="d-flex flex-wrap gap-2">
        <button
          type="button"
          className="btn btn-sm btn-outline-primary"
          disabled={groups.length >= MAX_GROUPS}
          onClick={() => onChange([...groups, { name: '', minSelections: 0, maxSelections: 1, options: [{ name: '', priceDelta: 0, isAvailable: true }] }])}
        >
          <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar grupo
        </button>
        {!groups.some((group) => group.name === TEMPLATES.size.name) && (
          <button type="button" className="btn btn-sm btn-light" disabled={groups.length >= MAX_GROUPS}
            onClick={() => onChange([...groups, structuredClone(TEMPLATES.size)])}>
            + Tamaños
          </button>
        )}
        {!groups.some((group) => group.name === TEMPLATES.extras.name) && (
          <button type="button" className="btn btn-sm btn-light" disabled={groups.length >= MAX_GROUPS}
            onClick={() => onChange([...groups, structuredClone(TEMPLATES.extras)])}>
            + Extras
          </button>
        )}
      </div>
    </div>
  );
}
