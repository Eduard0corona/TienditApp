import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import LoadingSpinner from './LoadingSpinner';
import { useAuth } from '../../hooks/useAuth';
import { APP_TITLE, useDocumentTitle } from '../../hooks/useDocumentTitle';
import { useTenant } from '../../hooks/useTenant';
import { getAccess, getLandingPath } from '../../utils/access';

interface HomeRedirectProps {
  /** What a visitor sees here instead of going to Explorar: the page for businesses, at `/`. */
  visitor?: ReactNode;
}

export default function HomeRedirect({ visitor }: HomeRedirectProps) {
  const { user, isAuthenticated, isLoading } = useAuth();
  const { tenant, capabilities, isLoading: isTenantLoading } = useTenant();
  // Shown while the session is checked too, so a visitor does not wait on a spinner; a session that turns up goes on
  // to where it always went.
  const showsVisitor = visitor !== undefined && !isAuthenticated;
  // The server writes the head of the page for businesses for / (Fase 24D-2): it titles itself, and the team goes to
  // its job with the app's own title.
  useDocumentTitle(showsVisitor ? null : APP_TITLE);

  if (showsVisitor) return visitor;
  if (isLoading || (isAuthenticated && isTenantLoading)) return <LoadingSpinner />;
  // Customers start in Explorar, and so do visitors outside / (ADR 0021); the team of a business, at its job.
  if (!isAuthenticated || getAccess(user, capabilities).myOrders) return <Navigate to="/explorar" replace />;

  // A new business starts with the guided setup until its owner finishes or skips it.
  if (tenant && !tenant.onboardingCompletedAt && getAccess(user, capabilities).businessSettings) {
    return <Navigate to="/setup" replace />;
  }

  return <Navigate to={getLandingPath(user, capabilities)} replace />;
}
