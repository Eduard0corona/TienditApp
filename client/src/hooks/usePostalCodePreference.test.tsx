import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi } from '../api/accountApi';
import { AuthContext, type AuthContextType } from '../contexts/AuthContextDefinition';
import { pendingPostalCode, readPostalCode, savePostalCode } from '../utils/postalCodePreference';
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

  it('keeps a code the account could not keep (offline) over the account\'s older one, and sends it again', async () => {
    const render = () => {
      const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const wrapper = ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={queryClient}>
          <AuthContext.Provider value={customer}>{children}</AuthContext.Provider>
        </QueryClientProvider>
      );
      return renderHook(() => usePostalCodePreference(), { wrapper });
    };
    // The account kept 72000; offline, the customer changes to 01000, twice.
    savePostalCode('72000');
    account.getPostalCode.mockResolvedValue({ postalCode: '72000' });
    account.savePostalCode.mockRejectedValue(new Error('Sin conexión'));
    const first = render();
    await waitFor(() => expect(first.result.current[0]).toBe('72000'));
    act(() => first.result.current[1]('01000'));
    await waitFor(() => expect(pendingPostalCode()).toBe('01000'));
    act(() => first.result.current[1]('01000'));
    await waitFor(() => expect(account.savePostalCode).toHaveBeenCalledTimes(2));
    first.unmount();

    // Back online, the next visit keeps 01000 and gives it to the account.
    account.savePostalCode.mockImplementation(async (postalCode) => ({ postalCode }) as never);
    const next = render();
    await waitFor(() => expect(account.savePostalCode).toHaveBeenCalledTimes(3));
    expect(account.savePostalCode).toHaveBeenLastCalledWith('01000');
    expect(next.result.current[0]).toBe('01000');
    expect(readPostalCode()).toBe('01000');
    await waitFor(() => expect(pendingPostalCode()).toBeNull());
  });
});
