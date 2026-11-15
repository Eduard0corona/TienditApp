import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi } from '../api/accountApi';
import { AuthContext, type AuthContextType } from '../contexts/AuthContextDefinition';
import { markPostalCodePending, savePostalCode } from '../utils/postalCodePreference';
import { useRememberedPostalCode } from './useRememberedPostalCode';

vi.mock('../api/accountApi', () => ({ accountApi: { getPostalCode: vi.fn(), savePostalCode: vi.fn() } }));

const account = vi.mocked(accountApi);

/** A customer account, signed in. */
const customer = {
  isAuthenticated: true,
  isLoading: false,
  user: {
    id: 'u1', email: 'mariana@demo.com', firstName: 'Mariana', lastName: 'Ortiz', fullName: 'Mariana Ortiz',
    phoneNumber: null, phoneNumberConfirmed: false, emailConfirmed: true, role: 'Customer', permissions: [],
  },
} as unknown as AuthContextType;

/** No one signed in. */
const guest = { isAuthenticated: false, isLoading: false, user: null } as unknown as AuthContextType;

describe('useRememberedPostalCode (Fase 31P)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
  });

  const renderRemembered = (auth: AuthContextType) => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>
        <AuthContext.Provider value={auth}>{children}</AuthContext.Provider>
      </QueryClientProvider>
    );
    return renderHook(() => useRememberedPostalCode(), { wrapper });
  };

  it('starts the address with the code the account keeps, once it is read, over the one of this device', async () => {
    savePostalCode('01000');
    account.getPostalCode.mockResolvedValue({ postalCode: '72000' } as never);

    const { result } = renderRemembered(customer);

    // Waits for the account: the page does not start with a code it is about to replace.
    expect(result.current).toEqual({ postalCode: '01000', pending: true });
    await waitFor(() => expect(result.current).toEqual({ postalCode: '72000', pending: false }));
  });

  it('keeps this device\'s when it changed it and the account could not keep it yet, or the account has none', async () => {
    savePostalCode('01000');
    markPostalCodePending('01000');
    account.getPostalCode.mockResolvedValue({ postalCode: '72000' } as never);
    const changedHere = renderRemembered(customer);
    await waitFor(() => expect(changedHere.result.current).toEqual({ postalCode: '01000', pending: false }));

    markPostalCodePending(null);
    account.getPostalCode.mockResolvedValue({ postalCode: null } as never);
    const none = renderRemembered(customer);
    await waitFor(() => expect(none.result.current).toEqual({ postalCode: '01000', pending: false }));
  });

  it('takes this device\'s without a session, while it is checked, or when the account\'s cannot be read', async () => {
    savePostalCode('01000');
    expect(renderRemembered(guest).result.current).toEqual({ postalCode: '01000', pending: false });
    expect(renderRemembered({ ...customer, isLoading: true } as AuthContextType).result.current)
      .toEqual({ postalCode: '01000', pending: true });

    account.getPostalCode.mockRejectedValue(new Error('offline'));
    const failed = renderRemembered(customer);
    await waitFor(() => expect(failed.result.current).toEqual({ postalCode: '01000', pending: false }));
  });
});
