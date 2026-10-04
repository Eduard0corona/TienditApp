import { Navigate } from 'react-router-dom';
import LoadingSpinner from './LoadingSpinner';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess, getLandingPath, type AccessArea } from '../../utils/access';

interface AccessRouteProps {
  children: React.ReactNode;
  area: AccessArea;
}

// Navigation aid only: the API is what enforces roles, permissions and modules. A page the user cannot
// use sends them to their landing page instead of loading a screen whose requests answer 403.
export default function AccessRoute({ children, area }: AccessRouteProps) {
  const { user } = useAuth();
  const { capabilities, isLoading } = useTenant();

  if (isLoading) return <LoadingSpinner />;
  if (!getAccess(user, capabilities)[area]) {
    return <Navigate to={getLandingPath(user, capabilities)} replace />;
  }

  return <>{children}</>;
}
