import { useMemo, useState } from 'react';
import { useScrollFade } from '../../hooks/useScrollFade';
import type { SupplyItemDto } from '../../types';

interface SupplyPickerProps {
  /** The active items of the business. */
  supplies: SupplyItemDto[];
  selected: string[];
  onChange: (selected: string[]) => void;
  /** Tells apart the ids of the boxes of two pickers on the screen. */
  idPrefix: string;
}

/** Items to choose by name, code or barcode, each with its box. */
export default function SupplyPicker({ supplies, selected, onChange, idPrefix }: SupplyPickerProps) {
  const [search, setSearch] = useState('');
  const visible = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) return supplies;
    return supplies.filter((supply) => [supply.name, supply.code, supply.sku, supply.barcode]
      .some((value) => value?.toLowerCase().includes(term)));
  }, [search, supplies]);
  const { ref, fadeClass } = useScrollFade<HTMLDivElement>(visible.length, 'y');
  const chosen = new Set(selected);

  const toggle = (id: string) => onChange(chosen.has(id) ? selected.filter((item) => item !== id) : [...selected, id]);

  return (
    <div>
      <label htmlFor={`${idPrefix}-buscar`} className="form-label">Buscar artículo</label>
      <input
        id={`${idPrefix}-buscar`}
        type="search"
        className="form-control mb-2"
        placeholder="Nombre, código o código de barras"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      <div ref={ref} className={`border rounded p-2 overflow-auto ta-scroll-fade-y ${fadeClass}`} style={{ maxHeight: '16rem' }}>
        {visible.length === 0 && <p className="text-muted small mb-0">No hay artículos con esa búsqueda.</p>}
        {visible.map((supply) => (
          <div key={supply.id} className="form-check">
            <input
              id={`${idPrefix}-${supply.id}`}
              type="checkbox"
              className="form-check-input"
              checked={chosen.has(supply.id)}
              onChange={() => toggle(supply.id)}
            />
            <label htmlFor={`${idPrefix}-${supply.id}`} className="form-check-label">
              {supply.name}
              {(supply.code || supply.barcode) && (
                <span className="text-muted small ms-1">{[supply.code, supply.barcode].filter(Boolean).join(' · ')}</span>
              )}
            </label>
          </div>
        ))}
      </div>
      <div className="form-text">{selected.length === 1 ? '1 artículo elegido' : `${selected.length} artículos elegidos`}</div>
    </div>
  );
}
