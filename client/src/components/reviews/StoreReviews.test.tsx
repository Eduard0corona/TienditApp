import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { reviewsApi, type PublicReview } from '../../api/reviewsApi';
import StoreReviews from './StoreReviews';

vi.mock('../../api/reviewsApi', () => ({ reviewsApi: { getStoreReviews: vi.fn(), getMyReview: vi.fn() } }));

const api = vi.mocked(reviewsApi);
const CAFE = { slug: 'cafe-luna', name: 'Café Luna' };

function review(index: number): PublicReview {
  return { id: `r${index}`, authorName: `Cliente ${index}.`, rating: 5, writtenAt: '2026-10-10T18:00:00Z', edited: false };
}

function renderReviews() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <StoreReviews business={CAFE} summary={{ count: 12, average: 4.8 }} customer={false} />
    </QueryClientProvider>,
  );
}

describe('StoreReviews', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows ten reviews and the next ones on «Ver más reseñas», with the focus on the first new one', async () => {
    api.getStoreReviews.mockImplementation(((_slug: string, page: number) => Promise.resolve(page === 1
      ? { summary: { count: 12, average: 4.8 }, pageNumber: 1, hasNextPage: true, items: Array.from({ length: 10 }, (_, i) => review(i + 1)) }
      : { summary: { count: 12, average: 4.8 }, pageNumber: 2, hasNextPage: false, items: [review(11), review(12)] })) as never);

    renderReviews();

    const section = await screen.findByRole('region', { name: 'Reseñas' });
    expect(await within(section).findByText('Cliente 10.')).toBeInTheDocument();
    // The average is said once, by the stars.
    expect(within(section).getByRole('img', { name: '4.8 de 5 estrellas' })).toBeInTheDocument();
    await userEvent.click(within(section).getByRole('button', { name: 'Ver más reseñas' }));

    const eleventh = await within(section).findByText('Cliente 11.');
    await waitFor(() => expect(document.activeElement).toBe(eleventh.closest('li')));
    expect(api.getStoreReviews).toHaveBeenLastCalledWith('cafe-luna', 2, expect.anything());
    expect(within(section).queryByRole('button', { name: 'Ver más reseñas' })).not.toBeInTheDocument();
    expect(within(section).getAllByRole('listitem')).toHaveLength(12);
  });
});
