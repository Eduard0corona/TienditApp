import { useQuery } from '@tanstack/react-query';
import { accountApi } from '../api/accountApi';
import { pendingPostalCode, readPostalCode } from '../utils/postalCodePreference';
import { useCustomerAccount } from './useCustomerAccount';
import { ACCOUNT_POSTAL_CODE_KEY } from './usePostalCodePreference';

/**
 * The postal code an address starts with in the store and on the visit page (Fase 31P): with a customer account, the one
 * the account keeps (Explorar's, from any device), unless this device changed it and the account could not keep it yet
 * (offline, or on its way: this device's is newer); without a session, or when the account's cannot be read, this
 * device's. `pending` while the session or the account's code are read, so the page does not start with one it is about
 * to replace: read again each time the page opens, not what Explorar read a moment ago (another tab or device may have
 * changed it since). Only where it is used (`enabled`: the store, when it delivers).
 */
export function useRememberedPostalCode(enabled = true): { postalCode: string; pending: boolean } {
  const { customer, checking } = useCustomerAccount();
  const account = useQuery({
    queryKey: ACCOUNT_POSTAL_CODE_KEY,
    queryFn: ({ signal }) => accountApi.getPostalCode(signal),
    enabled: enabled && !!customer && !checking,
    staleTime: 0,
  });
  const device = readPostalCode();
  if (!enabled) return { postalCode: device, pending: false };
  if (checking || (customer && !account.isFetchedAfterMount)) return { postalCode: device, pending: true };
  if (!customer || !account.isSuccess) return { postalCode: device, pending: false };
  const kept = account.data.postalCode ?? '';
  const changedHere = pendingPostalCode() === device && device !== kept;
  return { postalCode: changedHere ? device : kept || device, pending: false };
}
