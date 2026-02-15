import { createContext, useState, useEffect, useCallback, type ReactNode } from 'react';
import { authApi } from '../api/authApi';
import { profileApi } from '../api/profileApi';
import { setAccessToken } from '../api/axios';
import type { UserProfileDto, LoginRequest } from '../types';

interface AuthContextType {
  user: UserProfileDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (data: LoginRequest) => Promise<{ requiresTwoFactor: boolean; userId?: string }>;
  logout: () => Promise<void>;
  refreshProfile: () => Promise<void>;
}

export const AuthContext = createContext<AuthContextType>(null!);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserProfileDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const refreshProfile = useCallback(async () => {
    try {
      const { data } = await profileApi.getProfile();
      setUser(data);
    } catch {
      setUser(null);
      setAccessToken(null);
    }
  }, []);

  // On mount: try to recover session via refresh token cookie
  useEffect(() => {
    const recoverSession = async () => {
      try {
        const { data } = await authApi.refreshToken();
        if (data.accessToken) {
          setAccessToken(data.accessToken);
          await refreshProfile();
        }
      } catch {
        // No valid refresh cookie — user needs to log in
        setUser(null);
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
      await refreshProfile();
    }

    return { requiresTwoFactor: false };
  };

  const logout = async () => {
    try {
      await authApi.logout();
    } finally {
      setAccessToken(null);
      setUser(null);
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
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}
