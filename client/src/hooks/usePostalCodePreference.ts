import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi } from '../api/accountApi';
import { readPostalCode, savePostalCode } from '../utils/postalCodePreference';
import { useCustomerAccount } from './useCustomerAccount';

const ACCOUNT_POSTAL_CODE_KEY = ['me', 'postal-code'];

/**
 * The postal code Explorar answers for («Entregar en…», Fase 24B), remembered on this device and, with a customer account,
 * in the account (Fase 31J), so it follows the customer to another device. Once per account the two agree: the account's
 * is the device's too, and one given only on this device goes to the account. A change goes to both; one the account
 * could not keep (offline) stays on the device. One chosen before the account's is read wins over that answer. When the
 * customer leaves, the page shows what the device kept: nothing of theirs.
 */
export function usePostalCodePreference(): [string, (postalCode: string) => void] {
  const { customer, checking } = useCustomerAccount();
  const client = useQueryClient();
  const [postalCode, setPostalCode] = useState(readPostalCode);
  const account = useQuery({
    queryKey: ACCOUNT_POSTAL_CODE_KEY,
    queryFn: ({ signal }) => accountApi.getPostalCode(signal),
    enabled: !!customer && !checking,
  });
  const [agreedFor, setAgreedFor] = useState<string | null>(null);
  const [toAccount, setToAccount] = useState<string | null>(null);
  // A customer left (here or in another tab) and the device forgot their code: neither does the page keep it.
  const [codeOf, setCodeOf] = useState(customer?.id ?? null);
  if ((customer?.id ?? null) !== codeOf) {
    setCodeOf(customer?.id ?? null);
    if (!customer) {
      setPostalCode(readPostalCode());
      setAgreedFor(null);
      setToAccount(null);
    }
  }

  if (customer && account.isSuccess && agreedFor !== customer.id) {
    setAgreedFor(customer.id);
    const kept = account.data.postalCode ?? '';
    if (kept && kept !== postalCode) {
      savePostalCode(kept);
      setPostalCode(kept);
    } else if (!kept && postalCode) {
      setToAccount(postalCode);
    }
  }

  useEffect(() => {
    if (toAccount === null) return;
    void accountApi.savePostalCode(toAccount)
      .then((saved) => client.setQueryData(ACCOUNT_POSTAL_CODE_KEY, saved))
      .catch(() => undefined);
  }, [toAccount, client]);

  const change = (code: string) => {
    savePostalCode(code);
    setPostalCode(code);
    if (!customer) return;
    // Chosen before the account's was read: that answer is older than this, and neither the page nor its copy takes it.
    if (agreedFor !== customer.id) {
      setAgreedFor(customer.id);
      void client.cancelQueries({ queryKey: ACCOUNT_POSTAL_CODE_KEY });
    }
    setToAccount(code);
  };
  return [postalCode, change];
}
