import { Navigate } from 'react-router-dom';
import LoadingSpinner from './LoadingSpinner';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getLandingPath, hasUserPermission } from '../../utils/landing';

interface PermissionRouteProps {
  children: React.ReactNode;
  permission: string;
}

// Navigation aid only: the API is what enforces permissions.
export default function PermissionRoute({ children, permission }: PermissionRouteProps) {
  const { user } = useAuth();
  const { capabilities, isLoading } = useTenant();

  if (isLoading) return <LoadingSpinner />;
  if (!hasUserPermission(user, permission)) {
    return <Navigate to={getLandingPath(user, capabilities)} replace />;
  }

  return <>{children}</>;
}
