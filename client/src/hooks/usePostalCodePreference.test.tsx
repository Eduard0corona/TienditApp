import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi } from '../api/accountApi';
import { AuthContext, type AuthContextType } from '../contexts/AuthContextDefinition';
import { readPostalCode } from '../utils/postalCodePreference';
import { usePostalCodePreference } from './usePostalCodePreference';

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

describe('usePostalCodePreference (Fase 31J)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
  });

  it('keeps a code chosen before the account\'s was read, over that older answer', async () => {
    // The account kept 72000; its answer is slow to arrive.
    let answer: (value: { postalCode: string | null }) => void = () => undefined;
    account.getPostalCode.mockReturnValue(new Promise((resolve) => { answer = resolve; }) as never);
    account.savePostalCode.mockImplementation(async (postalCode) => ({ postalCode }) as never);
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>
        <AuthContext.Provider value={customer}>{children}</AuthContext.Provider>
      </QueryClientProvider>
    );
    const { result } = renderHook(() => usePostalCodePreference(), { wrapper });
    await waitFor(() => expect(account.getPostalCode).toHaveBeenCalled());

    // «Cambiar» before it arrives: 01000 goes to the device and to the account.
    act(() => result.current[1]('01000'));
    await waitFor(() => expect(account.savePostalCode).toHaveBeenCalledWith('01000'));
    await act(async () => { answer({ postalCode: '72000' }); });

    expect(result.current[0]).toBe('01000');
    expect(readPostalCode()).toBe('01000');
    await waitFor(() => expect(queryClient.getQueryData(['me', 'postal-code'])).toEqual({ postalCode: '01000' }));
  });
});
