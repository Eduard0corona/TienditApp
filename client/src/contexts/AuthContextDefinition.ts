import { createContext } from 'react';
import type { LoginRequest, TwoFactorLoginRequest, UserProfileDto } from '../types';

export interface AcceptInvitationRequest {
  userId: string;
  token: string;
  newPassword: string;
}

export type LoginResult =
  | { requiresTwoFactor: false }
  | { requiresTwoFactor: true; twoFactorChallengeToken: string };

export interface AuthContextType {
  user: UserProfileDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (data: LoginRequest) => Promise<LoginResult>;
  completeTwoFactorLogin: (data: TwoFactorLoginRequest) => Promise<void>;
  /** The invited person chooses their password and the session starts. */
  acceptInvitation: (data: AcceptInvitationRequest) => Promise<void>;
  logout: () => Promise<void>;
  refreshProfile: () => Promise<void>;
  switchTenant: (tenantId: string) => Promise<void>;
}

export const AuthContext = createContext<AuthContextType>(null!);
