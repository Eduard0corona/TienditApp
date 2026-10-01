import { Navigate } from 'react-router-dom';
import LoadingSpinner from './LoadingSpinner';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getLandingPath } from '../../utils/landing';

export default function HomeRedirect() {
  const { user, isAuthenticated, isLoading } = useAuth();
  const { capabilities, isLoading: isTenantLoading } = useTenant();

  if (isLoading || (isAuthenticated && isTenantLoading)) return <LoadingSpinner />;
  if (!isAuthenticated) return <Navigate to="/login" replace />;

  return <Navigate to={getLandingPath(user, capabilities)} replace />;
}
