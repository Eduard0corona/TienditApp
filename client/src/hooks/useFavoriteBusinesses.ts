import { useContext, useState } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi, type MyFavoriteBusiness } from '../api/accountApi';
import { getApiErrorMessage } from '../api/errors';
import { ToastContext } from '../components/feedback/feedbackContexts';
import { AuthContext } from '../contexts/AuthContextDefinition';
import { useCustomerAccount } from './useCustomerAccount';

interface FavoriteChange {
  slug: string;
  save: boolean;
}

export interface FavoriteBusinesses {
  /**
   * Who may keep favorites here: a customer account; a visitor is invited to sign in; the team of a business, neither;
   * nobody yet while the session comes back (no heart that changes a moment later).
   */
  mode: 'customer' | 'visitor' | 'none' | 'loading';
  /** The account's favorites, the last one first; empty for anyone else or while they load. */
  favorites: MyFavoriteBusiness[];
  /** The business is among the favorites, counting a change still on its way. */
  isSaved: (slug: string) => boolean;
  /** The heart of this business waits: the favorites are not here yet, or a change of it is on its way. */
  isBusy: (slug: string) => boolean;
  toggle: (slug: string) => void;
}

interface Options {
  /** Off where no heart shows (a table of the store): the favorites are not asked for. */
  enabled?: boolean;
}

/**
 * The favorite businesses of the signed-in customer account (Fase 24F): the same list for Explorar and each store, the
 * heart of a business showing its change right away and going back if the server says no. Several hearts change at
 * once, each on its own.
 */
export function useFavoriteBusinesses(postalCode?: string, { enabled = true }: Options = {}): FavoriteBusinesses {
  // The store is also rendered without the providers (its tests): no session there, and no toasts.
  const auth = useContext(AuthContext);
  const toast = useContext(ToastContext);
  const { customer, signedIn } = useCustomerAccount();
  const queryClient = useQueryClient();
  const favorites = useQuery({
    queryKey: ['me', 'favorites', postalCode ?? ''],
    queryFn: ({ signal }) => accountApi.getFavorites(postalCode || undefined, signal),
    enabled: !!customer && enabled,
    staleTime: 30_000,
    // Another postal code keeps the hearts and «Tus favoritos» as they were while the cards with its deliveries arrive.
    placeholderData: keepPreviousData,
  });
  // The changes on their way, each heart its own: several change at once.
  const [pending, setPending] = useState<FavoriteChange[]>([]);
  const change = useMutation({
    mutationFn: ({ slug, save }: FavoriteChange) => (save ? accountApi.saveFavorite(slug) : accountApi.removeFavorite(slug)),
    onMutate: (variables) => setPending((current) => [...current, variables]),
    onSuccess: (_, { slug, save }) => {
      // Taken out, it leaves every list kept at once (Explorar's and the store's); kept, they come back with its card.
      if (!save) {
        queryClient.setQueriesData<MyFavoriteBusiness[]>(
          { queryKey: ['me', 'favorites'] },
          (list) => list?.filter((favorite) => favorite.slug !== slug),
        );
      }
    },
    onError: (error, { save }) => {
      toast?.error(getApiErrorMessage(error, save ? 'No se pudo guardar el negocio.' : 'No se pudo quitar el negocio.'));
    },
    // The change stays on its way until the lists that come back have it, so no heart blinks.
    onSettled: async (_, __, variables) => {
      await queryClient.invalidateQueries({ queryKey: ['me', 'favorites'] });
      setPending((current) => current.filter((item) => item !== variables));
    },
  });

  const list = customer ? favorites.data ?? [] : [];
  const saved = new Set(list.map((favorite) => favorite.slug));
  const changing = (slug: string) => pending.filter((item) => item.slug === slug).at(-1);
  const isSaved = (slug: string) => changing(slug)?.save ?? saved.has(slug);
  // Until the first list arrives a saved business would show an empty heart: it waits instead.
  const isBusy = (slug: string) => favorites.isPending || changing(slug) !== undefined;

  return {
    mode: auth?.isLoading ? 'loading' : customer ? 'customer' : signedIn ? 'none' : 'visitor',
    favorites: list,
    isSaved,
    isBusy,
    toggle: (slug) => {
      if (!customer || isBusy(slug)) return;
      change.mutate({ slug, save: !isSaved(slug) });
    },
  };
}
