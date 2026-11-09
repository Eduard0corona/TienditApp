import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { ReviewConcern } from '../../api/generated/enums';
import ConcernDialog from './ConcernDialog';

const TITLE = 'Reportar la reseña de Mariana O.';

/** As the reviews pages do: what it asks for opens it, and saving or closing closes it. */
function Harness({ onSubmit }: { onSubmit: (reason: ReviewConcern) => Promise<unknown> }) {
  const [title, setTitle] = useState<string | null>(null);
  return (
    <>
      <button type="button" onClick={() => setTitle(TITLE)}>Abrir</button>
      <ConcernDialog
        title={title}
        description="La plataforma la revisa."
        confirmLabel="Reportar"
        onSubmit={onSubmit}
        onClose={() => setTitle(null)}
      />
    </>
  );
}

function renderDialog(onSubmit: (reason: ReviewConcern) => Promise<unknown>) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <Harness onSubmit={onSubmit} />
    </QueryClientProvider>,
  );
}

async function open(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Abrir' }));
  const dialog = await screen.findByRole('dialog', { name: TITLE });
  await user.click(within(dialog).getByRole('radio', { name: 'Publicidad o nada que ver con el negocio' }));
  return dialog;
}

describe('ConcernDialog', () => {
  it('stays open while the reason is on its way, and closes with the answer', async () => {
    const user = userEvent.setup();
    let answer: () => void = () => undefined;
    const onSubmit = vi.fn(() => new Promise<void>((resolve) => { answer = resolve; }));
    renderDialog(onSubmit);

    const dialog = await open(user);
    await user.click(within(dialog).getByRole('button', { name: 'Reportar' }));

    expect(within(dialog).getByRole('button', { name: 'Guardando…' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.getByRole('dialog', { name: TITLE })).toHaveClass('show');
    expect(onSubmit).toHaveBeenCalledWith(ReviewConcern.Spam);

    answer();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('sends the reason once, even with a double click when the answer comes at once', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn(() => Promise.resolve());
    renderDialog(onSubmit);

    const dialog = await open(user);
    // The second click lands while the dialog fades out.
    await user.dblClick(within(dialog).getByRole('button', { name: 'Reportar' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it('shows why it could not be saved, and opens again without it', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn(() => Promise.reject(new Error('sin red')));
    renderDialog(onSubmit);

    const dialog = await open(user);
    await user.click(within(dialog).getByRole('button', { name: 'Reportar' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('No se pudo guardar. Intenta de nuevo.');

    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'Abrir' }));
    const again = await screen.findByRole('dialog', { name: TITLE });
    expect(within(again).queryByRole('alert')).not.toBeInTheDocument();
  });
});
