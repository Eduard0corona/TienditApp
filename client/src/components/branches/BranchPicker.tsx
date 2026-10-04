import type { Branch } from '../../api/branchesApi';

interface BranchPickerProps {
  branches: Branch[];
  value?: string;
  onChange: (branchId: string) => void;
  label?: string;
  /** Adds an "all branches" choice with an empty value (filters). */
  allLabel?: string;
  id?: string;
}

/** Picks a branch; hidden while the business has only one. */
export default function BranchPicker({ branches, value, onChange, label = 'Sucursal', allLabel, id }: BranchPickerProps) {
  if (branches.length < 2) return null;
  return (
    <div className="ta-branch-picker d-inline-flex align-items-center gap-2">
      <i className="ri-store-3-line text-muted" aria-hidden="true"></i>
      <select
        id={id}
        className="form-select form-select-sm"
        aria-label={label}
        value={value ?? ''}
        onChange={(event) => onChange(event.target.value)}
      >
        {allLabel !== undefined && <option value="">{allLabel}</option>}
        {branches.map((branch) => (
          <option key={branch.id} value={branch.id}>{branch.name}</option>
        ))}
      </select>
    </div>
  );
}
