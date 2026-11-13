import { useContext } from 'react';
import { AuthContext, type AuthContextType } from '../contexts/AuthContextDefinition';
import type { UserProfileDto } from '../types';
import { getAccess } from '../utils/access';

/**
 * Who buys on a screen open to everyone (the store, Fase 24B): the signed-in customer account, or none. A session of a
 * business's team buys as a guest, as the server decides. `checking` is true while the session of this browser is still
 * being read (Fase 31J): what the account keeps (its cart, its postal code) is not known yet.
 */
export function useCustomerAccount(): { customer: UserProfileDto | null; signedIn: boolean; checking: boolean } {
  // The store is also rendered without the sign-in provider (its tests): no context is a visitor.
  const auth: AuthContextType | null = useContext(AuthContext);
  const checking = !!auth?.isLoading;
  if (!auth?.isAuthenticated || !auth.user) return { customer: null, signedIn: false, checking };
  return { customer: getAccess(auth.user, null).myOrders ? auth.user : null, signedIn: true, checking };
}
