import { Navigate } from 'react-router-dom';
import LoadingSpinner from './LoadingSpinner';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess, getLandingPath } from '../../utils/access';

export default function HomeRedirect() {
  const { user, isAuthenticated, isLoading } = useAuth();
  const { tenant, capabilities, isLoading: isTenantLoading } = useTenant();

  if (isLoading || (isAuthenticated && isTenantLoading)) return <LoadingSpinner />;
  if (!isAuthenticated) return <Navigate to="/login" replace />;

  // A new business starts with the guided setup until its owner finishes or skips it.
  if (tenant && !tenant.onboardingCompletedAt && getAccess(user, capabilities).businessSettings) {
    return <Navigate to="/setup" replace />;
  }

  return <Navigate to={getLandingPath(user, capabilities)} replace />;
}
