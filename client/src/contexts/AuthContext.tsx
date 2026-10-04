import { useState, useEffect, useCallback, type ReactNode } from 'react';
import { authApi } from '../api/authApi';
import { profileApi } from '../api/profileApi';
import { refreshAccessToken, setAccessToken } from '../api/axios';
import { queryKeys } from '../api/queries';
import { queryClient } from '../api/queryClient';
import { apiRequest } from '../api/typedClient';
import type { LoginRequest, TwoFactorLoginRequest, UserProfileDto } from '../types';
import { AuthContext, type LoginResult } from './AuthContextDefinition';
import { broadcastSessionChange, reloadForTenantChange, useSessionSync } from './useSessionSync';

const SESSION_MARKER_KEY = 'fpd_has_session';

function getSessionMarker(): boolean {
  try {
    return window.localStorage.getItem(SESSION_MARKER_KEY) === '1';
  } catch {
    return false;
  }
}

function setSessionMarker(value: boolean) {
  try {
    if (value) {
      window.localStorage.setItem(SESSION_MARKER_KEY, '1');
    } else {
      window.localStorage.removeItem(SESSION_MARKER_KEY);
    }
  } catch {
    // Ignore localStorage failures (private mode / blocked storage)
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserProfileDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const clearSession = useCallback(() => {
    setAccessToken(null);
    setUser(null);
    setSessionMarker(false);
    // Cached server state belongs to the user who leaves.
    queryClient.clear();
  }, []);

  useSessionSync(clearSession);

  const refreshProfile = useCallback(async () => {
    try {
      const { data } = await profileApi.getProfile();
      // The token's permission claims can be older than a change made by an admin: the server's current
      // access wins when it answers (menus and guards only; the API checks every request itself).
      const access = await apiRequest('get', '/api/v1/me/access').catch(() => null);
      setUser(access?.isMember ? { ...data, permissions: access.permissions ?? data.permissions } : data);
      queryClient.removeQueries({ queryKey: queryKeys.myAccess });
    } catch {
      setUser(null);
      setAccessToken(null);
    }
  }, []);

  // On mount: try to recover session via refresh token cookie.
  useEffect(() => {
    const recoverSession = async () => {
      if (!getSessionMarker()) {
        setIsLoading(false);
        return;
      }

      try {
        // Shares the cross-tab refresh lock with the axios interceptor.
        const accessToken = await refreshAccessToken();
        if (accessToken) {
          setSessionMarker(true);
          await refreshProfile();
        }
      } catch {
        // No valid refresh cookie; user needs to log in.
        setUser(null);
        setSessionMarker(false);
      } finally {
        setIsLoading(false);
      }
    };
    recoverSession();
  }, [refreshProfile]);

  const startSession = async (accessToken?: string) => {
    if (accessToken) {
      setAccessToken(accessToken);
      setSessionMarker(true);
      await refreshProfile();
    }
  };

  const login = async (credentials: LoginRequest): Promise<LoginResult> => {
    const { data } = await authApi.login(credentials);

    if (data.requiresTwoFactor) {
      if (!data.twoFactorChallengeToken) {
        throw new Error('Respuesta de inicio de sesión sin desafío de verificación.');
      }

      // The challenge only lives in memory for the login step; it is never persisted.
      return { requiresTwoFactor: true, twoFactorChallengeToken: data.twoFactorChallengeToken };
    }

    await startSession(data.accessToken);
    return { requiresTwoFactor: false };
  };

  const completeTwoFactorLogin = async (request: TwoFactorLoginRequest) => {
    const { data } = await authApi.loginTwoFactor(request);
    await startSession(data.accessToken);
  };

  const logout = async () => {
    try {
      await authApi.logout();
    } finally {
      clearSession();
      broadcastSessionChange({ type: 'logout' });
    }
  };

  const switchTenant = async (tenantId: string) => {
    const { data } = await authApi.switchTenant(tenantId);
    if (data.accessToken) {
      setAccessToken(data.accessToken);
      setSessionMarker(true);
      // Every loaded list, hub group and draft belongs to the previous tenant: start over, here and in
      // the other tabs (their next refresh would silently move them to the new tenant).
      broadcastSessionChange({ type: 'tenant-switched' });
      reloadForTenantChange();
    }
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated: !!user,
        isLoading,
        login,
        completeTwoFactorLogin,
        logout,
        refreshProfile,
        switchTenant,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}
