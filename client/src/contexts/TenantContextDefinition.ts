import { createContext } from 'react';
import type { TenantProfileDto } from '../types';

export interface TenantContextType {
  tenant: TenantProfileDto | null;
  capabilities: number | null;
  isLoading: boolean;
  refreshTenant: () => Promise<void>;
}

export const TenantContext = createContext<TenantContextType>(null!);
