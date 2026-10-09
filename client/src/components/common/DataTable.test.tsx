import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import DataTable, { DataTableFilter } from './DataTable';

interface Row { id: string; name: string }

const rows = (count: number): Row[] =>
  Array.from({ length: count }, (_, i) => ({ id: String(i + 1), name: `Insumo ${String(i + 1).padStart(2, '0')}` }));

function renderTable(data: Row[]) {
  return render(
    <DataTable
      data={data}
      columns={[{ header: 'Nombre' }]}
      keyExtractor={(row) => row.id}
      renderRow={(row) => <td>{row.name}</td>}
      searchFilter={(row, search) => row.name.toLowerCase().includes(search)}
      searchPlaceholder="Nombre del insumo"
      filters={(
        <DataTableFilter label="Estado" htmlFor="test-status">
          <select id="test-status" className="form-select form-select-sm"><option>Todos</option></select>
        </DataTableFilter>
      )}
      toolbarEnd={<button type="button" className="btn btn-primary btn-sm">Nuevo insumo</button>}
    />,
  );
}

describe('DataTable', () => {
  it('puts the search, the filters and the action of the list in one bar, and the page size at the bottom', () => {
    renderTable(rows(30));

    const search = screen.getByRole('search');
    expect(within(search).getByRole('searchbox', { name: 'Nombre del insumo' })).toBeInTheDocument();
    expect(within(search).getByRole('combobox', { name: 'Estado' })).toBeInTheDocument();
    // The action of the list shares the bar, outside the search landmark.
    const bar = search.closest('.datatable-toolbar') as HTMLElement;
    expect(within(bar).getByRole('button', { name: 'Nuevo insumo' })).toBeInTheDocument();
    expect(screen.queryByText('Mostrar')).not.toBeInTheDocument();

    expect(screen.getByText('1–10 de 30')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Por página' })).toHaveValue('10');
  });

  it('moves through the pages with named buttons and marks the current one', async () => {
    const user = userEvent.setup();
    renderTable(rows(30));

    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));

    expect(screen.getByText('11–20 de 30')).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Insumo 11' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '2' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('button', { name: 'Página anterior' })).toBeEnabled();
  });

  it('shows no page controls when everything fits in one page', () => {
    renderTable(rows(5));

    expect(screen.getByText('1–5 de 5')).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Por página' })).not.toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Páginas' })).not.toBeInTheDocument();
  });

  it('searches the rows loaded and starts again from the first page', async () => {
    const user = userEvent.setup();
    renderTable(rows(30));
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));

    // "0" is in Insumo 01 to 10, 20 and 30: twelve rows, back on the first page.
    await user.type(screen.getByRole('searchbox', { name: 'Nombre del insumo' }), '0');

    expect(screen.getByText('1–10 de 12')).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Insumo 01' })).toBeInTheDocument();
  });
});
