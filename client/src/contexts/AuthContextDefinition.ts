import { createContext } from 'react';
import type { LoginRequest, TwoFactorLoginRequest, UserProfileDto } from '../types';

export type LoginResult =
  | { requiresTwoFactor: false }
  | { requiresTwoFactor: true; twoFactorChallengeToken: string };

export interface AuthContextType {
  user: UserProfileDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (data: LoginRequest) => Promise<LoginResult>;
  completeTwoFactorLogin: (data: TwoFactorLoginRequest) => Promise<void>;
  logout: () => Promise<void>;
  refreshProfile: () => Promise<void>;
  switchTenant: (tenantId: string) => Promise<void>;
}

export const AuthContext = createContext<AuthContextType>(null!);
