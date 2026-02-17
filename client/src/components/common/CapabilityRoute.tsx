import { Navigate, useLocation } from 'react-router-dom';
import LoadingSpinner from './LoadingSpinner';
import { useTenant } from '../../hooks/useTenant';
import { hasAnyCapability, hasCapability } from '../../utils/tenantCapabilities';

interface CapabilityRouteProps {
  children: React.ReactNode;
  required: number;
  mode?: 'all' | 'any';
}

export default function CapabilityRoute({ children, required, mode = 'all' }: CapabilityRouteProps) {
  const { capabilities, isLoading } = useTenant();
  const location = useLocation();

  if (isLoading) return <LoadingSpinner />;

  if (capabilities == null) {
    return <Navigate to="/dashboard" state={{ from: location }} replace />;
  }

  const allowed = mode === 'any'
    ? hasAnyCapability(capabilities, required)
    : hasCapability(capabilities, required);

  if (!allowed) {
    return <Navigate to="/dashboard" state={{ from: location }} replace />;
  }

  return <>{children}</>;
}
