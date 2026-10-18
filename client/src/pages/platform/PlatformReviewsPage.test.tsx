import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PlatformReviewsFilter, ReviewConcern, ReviewStatus } from '../../api/generated/enums';
import { reviewsApi, type PlatformReview } from '../../api/reviewsApi';
import ConfirmProvider from '../../components/feedback/ConfirmProvider';
import PlatformReviewsPage from './PlatformReviewsPage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/reviewsApi', () => ({
  reviewsApi: { getPlatformReviews: vi.fn(), hide: vi.fn(), keep: vi.fn(), restore: vi.fn() },
}));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const api = vi.mocked(reviewsApi);

function review(overrides: Partial<PlatformReview> = {}): PlatformReview {
  return {
    id: 'r1',
    tenantId: 't1',
    businessName: 'Café Luna',
    businessSlug: 'cafe-luna',
    authorName: 'Mariana O.',
    rating: 1,
    comment: 'Compra en otro lado, aquí todo es malo.',
    writtenAt: '2026-10-10T18:00:00Z',
    status: ReviewStatus.Published,
    reportedAt: '2026-10-11T10:00:00Z',
    reportReason: ReviewConcern.Spam,
    ...overrides,
  };
}

const page = (items: PlatformReview[]) => ({
  items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false,
});

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <ConfirmProvider>
        <PlatformReviewsPage />
      </ConfirmProvider>
    </QueryClientProvider>,
  );
}

describe('PlatformReviewsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('hides a reported review with the reason of its report already chosen, which can change', async () => {
    api.getPlatformReviews.mockResolvedValue(page([review()]));
    api.hide.mockResolvedValue(undefined as never);

    renderPage();

    expect(await screen.findByRole('link', { name: 'Café Luna' })).toHaveAttribute('href', '/m/cafe-luna');
    expect(screen.getByText('Publicidad o nada que ver con el negocio')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Ocultar la reseña de Mariana O. en Café Luna' }));
    const dialog = await screen.findByRole('dialog', { name: 'Ocultar la reseña de Mariana O.' });
    expect(within(dialog).getByRole('radio', { name: 'Publicidad o nada que ver con el negocio' })).toBeChecked();
    await userEvent.click(within(dialog).getByRole('radio', { name: 'Insultos, amenazas u odio' }));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Ocultar' }));

    await waitFor(() => expect(api.hide).toHaveBeenCalledWith('r1', ReviewConcern.Offensive));
    expect(toast.success).toHaveBeenCalledWith('Ocultaste la reseña.');
  });

  it('keeps a reported review after asking', async () => {
    api.getPlatformReviews.mockResolvedValue(page([review()]));
    api.keep.mockResolvedValue(undefined as never);

    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Mantener la reseña de Mariana O. en Café Luna' }));
    const confirm = await screen.findByRole('dialog', { name: 'Mantener reseña' });
    await userEvent.click(within(confirm).getByRole('button', { name: 'Mantener' }));

    await waitFor(() => expect(api.keep).toHaveBeenCalledWith('r1'));
  });

  it('restores a hidden review from the hidden ones', async () => {
    api.getPlatformReviews.mockImplementation(((filter: PlatformReviewsFilter) => Promise.resolve(page(
      filter === PlatformReviewsFilter.Hidden
        ? [review({ status: ReviewStatus.Hidden, reportedAt: null, reportReason: undefined, hiddenReason: ReviewConcern.Offensive, moderatedAt: '2026-10-12T10:00:00Z' })]
        : [],
    ))) as never);
    api.restore.mockResolvedValue(undefined as never);

    renderPage();

    expect(await screen.findByText('No hay reseñas reportadas por revisar.')).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Mostrar'), 'Ocultas');
    await userEvent.click(await screen.findByRole('button', { name: 'Restaurar la reseña de Mariana O. en Café Luna' }));
    const confirm = await screen.findByRole('dialog', { name: 'Restaurar reseña' });
    await userEvent.click(within(confirm).getByRole('button', { name: 'Restaurar' }));

    await waitFor(() => expect(api.restore).toHaveBeenCalledWith('r1'));
    expect(screen.queryByRole('button', { name: /Ocultar la reseña/ })).not.toBeInTheDocument();
  });
});
