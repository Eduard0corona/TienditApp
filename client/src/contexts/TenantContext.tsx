import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { tenantApi } from '../api/tenantApi';
import { useAuth } from '../hooks/useAuth';
import { TenantContext } from './TenantContextDefinition';
import type { TenantProfileDto } from '../types';

export function TenantProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading: isAuthLoading } = useAuth();
  const [tenant, setTenant] = useState<TenantProfileDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const refreshTenant = useCallback(async () => {
    if (!isAuthenticated) {
      setTenant(null);
      setIsLoading(false);
      return;
    }

    try {
      const { data } = await tenantApi.getMyTenant();
      setTenant(data);
    } catch {
      setTenant(null);
    } finally {
      setIsLoading(false);
    }
  }, [isAuthenticated]);

  useEffect(() => {
    if (isAuthLoading) return;
    setIsLoading(true);
    refreshTenant();
  }, [isAuthLoading, refreshTenant]);

  return (
    <TenantContext.Provider
      value={{
        tenant,
        capabilities: tenant?.capabilities ?? null,
        isLoading,
        refreshTenant,
      }}
    >
      {children}
    </TenantContext.Provider>
  );
}
