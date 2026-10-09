import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { tenantApi } from '../api/tenantApi';
import { useAuth } from '../hooks/useAuth';
import { TenantContext } from './TenantContextDefinition';
import type { TenantProfileDto } from '../types';
import { setBusinessTimeZone } from '../utils/dates';
import { applyBusinessTheme } from '../utils/theme';

export function TenantProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading: isAuthLoading } = useAuth();
  const [tenant, setTenant] = useState<TenantProfileDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const refreshTenant = useCallback(async () => {
    if (!isAuthenticated) {
      setBusinessTimeZone(null);
      applyBusinessTheme(null);
      setTenant(null);
      setIsLoading(false);
      return;
    }

    try {
      const { data } = await tenantApi.getMyTenant();
      // Set before the screens render with the business: their dates are in its zone and its colors from the first paint.
      setBusinessTimeZone(data.timeZone);
      applyBusinessTheme(data.theme);
      setTenant(data);
    } catch {
      setBusinessTimeZone(null);
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
