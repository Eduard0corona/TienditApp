import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders } from 'axios';
import { useState } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ReviewConcern } from '../../api/generated/enums';
import { reviewsApi, type MyReview } from '../../api/reviewsApi';
import ReviewDialog from './ReviewDialog';

vi.mock('../../api/reviewsApi', () => ({
  reviewsApi: { getMyReview: vi.fn(), saveMyReview: vi.fn(), deleteMyReview: vi.fn() },
}));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const api = vi.mocked(reviewsApi);
const CAFE = { slug: 'cafe-luna', name: 'Café Luna' };

function review(overrides: Partial<MyReview> = {}): MyReview {
  return {
    slug: 'cafe-luna',
    businessName: 'Café Luna',
    rating: 3,
    comment: 'Rico, pero tardó.',
    writtenAt: '2026-10-10T18:00:00Z',
    hidden: false,
    ...overrides,
  };
}

/** What the API answers with a ProblemDetails, as its handler words it. */
function problem(detail: string) {
  return new AxiosError('400', 'ERR_BAD_REQUEST', undefined, undefined, {
    status: 400, statusText: 'Bad Request', headers: {}, config: { headers: new AxiosHeaders() }, data: { detail },
  });
}

function renderDialog(onClose = vi.fn(), queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  render(
    <QueryClientProvider client={queryClient}>
      <ReviewDialog business={CAFE} onClose={onClose} />
    </QueryClientProvider>,
  );
  return onClose;
}

/** As Mis pedidos does: «Calificar» opens it, and saving, deleting or closing closes it. */
function Harness() {
  const [business, setBusiness] = useState<typeof CAFE | null>(null);
  return (
    <>
      <button type="button" onClick={() => setBusiness(CAFE)}>Calificar</button>
      <ReviewDialog business={business} onClose={() => setBusiness(null)} />
    </>
  );
}

function renderHarness() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <Harness />
    </QueryClientProvider>,
  );
}

describe('ReviewDialog', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('changes the review the account wrote, starting from what it keeps', async () => {
    api.getMyReview.mockResolvedValue({ canReview: true, review: review({ reply: 'Gracias, ya mejoramos los tiempos.' }) });
    api.saveMyReview.mockResolvedValue(review({ rating: 5 }));
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidated = vi.spyOn(queryClient, 'invalidateQueries');

    const onClose = renderDialog(vi.fn(), queryClient);

    const dialog = await screen.findByRole('dialog', { name: 'Tu reseña de Café Luna' });
    expect(within(dialog).getByRole('radio', { name: '3 estrellas' })).toBeChecked();
    expect(within(dialog).getByLabelText('Cuéntanos cómo te fue (opcional)')).toHaveValue('Rico, pero tardó.');
    expect(dialog).toHaveTextContent('Respuesta de Café Luna');
    expect(dialog).toHaveTextContent('17/500');
    await userEvent.click(within(dialog).getByRole('radio', { name: '5 estrellas' }));
    await userEvent.clear(within(dialog).getByLabelText('Cuéntanos cómo te fue (opcional)'));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    // An empty comment is no comment.
    expect(api.saveMyReview).toHaveBeenCalledWith('cafe-luna', { rating: 5, comment: null });
    expect(toast.success).toHaveBeenCalledWith('Guardamos los cambios de tu reseña.');
    // What shows the review asks for it again: the account's reviews (Mis pedidos, Mis citas), the store's and its rating.
    expect(invalidated.mock.calls.map(([filters]) => filters?.queryKey)).toEqual([
      ['me', 'reviews'], ['public-reviews', 'cafe-luna'], ['public-menu', 'cafe-luna'],
    ]);
  });

  it('deletes it after asking in the same dialog', async () => {
    api.getMyReview.mockResolvedValue({ canReview: true, review: review() });
    api.deleteMyReview.mockResolvedValue(undefined as never);

    const onClose = renderDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Tu reseña de Café Luna' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Borrar reseña' }));
    expect(dialog).toHaveTextContent('¿Borrar tu reseña de Café Luna?');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Volver' }));
    expect(api.deleteMyReview).not.toHaveBeenCalled();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Borrar reseña' }));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Borrar' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(api.deleteMyReview).toHaveBeenCalledWith('cafe-luna');
    expect(toast.success).toHaveBeenCalledWith('Borraste tu reseña.');
  });

  it('shows why the platform hid it, and only lets delete it', async () => {
    api.getMyReview.mockResolvedValue({
      canReview: true, review: review({ hidden: true, hiddenReason: ReviewConcern.PersonalData }),
    });

    renderDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Tu reseña de Café Luna' });
    expect(dialog).toHaveTextContent('La plataforma ocultó tu reseña (datos personales de alguien): ya no se puede cambiar.');
    expect(within(dialog).queryByRole('radio')).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: /Guardar|Publicar/ })).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Borrar reseña' })).toBeInTheDocument();
  });

  it('tells an account that did not buy there when it can rate it', async () => {
    api.getMyReview.mockResolvedValue({ canReview: false, review: null });

    renderDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Califica a Café Luna' });
    expect(await within(dialog).findByText('Podrás calificar a Café Luna cuando recibas un pedido o vayas a una cita con tu cuenta.'))
      .toBeInTheDocument();
    expect(within(dialog).queryByRole('radio')).not.toBeInTheDocument();
  });

  it('asks for the stars first, and keeps what was written when the API refuses it', async () => {
    api.getMyReview.mockResolvedValue({ canReview: true, review: null });
    api.saveMyReview.mockRejectedValue(problem('Tu reseña no puede llevar enlaces, correos ni teléfonos.'));

    const onClose = renderDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Califica a Café Luna' });
    expect(await within(dialog).findByRole('button', { name: 'Publicar' })).toBeDisabled();
    expect(dialog).toHaveTextContent('Elige de 1 a 5 estrellas.');
    await userEvent.click(within(dialog).getByRole('radio', { name: '1 estrella' }));
    await userEvent.type(within(dialog).getByLabelText('Cuéntanos cómo te fue (opcional)'), 'Escríbeme a www.ofertas.com');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Publicar' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Tu reseña no puede llevar enlaces, correos ni teléfonos.');
    expect(within(dialog).getByLabelText('Cuéntanos cómo te fue (opcional)')).toHaveValue('Escríbeme a www.ofertas.com');
    expect(onClose).not.toHaveBeenCalled();
  });
  it('stays open while the review is saved', async () => {
    const user = userEvent.setup();
    api.getMyReview.mockResolvedValue({ canReview: true, review: null });
    let saved: (value: unknown) => void = () => undefined;
    api.saveMyReview.mockReturnValue(new Promise((resolve) => { saved = resolve; }) as never);

    const onClose = renderDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Califica a Café Luna' });
    await user.click(await within(dialog).findByRole('radio', { name: '5 estrellas' }));
    await user.click(within(dialog).getByRole('button', { name: 'Publicar' }));

    expect(within(dialog).getByRole('button', { name: 'Guardando…' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(onClose).not.toHaveBeenCalled();

    saved(review({ rating: 5 }));
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(toast.success).toHaveBeenCalledWith('Publicamos tu reseña de Café Luna.');
  });

  it('publishes a review once, even with a double click when the server answers at once', async () => {
    const user = userEvent.setup();
    api.getMyReview.mockResolvedValue({ canReview: true, review: null });
    api.saveMyReview.mockResolvedValue(review({ rating: 5 }));

    renderHarness();

    await user.click(screen.getByRole('button', { name: 'Calificar' }));
    const dialog = await screen.findByRole('dialog', { name: 'Califica a Café Luna' });
    await user.click(await within(dialog).findByRole('radio', { name: '5 estrellas' }));
    // The second click lands while the dialog fades out.
    await user.dblClick(within(dialog).getByRole('button', { name: 'Publicar' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(api.saveMyReview).toHaveBeenCalledTimes(1);
  });

  it('deletes a review once, even with a double click when the server answers at once', async () => {
    const user = userEvent.setup();
    api.getMyReview.mockResolvedValue({ canReview: true, review: review() });
    api.deleteMyReview.mockResolvedValue(undefined as never);

    renderHarness();

    await user.click(screen.getByRole('button', { name: 'Calificar' }));
    const dialog = await screen.findByRole('dialog', { name: 'Tu reseña de Café Luna' });
    await user.click(await within(dialog).findByRole('button', { name: 'Borrar reseña' }));
    await user.dblClick(within(dialog).getByRole('button', { name: 'Borrar' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(api.deleteMyReview).toHaveBeenCalledTimes(1);
  });

  it('opens again without the error of the last time', async () => {
    const user = userEvent.setup();
    api.getMyReview.mockResolvedValue({ canReview: true, review: null });
    api.saveMyReview.mockImplementation(() => Promise.reject(problem('Tu reseña no puede llevar enlaces, correos ni teléfonos.')));

    renderHarness();

    await user.click(screen.getByRole('button', { name: 'Calificar' }));
    const dialog = await screen.findByRole('dialog', { name: 'Califica a Café Luna' });
    await user.click(await within(dialog).findByRole('radio', { name: '1 estrella' }));
    await user.click(within(dialog).getByRole('button', { name: 'Publicar' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Tu reseña no puede llevar enlaces');

    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'Calificar' }));
    const again = await screen.findByRole('dialog', { name: 'Califica a Café Luna' });
    expect(within(again).queryByRole('alert')).not.toBeInTheDocument();
  });
  it('opens again without the error of a delete that failed', async () => {
    const user = userEvent.setup();
    api.getMyReview.mockResolvedValue({ canReview: true, review: review() });
    api.deleteMyReview.mockImplementation(() => Promise.reject(problem('La reseña ya no existe.')));

    renderHarness();

    await user.click(screen.getByRole('button', { name: 'Calificar' }));
    const dialog = await screen.findByRole('dialog', { name: 'Tu reseña de Café Luna' });
    await user.click(await within(dialog).findByRole('button', { name: 'Borrar reseña' }));
    await user.click(within(dialog).getByRole('button', { name: 'Borrar' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('La reseña ya no existe.');

    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'Calificar' }));
    const again = await screen.findByRole('dialog', { name: 'Tu reseña de Café Luna' });
    expect(within(again).queryByRole('alert')).not.toBeInTheDocument();
  });
  it('opens again without the error of a save, once the review was deleted after it', async () => {
    const user = userEvent.setup();
    api.getMyReview.mockResolvedValue({ canReview: true, review: review() });
    api.saveMyReview.mockImplementation(() => Promise.reject(problem('Tu reseña no puede llevar enlaces, correos ni teléfonos.')));
    api.deleteMyReview.mockResolvedValue(undefined as never);

    renderHarness();

    await user.click(screen.getByRole('button', { name: 'Calificar' }));
    const dialog = await screen.findByRole('dialog', { name: 'Tu reseña de Café Luna' });
    await user.click(await within(dialog).findByRole('button', { name: 'Guardar cambios' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Tu reseña no puede llevar enlaces');
    await user.click(within(dialog).getByRole('button', { name: 'Borrar reseña' }));
    await user.click(within(dialog).getByRole('button', { name: 'Borrar' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    api.getMyReview.mockResolvedValue({ canReview: true, review: null });
    await user.click(screen.getByRole('button', { name: 'Calificar' }));
    const again = await screen.findByRole('dialog', { name: 'Califica a Café Luna' });
    await within(again).findByRole('button', { name: 'Publicar' });
    expect(within(again).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('opens again without the error of a delete, once the review was saved after it', async () => {
    const user = userEvent.setup();
    api.getMyReview.mockResolvedValue({ canReview: true, review: review() });
    api.deleteMyReview.mockImplementation(() => Promise.reject(problem('La reseña ya no existe.')));
    api.saveMyReview.mockResolvedValue(review({ rating: 5 }));

    renderHarness();

    await user.click(screen.getByRole('button', { name: 'Calificar' }));
    const dialog = await screen.findByRole('dialog', { name: 'Tu reseña de Café Luna' });
    await user.click(await within(dialog).findByRole('button', { name: 'Borrar reseña' }));
    await user.click(within(dialog).getByRole('button', { name: 'Borrar' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('La reseña ya no existe.');
    await user.click(within(dialog).getByRole('button', { name: 'Volver' }));
    await user.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Calificar' }));
    const again = await screen.findByRole('dialog', { name: 'Tu reseña de Café Luna' });
    await within(again).findByRole('button', { name: 'Guardar cambios' });
    expect(within(again).queryByRole('alert')).not.toBeInTheDocument();
  });
});
