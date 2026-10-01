import { useState, useEffect, useCallback, type ReactNode } from 'react';
import { authApi } from '../api/authApi';
import { profileApi } from '../api/profileApi';
import { refreshAccessToken, setAccessToken } from '../api/axios';
import type { LoginRequest, UserProfileDto } from '../types';
import { AuthContext } from './AuthContextDefinition';
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
  }, []);

  useSessionSync(clearSession);

  const refreshProfile = useCallback(async () => {
    try {
      const { data } = await profileApi.getProfile();
      setUser(data);
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

  const login = async (credentials: LoginRequest) => {
    const { data } = await authApi.login(credentials);

    if (data.requiresTwoFactor) {
      return { requiresTwoFactor: true, userId: data.userId ?? undefined };
    }

    if (data.accessToken) {
      setAccessToken(data.accessToken);
      setSessionMarker(true);
      await refreshProfile();
    }

    return { requiresTwoFactor: false };
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
        logout,
        refreshProfile,
        switchTenant,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}
