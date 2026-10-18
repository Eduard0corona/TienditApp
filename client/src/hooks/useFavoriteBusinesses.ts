import { useContext } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi, type MyFavoriteBusiness } from '../api/accountApi';
import { getApiErrorMessage } from '../api/errors';
import { ToastContext } from '../components/feedback/feedbackContexts';
import { useCustomerAccount } from './useCustomerAccount';

export interface FavoriteBusinesses {
  /** Who may keep favorites here: a customer account; a visitor is invited to sign in; the team of a business, neither. */
  mode: 'customer' | 'visitor' | 'none';
  /** The account's favorites, the last one first; empty for anyone else or while they load. */
  favorites: MyFavoriteBusiness[];
  /** The business is among the favorites, counting a change still on its way. */
  isSaved: (slug: string) => boolean;
  /** A change of this business is on its way. */
  isChanging: (slug: string) => boolean;
  toggle: (slug: string) => void;
}

/**
 * The favorite businesses of the signed-in customer account (Fase 24F): the same list for Explorar and each store, the
 * heart of a business showing its change right away and going back if the server says no.
 */
export function useFavoriteBusinesses(postalCode?: string): FavoriteBusinesses {
  const { customer, signedIn } = useCustomerAccount();
  // The store is also rendered without the providers (its tests): no toasts there.
  const toast = useContext(ToastContext);
  const queryClient = useQueryClient();
  const favorites = useQuery({
    queryKey: ['me', 'favorites', postalCode ?? ''],
    queryFn: ({ signal }) => accountApi.getFavorites(postalCode || undefined, signal),
    enabled: !!customer,
    staleTime: 30_000,
  });
  const change = useMutation({
    mutationFn: ({ slug, save }: { slug: string; save: boolean }) =>
      save ? accountApi.saveFavorite(slug) : accountApi.removeFavorite(slug),
    onError: (error, { save }) => {
      toast?.error(getApiErrorMessage(error, save ? 'No se pudo guardar el negocio.' : 'No se pudo quitar el negocio.'));
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['me', 'favorites'] }),
  });

  const list = customer ? favorites.data ?? [] : [];
  const saved = new Set(list.map((favorite) => favorite.slug));
  const pending = change.isPending ? change.variables : undefined;
  const isSaved = (slug: string) => (pending?.slug === slug ? pending.save : saved.has(slug));

  return {
    mode: customer ? 'customer' : signedIn ? 'none' : 'visitor',
    favorites: list,
    isSaved,
    isChanging: (slug) => pending?.slug === slug,
    toggle: (slug) => {
      if (!customer || change.isPending) return;
      change.mutate({ slug, save: !isSaved(slug) });
    },
  };
}
