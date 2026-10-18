import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BusinessReviewsFilter, ReviewConcern, ReviewStatus } from '../../api/generated/enums';
import { reviewsApi, type BusinessReview } from '../../api/reviewsApi';
import ConfirmProvider from '../../components/feedback/ConfirmProvider';
import BusinessReviewsPage from './BusinessReviewsPage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/reviewsApi', () => ({
  reviewsApi: { getBusinessReviews: vi.fn(), reply: vi.fn(), removeReply: vi.fn(), report: vi.fn() },
}));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const api = vi.mocked(reviewsApi);

function review(overrides: Partial<BusinessReview> = {}): BusinessReview {
  return {
    id: 'r1',
    authorName: 'Mariana O.',
    rating: 2,
    comment: 'Llegó frío.',
    writtenAt: '2026-10-10T18:00:00Z',
    status: ReviewStatus.Published,
    ...overrides,
  };
}

function answer(items: BusinessReview[]) {
  api.getBusinessReviews.mockResolvedValue({
    summary: { count: 23, average: 4.6 },
    unanswered: 4,
    reported: 1,
    reviews: { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false },
  });
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <ConfirmProvider>
        <BusinessReviewsPage />
      </ConfirmProvider>
    </QueryClientProvider>,
  );
}

describe('BusinessReviewsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows the rating and what waits for the business, and answers a review', async () => {
    answer([review()]);
    api.reply.mockResolvedValue(review({ reply: 'Lo sentimos.' }));

    renderPage();

    expect(await screen.findByText('23 reseñas')).toBeInTheDocument();
    expect(screen.getByText('Responder ayuda a que te elijan')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: '4.6 de 5 estrellas' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Responder a Mariana O.' }));
    const dialog = await screen.findByRole('dialog', { name: 'Responder a Mariana O.' });
    expect(dialog).toHaveTextContent('Llegó frío.');
    await userEvent.type(within(dialog).getByLabelText('Tu respuesta'), '  Lo sentimos, ya revisamos el envío.  ');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Publicar respuesta' }));

    await waitFor(() => expect(api.reply).toHaveBeenCalledWith('r1', 'Lo sentimos, ya revisamos el envío.'));
    expect(toast.success).toHaveBeenCalledWith('Publicamos tu respuesta a Mariana O..');
  });

  it('reports a review with a reason from the list, and offers it no more once reported or kept', async () => {
    answer([
      review(),
      review({ id: 'r2', authorName: 'Luis P.', reportedAt: '2026-10-11T10:00:00Z', reportReason: ReviewConcern.Spam }),
      review({ id: 'r3', authorName: 'Ana R.', moderatedAt: '2026-10-12T10:00:00Z' }),
      review({ id: 'r4', authorName: 'Rosa T.', status: ReviewStatus.Hidden, hiddenReason: ReviewConcern.Offensive, reply: 'Gracias.' }),
    ]);
    api.report.mockResolvedValue(review({ reportedAt: '2026-10-13T10:00:00Z', reportReason: ReviewConcern.NotAnExperience }));

    renderPage();

    await screen.findByText('Luis P.');
    expect(screen.queryByRole('button', { name: 'Reportar la reseña de Luis P.' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reportar la reseña de Ana R.' })).not.toBeInTheDocument();
    expect(screen.getByText('En revisión')).toBeInTheDocument();
    // A hidden one is not answered, though its answer can still go.
    expect(screen.getByText('Oculta')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cambiar respuesta a Rosa T.' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Quitar respuesta a Rosa T.' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Reportar la reseña de Mariana O.' }));
    const dialog = await screen.findByRole('dialog', { name: 'Reportar la reseña de Mariana O.' });
    expect(within(dialog).getByRole('button', { name: 'Reportar' })).toBeDisabled();
    await userEvent.click(within(dialog).getByRole('radio', { name: 'No habla de un pedido o una cita' }));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Reportar' }));

    await waitFor(() => expect(api.report).toHaveBeenCalledWith('r1', ReviewConcern.NotAnExperience));
    expect(toast.success).toHaveBeenCalledWith('Reportaste la reseña: la plataforma la va a revisar.');
  });

  it('asks the API for the reviews of the filter chosen, from its first page', async () => {
    answer([]);

    renderPage();

    expect(await screen.findByText('Todavía no tienes reseñas. Tus clientes califican desde sus pedidos y citas.')).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Mostrar'), 'Sin responder');

    await waitFor(() => expect(api.getBusinessReviews).toHaveBeenLastCalledWith(BusinessReviewsFilter.Unanswered, 1, 20, expect.anything()));
    expect(await screen.findByText('No hay reseñas con este filtro.')).toBeInTheDocument();
  });
});
