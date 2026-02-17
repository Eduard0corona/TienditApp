import { createContext } from 'react';
import type { LoginRequest, UserProfileDto } from '../types';

export interface AuthContextType {
  user: UserProfileDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (data: LoginRequest) => Promise<{ requiresTwoFactor: boolean; userId?: string }>;
  logout: () => Promise<void>;
  refreshProfile: () => Promise<void>;
  switchTenant: (tenantId: string) => Promise<void>;
}

export const AuthContext = createContext<AuthContextType>(null!);
